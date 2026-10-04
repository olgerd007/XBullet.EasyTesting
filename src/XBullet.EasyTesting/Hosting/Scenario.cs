namespace XBullet.EasyTesting.Hosting;

/// <summary>Provides a single-use arrangement lifecycle for a test-project-specific domain scenario.</summary>
/// <typeparam name="TEntryPoint">The application entry-point type of the borrowed scenario scope.</typeparam>
/// <remarks>
/// The caller owns the scope and must keep it alive throughout arrangement and subsequent operations.
/// Use <see cref="ArrangeAsync"/> for workflows with multiple operations, or <see cref="Arrange"/>
/// to attach arrangement to one HTTP request. Instances are mutable and must not be used concurrently.
/// </remarks>
public abstract class Scenario<TEntryPoint>
    where TEntryPoint : class
{
    private bool _arranged;

    /// <summary>Creates a domain scenario associated with an existing isolated scope.</summary>
    /// <param name="scope">The non-null caller-owned scope. This scenario never disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    protected Scenario(TestScenarioScope<TEntryPoint> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
    }

    /// <summary>Gets the isolated scope used by domain setup and operations.</summary>
    /// <value>The borrowed scope supplied to the constructor. The caller owns and disposes it.</value>
    protected TestScenarioScope<TEntryPoint> Scope { get; }

    /// <summary>Freezes domain configuration and defers its arrangement until a request executes.</summary>
    /// <returns>
    /// A new single-use HTTP request builder. Its execution runs domain arrangement before sending
    /// the request. The caller owns the execution result and must dispose it.
    /// </returns>
    /// <exception cref="InvalidOperationException">Arrangement has already been selected or started.</exception>
    /// <remarks>
    /// Calling this method consumes the scenario even if the returned builder is never executed.
    /// Use <see cref="ArrangeAsync"/> when arrangement should run without an HTTP request.
    /// </remarks>
    public TestScenarioBuilder<TEntryPoint> Arrange()
    {
        EnsureNotArranged();
        _arranged = true;
        return Scope.Scenario().Arrange(RunArrangementAsync);
    }

    /// <summary>Freezes domain configuration and runs arrangement once without sending a request.</summary>
    /// <param name="cancellationToken">
    /// Cancels arrangement. The token is checked before invoking the domain callback and then passed
    /// to it. Cancellation or failure still consumes this scenario; use a new instance to retry.
    /// </param>
    /// <returns>A task that completes after domain arrangement has finished.</returns>
    /// <exception cref="InvalidOperationException">Arrangement has already been selected or started.</exception>
    public async Task ArrangeAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotArranged();
        _arranged = true;
        await RunArrangementAsync(cancellationToken);
    }

    /// <summary>Rejects domain configuration changes after arrangement has been selected or started.</summary>
    /// <exception cref="InvalidOperationException">Arrangement has already been selected or started.</exception>
    /// <remarks>Call this from derived configuration methods before changing domain state.</remarks>
    protected void EnsureNotArranged()
    {
        if (_arranged)
        {
            throw new InvalidOperationException("The domain scenario has already been arranged.");
        }
    }

    /// <summary>Applies the domain's configured database state, stubs, or other arrangements.</summary>
    /// <param name="cancellationToken">The arrangement token to pass to asynchronous setup operations.</param>
    /// <returns>A non-null task that completes after setup finishes.</returns>
    /// <remarks>
    /// Invoked once after configuration is frozen, either by <see cref="ArrangeAsync"/> or during
    /// execution of the builder returned by <see cref="Arrange"/>. This callback does not own the scope.
    /// </remarks>
    protected abstract Task ArrangeCoreAsync(CancellationToken cancellationToken);

    private Task RunArrangementAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ArrangeCoreAsync(cancellationToken);
    }
}
