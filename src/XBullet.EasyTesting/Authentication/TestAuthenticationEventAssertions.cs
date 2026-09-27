namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently verifies authentication events recorded by a test scenario.</summary>
public sealed class TestAuthenticationEventAssertions
{
    private readonly TestAuthenticationEventRecorder _recorder;

    internal TestAuthenticationEventAssertions(TestAuthenticationEventRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <summary>Verifies that the expected number of challenge events occurred.</summary>
    /// <param name="exactly">
    /// The non-negative number of matching events required. The default is one.
    /// </param>
    /// <param name="authenticationScheme">
    /// The exact, case-sensitive scheme to match, or <see langword="null"/> to count events from
    /// every scheme. The default is <see langword="null"/>.
    /// </param>
    /// <returns>This assertion object so additional event checks can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exactly"/> is negative.</exception>
    /// <exception cref="TestAuthenticationEventVerificationException">
    /// The recorded challenge count does not equal <paramref name="exactly"/>.
    /// </exception>
    public TestAuthenticationEventAssertions HaveChallenge(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.Challenge, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of forbidden events occurred.</summary>
    /// <param name="exactly">
    /// The non-negative number of matching events required. The default is one.
    /// </param>
    /// <param name="authenticationScheme">
    /// The exact, case-sensitive scheme to match, or <see langword="null"/> to count events from
    /// every scheme. The default is <see langword="null"/>.
    /// </param>
    /// <returns>This assertion object so additional event checks can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exactly"/> is negative.</exception>
    /// <exception cref="TestAuthenticationEventVerificationException">
    /// The recorded forbidden-event count does not equal <paramref name="exactly"/>.
    /// </exception>
    public TestAuthenticationEventAssertions HaveForbidden(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.Forbidden, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of validation failures occurred.</summary>
    /// <param name="exactly">
    /// The non-negative number of matching events required. The default is one.
    /// </param>
    /// <param name="authenticationScheme">
    /// The exact, case-sensitive scheme to match, or <see langword="null"/> to count events from
    /// every scheme. The default is <see langword="null"/>.
    /// </param>
    /// <returns>This assertion object so additional event checks can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exactly"/> is negative.</exception>
    /// <exception cref="TestAuthenticationEventVerificationException">
    /// The recorded validation-failure count does not equal <paramref name="exactly"/>.
    /// </exception>
    public TestAuthenticationEventAssertions HaveValidationFailure(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.ValidationFailed, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of successful validations occurred.</summary>
    /// <param name="exactly">
    /// The non-negative number of matching events required. The default is one.
    /// </param>
    /// <param name="authenticationScheme">
    /// The exact, case-sensitive scheme to match, or <see langword="null"/> to count events from
    /// every scheme. The default is <see langword="null"/>.
    /// </param>
    /// <returns>This assertion object so additional event checks can be chained.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exactly"/> is negative.</exception>
    /// <exception cref="TestAuthenticationEventVerificationException">
    /// The recorded successful-validation count does not equal <paramref name="exactly"/>.
    /// </exception>
    public TestAuthenticationEventAssertions HaveValidatedCredential(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.TokenValidated, exactly, authenticationScheme);

    /// <summary>Verifies that no event of the supplied kind occurred.</summary>
    /// <param name="kind">The authentication-event kind that must be absent.</param>
    /// <param name="authenticationScheme">
    /// The exact, case-sensitive scheme to match, or <see langword="null"/> to inspect events from
    /// every scheme. The default is <see langword="null"/>.
    /// </param>
    /// <returns>This assertion object so additional event checks can be chained.</returns>
    /// <exception cref="TestAuthenticationEventVerificationException">
    /// At least one matching event was recorded.
    /// </exception>
    public TestAuthenticationEventAssertions NotHave(
        TestAuthenticationEventKind kind,
        string? authenticationScheme = null) =>
        Have(kind, exactly: 0, authenticationScheme);

    private TestAuthenticationEventAssertions Have(
        TestAuthenticationEventKind kind,
        int exactly,
        string? authenticationScheme)
    {
        if (exactly < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exactly));
        }

        var matching = _recorder.Events.Count(authenticationEvent =>
            authenticationEvent.Kind == kind &&
            (authenticationScheme is null || string.Equals(
                authenticationEvent.AuthenticationScheme,
                authenticationScheme,
                StringComparison.Ordinal)));
        if (matching != exactly)
        {
            throw new TestAuthenticationEventVerificationException(
                $"Expected exactly {exactly} '{kind}' authentication event(s)" +
                (authenticationScheme is null ? string.Empty : $" for scheme '{authenticationScheme}'") +
                $", but found {matching}.{Environment.NewLine}" +
                FormatEvents(_recorder.Events));
        }

        return this;
    }

    private static string FormatEvents(IReadOnlyList<TestAuthenticationEvent> events) =>
        events.Count == 0
            ? "No authentication events were recorded."
            : "Recorded events:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                events.Select(authenticationEvent =>
                    $"- {authenticationEvent.Kind} [{authenticationEvent.AuthenticationScheme}] " +
                    $"{authenticationEvent.Method} {authenticationEvent.Path}" +
                    (authenticationEvent.FailureType is null
                        ? string.Empty
                        : $" ({authenticationEvent.FailureType}: {authenticationEvent.FailureMessage})")));
}

/// <summary>Thrown when recorded authentication events do not satisfy a fluent assertion.</summary>
/// <param name="message">
/// The non-null assertion message containing the expected count and redacted recorded-event
/// metadata.
/// </param>
public sealed class TestAuthenticationEventVerificationException(string message) : Exception(message);
