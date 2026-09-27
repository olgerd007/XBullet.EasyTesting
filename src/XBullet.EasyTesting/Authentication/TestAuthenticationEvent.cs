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
/// <param name="Kind">The authentication operation that produced the event.</param>
/// <param name="AuthenticationScheme">
/// The case-sensitive scheme name of the real authentication handler that produced the event.
/// </param>
/// <param name="Method">The HTTP request method associated with the event.</param>
/// <param name="Path">The request path without the query string.</param>
/// <param name="Timestamp">
/// The absolute time at which the event was recorded. Recorder-created events use UTC.
/// </param>
/// <param name="FailureType">
/// The validation exception's fully qualified type name, or <see langword="null"/> when the event
/// has no captured failure. The default is <see langword="null"/>.
/// </param>
/// <param name="FailureMessage">
/// The redacted validation-failure message, or <see langword="null"/> when the event has no
/// captured failure. The default is <see langword="null"/>.
/// </param>
public sealed record TestAuthenticationEvent(
    TestAuthenticationEventKind Kind,
    string AuthenticationScheme,
    string Method,
    string Path,
    DateTimeOffset Timestamp,
    string? FailureType = null,
    string? FailureMessage = null);
