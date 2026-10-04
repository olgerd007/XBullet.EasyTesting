namespace XBullet.EasyTesting.Hosting;

/// <summary>Provides a factory and an isolated scope runner for integration-test classes.</summary>
/// <typeparam name="TEntryPoint">The application entry-point type used by the factory.</typeparam>
/// <typeparam name="TFactory">The concrete factory type, including any test-project-specific helpers.</typeparam>
/// <remarks>
/// This base is independent of test frameworks. The caller owns the factory, and each
/// <c>RunAsync</c> call creates and cleans up a fresh scenario scope using the factory's existing
/// runner. No scope is stored on the test class between calls.
/// </remarks>
public abstract class ScopedTest<TEntryPoint, TFactory>
    where TEntryPoint : class
    where TFactory : AuthenticatedWebApplicationFactory<TEntryPoint>
{
    /// <summary>Creates a test base that borrows a factory and a test cancellation token.</summary>
    /// <param name="factory">The non-null caller-owned factory. This base never disposes it.</param>
    /// <param name="cancellationToken">
    /// The token used by every scope run on this instance. Pass the test framework's current test
    /// token when available. The default token does not request cancellation.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    protected ScopedTest(TFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
        TestCancellationToken = cancellationToken;
    }

    /// <summary>Gets the factory and its test-project-specific helpers.</summary>
    /// <value>The borrowed factory supplied to the constructor. The caller owns and disposes it.</value>
    protected TFactory Factory { get; }

    /// <summary>Gets the token passed to scope creation and test callbacks.</summary>
    /// <value>The token supplied to the constructor, or a non-cancelable token when omitted.</value>
    protected CancellationToken TestCancellationToken { get; }

    /// <summary>Runs a test in a fresh scope using factory-level configuration.</summary>
    /// <param name="test">The non-null callback borrowing the new scope and test cancellation token.</param>
    /// <returns>A task that completes after the callback and scope cleanup finish.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test"/> is <see langword="null"/>.</exception>
    protected Task RunAsync(Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task> test) =>
        RunAsync(test, null);

    /// <summary>Runs a test in a fresh isolated scope and captures failure diagnostics before cleanup.</summary>
    /// <param name="test">
    /// The non-null callback invoked once with the new scope and <see cref="TestCancellationToken"/>.
    /// The scope is borrowed for the callback's lifetime and must not escape it.
    /// </param>
    /// <param name="configure">
    /// Configures the new scope's service, configuration, and resource overrides. When
    /// <see langword="null"/>, factory-level configuration is used.
    /// </param>
    /// <returns>A task that completes after the callback and scope cleanup finish.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Runs through the factory's scenario gate. On failure or cancellation, diagnostics and cleanup
    /// use a non-cancelable token, and the original test exception is preserved.
    /// </remarks>
    protected Task RunAsync(
        Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task> test,
        Action<TestScenarioScopeBuilder>? configure) =>
        Factory.RunInTestScenarioScopeAsync(test, configure, TestCancellationToken);

    /// <summary>Runs a test function in a fresh scope using factory-level configuration.</summary>
    /// <typeparam name="TResult">The detached value produced by the test function.</typeparam>
    /// <param name="test">The non-null callback borrowing the new scope and test cancellation token.</param>
    /// <returns>The callback's value after scope cleanup finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test"/> is <see langword="null"/>.</exception>
    protected Task<TResult> RunAsync<TResult>(
        Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task<TResult>> test) => RunAsync(test, null);

    /// <summary>Runs a test function in a fresh isolated scope and returns its value after cleanup.</summary>
    /// <typeparam name="TResult">The value produced by the test function.</typeparam>
    /// <param name="test">
    /// The non-null callback invoked once with the new scope and <see cref="TestCancellationToken"/>.
    /// Return detached data; the scope and its services or resources are disposed before this method completes.
    /// </param>
    /// <param name="configure">
    /// Configures the new scope's service, configuration, and resource overrides. When
    /// <see langword="null"/>, factory-level configuration is used.
    /// </param>
    /// <returns>The callback's value after scope cleanup finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Uses the same gate, failure diagnostics, cancellation, and cleanup behavior as the
    /// non-result overload.
    /// </remarks>
    protected Task<TResult> RunAsync<TResult>(
        Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task<TResult>> test,
        Action<TestScenarioScopeBuilder>? configure) =>
        Factory.RunInTestScenarioScopeAsync(test, configure, TestCancellationToken);
}
