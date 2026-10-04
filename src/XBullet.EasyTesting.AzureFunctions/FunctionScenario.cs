namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides guarded, single-use domain arrangement for a Functions scenario.</summary>
/// <remarks>The caller owns the borrowed scope. Instances must not be configured or arranged concurrently.</remarks>
public abstract class FunctionScenario
{
    private bool _arranged;

    /// <summary>Creates a domain scenario associated with an active Functions scope.</summary>
    /// <param name="scope">The non-null caller-owned scope, which this helper never disposes.</param>
    protected FunctionScenario(AzureFunctionTestScenarioScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
    }

    /// <summary>Gets the borrowed Functions scenario scope.</summary>
    /// <value>The caller-owned scope kept alive throughout setup and invocations.</value>
    protected AzureFunctionTestScenarioScope Scope { get; }

    /// <summary>Freezes configuration and performs arrangement once.</summary>
    /// <param name="cancellationToken">The token checked before setup and passed to the domain callback.</param>
    /// <returns>A task completing when arrangement finishes.</returns>
    /// <remarks>Failure or cancellation consumes the helper. Create a new instance to retry.</remarks>
    public async Task ArrangeAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotArranged();
        _arranged = true;
        _ = Scope.Host;
        cancellationToken.ThrowIfCancellationRequested();
        await ArrangeCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rejects configuration changes after arrangement starts.</summary>
    /// <remarks>Call from domain configuration methods before changing their state.</remarks>
    protected void EnsureNotArranged()
    {
        if (_arranged)
        {
            throw new InvalidOperationException("The domain scenario has already been arranged.");
        }
    }

    /// <summary>Applies the domain's configured stubs, resource state, or persisted data.</summary>
    /// <param name="cancellationToken">The arrangement cancellation token.</param>
    /// <returns>A non-null task completing when domain setup finishes.</returns>
    protected abstract Task ArrangeCoreAsync(CancellationToken cancellationToken);
}
