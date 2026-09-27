namespace XBullet.EasyTesting.Azure;

/// <summary>An immutable token request captured by <see cref="TestTokenCredential"/>.</summary>
public sealed class TestTokenRequest
{
    internal TestTokenRequest(
        IReadOnlyList<string> scopes,
        string? parentRequestId,
        string? claims,
        string? tenantId)
    {
        Scopes = scopes;
        ParentRequestId = parentRequestId;
        Claims = claims;
        TenantId = tenantId;
    }

    /// <summary>Gets the requested OAuth scopes.</summary>
    /// <value>
    /// A stable, read-only snapshot of scopes in the order supplied to the Azure SDK token request.
    /// </value>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>Gets the parent request identifier, when supplied.</summary>
    /// <value>The parent request identifier, or <see langword="null"/> when none was supplied.</value>
    public string? ParentRequestId { get; }

    /// <summary>Gets claims requested by a challenge, when supplied.</summary>
    /// <value>
    /// The unredacted claims challenge, or <see langword="null"/> when none was supplied. Claims may
    /// contain sensitive data and are omitted from captured scenario diagnostics.
    /// </value>
    public string? Claims { get; }

    /// <summary>Gets the requested tenant identifier, when supplied.</summary>
    /// <value>The tenant identifier, or <see langword="null"/> when none was supplied.</value>
    public string? TenantId { get; }
}
