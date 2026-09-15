namespace XBullet.EasyTesting.Authentication;

/// <summary>Identifies an authentication event captured by an end-to-end test handler.</summary>
public enum TestAuthenticationEventKind
{
    /// <summary>A credential was validated successfully.</summary>
    TokenValidated,

    /// <summary>Credential validation failed.</summary>
    ValidationFailed,

    /// <summary>The handler issued an authentication challenge.</summary>
    Challenge,

    /// <summary>The handler issued a forbidden response.</summary>
    Forbidden
}

/// <summary>Contains safe metadata captured from one real-handler authentication event.</summary>
public sealed record TestAuthenticationEvent(
    TestAuthenticationEventKind Kind,
    string AuthenticationScheme,
    string Method,
    string Path,
    DateTimeOffset Timestamp,
    string? FailureType = null,
    string? FailureMessage = null);
