using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using XBullet.EasyTesting.Azure;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class AzureTestingTests
{
    private const string SasSignature = "sensitive-sas-signature";
    private const string SasQuery = "sv=2024-11-04&sp=r&sig=" + SasSignature;

    [Fact]
    public void Response_exposes_content_headers_and_model_value()
    {
        using var raw = new TestAzureResponse(201)
            .WithHeader("x-ms-request-id", "request-42")
            .WithJsonContent(new { name = "Ada" });
        var response = raw.FromValue("created");

        Assert.Equal("created", response.Value);
        Assert.Equal(201, response.GetRawResponse().Status);
        Assert.Equal("request-42", response.GetRawResponse().Headers.RequestId);
        Assert.Equal("application/json", response.GetRawResponse().Headers.ContentType);
        Assert.Contains("Ada", response.GetRawResponse().Content.ToString());
    }

    [Fact]
    public async Task Pageable_helpers_preserve_pages_and_continuation_tokens()
    {
        var first = AzureTestData.Page([1, 2], "next");
        var last = AzureTestData.Page([3]);
        var pageable = AzureTestData.AsyncPageable(first, last);
        var pages = new List<Page<int>>();

        await foreach (var page in pageable.AsPages()
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal("next", pages[0].ContinuationToken);
        Assert.Equal([1, 2, 3], pages.SelectMany(page => page.Values));
    }

    [Fact]
    public async Task Credential_returns_deterministic_token_records_context_and_can_fail()
    {
        var expiresOn = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var credential = new TestTokenCredential("token-42", expiresOn);
        var context = new TokenRequestContext(
            ["https://storage.azure.com/.default"],
            parentRequestId: "parent-1",
            claims: "claims-1",
            tenantId: "tenant-1");

        var token = await credential.GetTokenAsync(
            context,
            TestContext.Current.CancellationToken);

        Assert.Equal("token-42", token.Token);
        Assert.Equal(expiresOn, token.ExpiresOn);
        var request = Assert.Single(credential.Requests);
        Assert.Equal(context.Scopes, request.Scopes);
        Assert.Equal("tenant-1", request.TenantId);

        credential.FailWith(new InvalidOperationException("Expected test failure."));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await credential.GetTokenAsync(
                context,
                TestContext.Current.CancellationToken));
        Assert.Equal(2, credential.RequestCount);
    }

    [Fact]
    public async Task Transport_exercises_real_blob_client_and_records_retries()
    {
        var transport = new StubAzureHttpPipelineTransport()
            .Respond(status: 500)
            .Respond(status: 200);
        var options = new BlobClientOptions
        {
            Transport = transport,
            Retry =
            {
                Delay = TimeSpan.Zero,
                MaxRetries = 1,
                Mode = RetryMode.Fixed
            }
        };
        var client = new BlobClient(
            new Uri("https://storage.test/container/blob"),
            new AzureSasCredential("sig=test"),
            options);

        var response = await client.ExistsAsync(TestContext.Current.CancellationToken);

        Assert.True(response.Value);
        Assert.Equal(2, transport.CallCount);
        Assert.Equal(0, transport.RemainingResponseCount);
        transport.VerifyCalled(RequestMethod.Head, "/container/blob?sig=test", expectedCount: 2);
    }

    [Fact]
    public async Task Transport_diagnostics_redact_query_header_values_and_content()
    {
        var transport = new StubAzureHttpPipelineTransport().Respond();
        var options = new BlobClientOptions { Transport = transport };
        var client = new BlobClient(
            new Uri("https://storage.test/container/blob"),
            new AzureSasCredential("sig=secret"),
            options);

        await client.ExistsAsync(TestContext.Current.CancellationToken);
        var diagnostics = await transport.CaptureDiagnosticsAsync(
            TestContext.Current.CancellationToken);
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostics);

        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", json, StringComparison.Ordinal);
        Assert.Contains("x-ms-version", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_verification_redacts_actual_and_expected_SAS_query_values()
    {
        var transport = new StubAzureHttpPipelineTransport().Respond();
        var client = CreateSasBlobClient(transport);

        await client.ExistsAsync(TestContext.Current.CancellationToken);

        var exactFailure = Assert.Throws<AzureTransportVerificationException>(() =>
            transport.VerifyCalled(
                RequestMethod.Head,
                "https://other.test/expected?sig=expected-signature&comp=properties"));
        var predicateFailure = Assert.Throws<AzureTransportVerificationException>(() =>
            transport.Verify(_ => false, expectedCount: 1, "the expected blob request"));

        Assert.DoesNotContain(SasSignature, exactFailure.Message);
        Assert.DoesNotContain("expected-signature", exactFailure.Message);
        Assert.DoesNotContain("https://storage.test", exactFailure.Message);
        Assert.DoesNotContain("https://other.test", exactFailure.Message);
        Assert.Contains(
            "/expected?sig={Redacted}&comp=properties",
            exactFailure.Message);
        Assert.Contains("/container/blob?comp=metadata", exactFailure.Message);
        Assert.Contains("sv={Redacted}", exactFailure.Message);
        Assert.Contains("sp={Redacted}", exactFailure.Message);
        Assert.Contains("comp=metadata", predicateFailure.Message);
        Assert.DoesNotContain(SasSignature, predicateFailure.Message);
    }

    [Fact]
    public async Task Unarranged_response_body_redacts_SAS_query_values()
    {
        var transport = new StubAzureHttpPipelineTransport();
        var client = CreateSasBlobClient(transport);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            async () => await client.ExistsAsync(TestContext.Current.CancellationToken));
        var body = exception.Message;

        Assert.Equal(501, exception.Status);
        Assert.DoesNotContain(SasSignature, body);
        Assert.DoesNotContain("https://storage.test", body);
        Assert.Contains("/container/blob?comp=metadata", body);
        Assert.Contains("sv={Redacted}", body);
        Assert.Contains("sp={Redacted}", body);
        Assert.Contains("sig={Redacted}", body);
    }

    [Fact]
    public async Task SAS_query_parameters_still_participate_in_request_matching()
    {
        var transport = new StubAzureHttpPipelineTransport().Respond();
        var client = CreateSasBlobClient(transport);

        await client.ExistsAsync(TestContext.Current.CancellationToken);

        transport.VerifyCalled(
            RequestMethod.Head,
            $"/container/blob?comp=metadata&{SasQuery}");
        var recorded = Assert.Single(transport.Requests);
        Assert.Contains(SasSignature, recorded.Uri.Query);
        Assert.Contains("comp=metadata", recorded.Uri.Query);
    }

    private static BlobClient CreateSasBlobClient(StubAzureHttpPipelineTransport transport)
    {
        var options = new BlobClientOptions
        {
            Transport = transport,
            Retry = { MaxRetries = 0 }
        };
        return new BlobClient(
            new Uri("https://storage.test/container/blob?comp=metadata"),
            new AzureSasCredential(SasQuery),
            options);
    }
}
