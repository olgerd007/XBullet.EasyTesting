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
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>Gets the parent request identifier, when supplied.</summary>
    public string? ParentRequestId { get; }

    /// <summary>Gets claims requested by a challenge, when supplied.</summary>
    public string? Claims { get; }

    /// <summary>Gets the requested tenant identifier, when supplied.</summary>
    public string? TenantId { get; }
}
