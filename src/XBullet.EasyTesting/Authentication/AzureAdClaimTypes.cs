namespace XBullet.EasyTesting.Authentication;

/// <summary>Common claim names emitted by Microsoft Entra ID (Azure AD) access tokens.</summary>
public static class AzureAdClaimTypes
{
    /// <summary>Object identifier of the principal.</summary>
    public const string ObjectId = "oid";

    /// <summary>Identifier of the tenant that issued the identity.</summary>
    public const string TenantId = "tid";

    /// <summary>Preferred username of a delegated user.</summary>
    public const string PreferredUsername = "preferred_username";

    /// <summary>Space-delimited delegated permissions.</summary>
    public const string Scope = "scp";

    /// <summary>Application roles assigned to the principal.</summary>
    public const string Roles = "roles";

    /// <summary>Authorized-party application identifier.</summary>
    public const string AuthorizedParty = "azp";
}
