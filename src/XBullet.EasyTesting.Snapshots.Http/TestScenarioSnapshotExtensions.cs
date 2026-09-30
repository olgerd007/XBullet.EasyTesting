using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Snapshots;

#pragma warning disable RS0026, RS0027 // A required callback distinguishes the configuration overload.

/// <summary>Creates fluent scenarios configured for complete HTTP exchange snapshots.</summary>
public static class TestScenarioSnapshotExtensions
{
    /// <summary>
    /// Starts a fluent snapshot scenario and configures its recorder options inline.
    /// </summary>
    /// <typeparam name="TEntryPoint">The application entry-point type hosted by the scenario scope.</typeparam>
    /// <param name="scope">The non-null, active scenario scope that creates the client.</param>
    /// <param name="configureExchange">
    /// A non-null callback invoked once with independent options based on the effective global or
    /// package defaults before the recorder is created.
    /// </param>
    /// <returns>
    /// A new single-use scenario builder with a configured <see cref="HttpExchangeRecorder"/>
    /// attached.
    /// </returns>
    public static TestScenarioBuilder<TEntryPoint> SnapshotScenario<TEntryPoint>(
        this TestScenarioScope<TEntryPoint> scope,
        Action<HttpExchangeSnapshotOptions> configureExchange)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(configureExchange);
        return SnapshotScenario(
            scope,
            HttpExchangeSnapshotOptionsDefaults.ExtendGlobal(configureExchange));
    }

    /// <summary>
    /// Starts a fluent scenario whose client records the request before transport and associates
    /// the captured exchange with its response.
    /// </summary>
    /// <typeparam name="TEntryPoint">The application entry-point type hosted by the scenario scope.</typeparam>
    /// <param name="scope">The non-null, active scenario scope that creates the client.</param>
    /// <param name="options">
    /// Optional capture, redaction, and format options. <see langword="null"/> resolves the effective
    /// global or package defaults when the recorder is created.
    /// </param>
    /// <returns>
    /// A new single-use scenario builder with an <see cref="HttpExchangeRecorder"/> attached.
    /// </returns>
    public static TestScenarioBuilder<TEntryPoint> SnapshotScenario<TEntryPoint>(
        this TestScenarioScope<TEntryPoint> scope,
        HttpExchangeSnapshotOptions? options = null)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Scenario().WithHandler(new HttpExchangeRecorder(options));
    }
}

#pragma warning restore RS0026, RS0027
