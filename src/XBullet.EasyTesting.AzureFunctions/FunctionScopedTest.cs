namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides a borrowed Functions host and an isolated scenario runner for test classes.</summary>
/// <remarks>Independent of test frameworks. Each run creates and cleans up one fresh scenario.</remarks>
public abstract class FunctionScopedTest
{
    /// <summary>Creates a test base that borrows a host and test cancellation token.</summary>
    /// <param name="host">The non-null caller-owned function host, which this base never disposes.</param>
    /// <param name="cancellationToken">The token used by scope initialization and each test callback.</param>
    protected FunctionScopedTest(AzureFunctionTestHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        TestCancellationToken = cancellationToken;
    }

    /// <summary>Gets the borrowed function host.</summary>
    /// <value>The caller-owned host supplied at construction.</value>
    protected AzureFunctionTestHost Host { get; }

    /// <summary>Gets the token used for every test run.</summary>
    /// <value>The caller-supplied token, or a non-cancelable token when omitted.</value>
    protected CancellationToken TestCancellationToken { get; }

    /// <summary>Runs a test in a fresh scenario with failure diagnostics and automatic cleanup.</summary>
    /// <param name="test">The non-null callback borrowing the scope and test token.</param>
    /// <returns>A task completing after the callback and cleanup finish.</returns>
    protected Task RunAsync(Func<AzureFunctionTestScenarioScope, CancellationToken, Task> test) =>
        Host.RunInTestScenarioScopeAsync(test, TestCancellationToken);

    /// <summary>Runs a test function and returns detached data after cleanup.</summary>
    /// <typeparam name="TResult">The detached result type; do not return scenario-owned resources.</typeparam>
    /// <param name="test">The non-null callback borrowing the scope and test token.</param>
    /// <returns>The callback's result after cleanup finishes.</returns>
    protected Task<TResult> RunAsync<TResult>(Func<AzureFunctionTestScenarioScope, CancellationToken, Task<TResult>> test) =>
        Host.RunInTestScenarioScopeAsync(test, TestCancellationToken);
}
