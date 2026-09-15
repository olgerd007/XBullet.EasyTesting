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
    public TestAuthenticationEventAssertions HaveChallenge(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.Challenge, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of forbidden events occurred.</summary>
    public TestAuthenticationEventAssertions HaveForbidden(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.Forbidden, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of validation failures occurred.</summary>
    public TestAuthenticationEventAssertions HaveValidationFailure(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.ValidationFailed, exactly, authenticationScheme);

    /// <summary>Verifies that the expected number of successful validations occurred.</summary>
    public TestAuthenticationEventAssertions HaveValidatedCredential(
        int exactly = 1,
        string? authenticationScheme = null) =>
        Have(TestAuthenticationEventKind.TokenValidated, exactly, authenticationScheme);

    /// <summary>Verifies that no event of the supplied kind occurred.</summary>
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
public sealed class TestAuthenticationEventVerificationException(string message) : Exception(message);
