using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Http;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class StubHttpMessageHandlerTests
{
    [Fact]
    public async Task Arranged_response_is_returned_and_request_is_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders?notify=true")
            .RespondJson(new { Accepted = true }, HttpStatusCode.Accepted);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

        using var response = await client.PostAsJsonAsync(
            "/orders?notify=true",
            new { OrderId = 42 },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(body!.Accepted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/orders?notify=true", request.RequestUri!.PathAndQuery);
        Assert.Equal(["tenant-42"], request.Headers["X-Tenant"]);
        Assert.Contains("\"orderId\":42", request.Body);
    }

    [Fact]
    public async Task Unmatched_request_returns_a_diagnostic_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/missing", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("No outbound HTTP stub matches GET /missing", body);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Unmatched_request_explains_why_each_rule_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .Respond(HttpStatusCode.Created)
            .When(HttpMethod.Get, "/customers")
            .Respond(HttpStatusCode.OK)
            .When(HttpMethod.Get, "/orders")
            .WithQueryParameter("page", "2")
            .WithRequestHeader("X-Tenant", "tenant-42")
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/orders?page=3", cancellationToken);
        var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("Rule 1 (POST /orders): method differed", diagnostic);
        Assert.Contains("Rule 2 (GET /customers): URI differed", diagnostic);
        Assert.Contains(
            "Rule 3 (GET /orders): did not satisfy query parameter 'page' containing value '2'",
            diagnostic);
        Assert.Contains("did not satisfy header 'X-Tenant' containing value 'tenant-42'", diagnostic);
    }

    [Fact]
    public async Task Predicate_exception_is_reported_as_a_rule_mismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/orders")
            .WithRequest(
                _ => throw new InvalidOperationException("predicate failed"),
                "valid order request")
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/orders", cancellationToken);
        var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains(
            "valid order request threw InvalidOperationException: predicate failed",
            diagnostic);
    }

    [Fact]
    public async Task Request_matchers_support_headers_json_and_dynamic_responses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .WithRequestHeader("X-Tenant", "tenant-42")
            .WithJsonRequestBody(new { OrderId = 42 })
            .Respond(request => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { CapturedBody = request.Body })
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

        using var response = await client.PostAsJsonAsync(
            "/orders",
            new { OrderId = 42 },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        handler
            .VerifyCalled(HttpMethod.Post, "/orders")
            .Verify(
                request => request.Headers.ContainsKey("X-Tenant"),
                expectedCount: 1,
                description: "a request containing the tenant header");
    }

    [Fact]
    public async Task Request_predicate_mismatch_does_not_select_the_rule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .WithJsonRequestBody(new { OrderId = 42 })
            .Respond(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.PostAsJsonAsync(
            "/orders",
            new { OrderId = 99 },
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Json_property_and_path_matchers_support_values_predicates_and_arrays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .WithJsonProperty("tenantId", "tenant-42")
            .WithJsonProperty("version", element => element.GetInt32() >= 2)
            .WithJsonPath("$.customer.id", 701)
            .WithJsonPath("items[1].quantity", element => element.GetInt32() > 1)
            .WithJsonPath("$.metadata", new { Priority = "high" })
            .Respond(HttpStatusCode.Created);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.PostAsJsonAsync(
            "/orders",
            new
            {
                TenantId = "tenant-42",
                Version = 2,
                Customer = new { Id = 701 },
                Items = new[]
                {
                    new { Sku = "first", Quantity = 1 },
                    new { Sku = "second", Quantity = 3 }
                },
                Metadata = new { Priority = "high" }
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Json_path_mismatch_is_included_in_rule_diagnostics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .WithJsonPath("$.customer.id", 701)
            .Respond(HttpStatusCode.Created);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.PostAsJsonAsync(
            "/orders",
            new { Customer = new { Id = 999 } },
            cancellationToken);
        var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains(
            "did not satisfy JSON path '$.customer.id' equal to the configured value",
            diagnostic);
    }

    [Fact]
    public void Invalid_json_path_is_rejected_when_the_rule_is_configured()
    {
        using var handler = new StubHttpMessageHandler();

        var exception = Assert.Throws<ArgumentException>(() =>
            handler
                .When(HttpMethod.Post, "/orders")
                .WithJsonPath("$.items[]", "sku"));

        Assert.Contains("JSON path '$.items[]' is invalid", exception.Message);
    }

    [Fact]
    public async Task Query_parameter_matcher_ignores_order_and_decodes_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/search")
            .WithQueryParameter("term", "red shoes")
            .WithQueryParameter("page", "2")
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync(
            "/search?page=2&term=red+shoes",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Query_parameter_predicate_receives_all_repeated_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/products")
            .WithQueryParameter(
                "tag",
                values => values.SequenceEqual(["sale", "featured"]))
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var matchingResponse = await client.GetAsync(
            "/products?tag=sale&tag=featured",
            cancellationToken);
        using var mismatchingResponse = await client.GetAsync(
            "/products?tag=sale",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, matchingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, mismatchingResponse.StatusCode);
    }

    [Fact]
    public async Task Rule_without_query_matcher_keeps_exact_uri_matching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/search")
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/search?term=shoes", cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Arranged_exception_is_thrown_for_a_matching_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/unstable")
            .Throw(_ => new HttpRequestException("External service unavailable."));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("/unstable", cancellationToken));

        Assert.Equal("External service unavailable.", exception.Message);
        handler.VerifyCalled(HttpMethod.Get, "/unstable");
    }

    [Fact]
    public async Task Failed_verification_lists_recorded_requests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        using var response = await client.GetAsync("/actual", cancellationToken);

        var exception = Assert.Throws<StubHttpVerificationException>(
            () => handler.VerifyCalled(HttpMethod.Get, "/expected"));

        Assert.Contains("Expected GET /expected to be called 1 time(s)", exception.Message);
        Assert.Contains("- GET /actual", exception.Message);
    }

    [Fact]
    public async Task Exact_body_async_response_and_negative_verification_are_supported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Put, "/echo")
            .WithRequestBody("payload")
            .RespondAsync((request, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.Body!)
                });
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.PutAsync(
            "/echo",
            new StringContent("payload"),
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal("payload", responseBody);
        handler
            .VerifyCalled(HttpMethod.Put, "/echo")
            .VerifyNotCalled(HttpMethod.Get, "/echo");
    }

    [Fact]
    public async Task Response_sequence_returns_each_response_once_and_then_fails_loudly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/status")
            .RespondSequence(sequence => sequence
                .Respond(HttpStatusCode.ServiceUnavailable)
                .RespondJson(new { Accepted = true }, HttpStatusCode.OK));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var first = await client.GetAsync("/status", cancellationToken);
        using var second = await client.GetAsync("/status", cancellationToken);
        var secondBody = await second.Content.ReadFromJsonAsync<Response>(cancellationToken);
        var exception = await Assert.ThrowsAsync<StubHttpSequenceExhaustedException>(
            () => client.GetAsync("/status", cancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(secondBody!.Accepted);
        Assert.Contains("contains 2 response(s), but call 3 was received", exception.Message);
    }

    [Fact]
    public async Task Delayed_response_observes_the_configured_delay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/slow")
            .WithDelay(TimeSpan.FromMilliseconds(50))
            .Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.GetAsync("/slow", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(35));
    }

    [Fact]
    public async Task Timeout_behaviors_support_deterministic_and_client_driven_timeouts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/deterministic-timeout")
            .TimeoutAfter(TimeSpan.FromMilliseconds(10))
            .When(HttpMethod.Get, "/client-timeout")
            .Timeout();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        var timeout = await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetAsync("/deterministic-timeout", cancellationToken));
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/client-timeout", timeoutCancellation.Token));

        Assert.Contains("timed out after", timeout.Message);
    }

    [Fact]
    public async Task Cancellation_behaviors_support_immediate_and_delayed_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/cancel")
            .Cancel()
            .When(HttpMethod.Get, "/cancel-after")
            .CancelAfter(TimeSpan.FromMilliseconds(10));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/cancel", cancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/cancel-after", cancellationToken));
    }

    [Fact]
    public async Task Malformed_json_response_fails_json_deserialization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/malformed")
            .RespondMalformedJson("{\"ready\":");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/malformed", cancellationToken);

        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => response.Content.ReadFromJsonAsync<Response>(cancellationToken));
    }

    [Fact]
    public void Malformed_json_response_rejects_valid_json()
    {
        using var handler = new StubHttpMessageHandler();

        var exception = Assert.Throws<ArgumentException>(() =>
            handler
                .When(HttpMethod.Get, "/not-malformed")
                .RespondMalformedJson("{\"ready\":true}"));

        Assert.Contains("must not be valid JSON", exception.Message);
    }

    [Fact]
    public async Task Truncated_response_throws_while_content_is_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/truncated")
            .RespondTruncated("{\"ready\":", mediaType: "application/json");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/truncated");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var exception = await Record.ExceptionAsync(
            () => response.Content.ReadAsStringAsync(cancellationToken));

        Assert.NotNull(exception);
        Assert.Contains("ended before its content was complete", exception.ToString());
    }

    private sealed record Response(bool Accepted);
}
