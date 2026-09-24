using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Records HTTP exchanges without replacing the underlying transport. Add this handler to a real
/// or in-memory client pipeline to snapshot requests, responses, and send failures.
/// </summary>
public sealed class HttpExchangeRecorder : DelegatingHandler
{
    private static readonly object RecordedExchangeGate = new();
    private static readonly ConditionalWeakTable<HttpResponseMessage, RecordedExchange>
        RecordedExchanges = new();
    private readonly object _gate = new();
    private readonly List<PendingExchange> _exchanges = [];

    /// <summary>Creates a recorder with optional request and response capture settings.</summary>
    public HttpExchangeRecorder(HttpExchangeSnapshotOptions? options = null) =>
        Options = options ?? new HttpExchangeSnapshotOptions();

    /// <summary>Gets the options used to capture exchanges.</summary>
    public HttpExchangeSnapshotOptions Options { get; }

    /// <summary>Gets the number of requests observed since construction or the last reset.</summary>
    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _exchanges.Count;
            }
        }
    }

    /// <summary>Creates stable snapshots for all completed exchanges in request order.</summary>
    public Task<IReadOnlyList<HttpExchangeSnapshot>> CreateSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingExchange[] exchanges;
        lock (_gate)
        {
            exchanges = _exchanges.ToArray();
        }

        var snapshots = new HttpExchangeSnapshot[exchanges.Length];
        for (var index = 0; index < exchanges.Length; index++)
        {
            snapshots[index] = exchanges[index].CreateSnapshot();
        }

        return Task.FromResult<IReadOnlyList<HttpExchangeSnapshot>>(snapshots);
    }

    /// <summary>Clears all recorded exchanges and returns this recorder.</summary>
    public HttpExchangeRecorder Reset()
    {
        lock (_gate)
        {
            _exchanges.Clear();
        }

        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = await HttpExchangeSnapshot.CreateRequestAsync(
            request,
            Options.Request,
            cancellationToken);
        var exchange = new PendingExchange(capturedRequest);
        lock (_gate)
        {
            _exchanges.Add(exchange);
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var capturedResponse = await HttpExchangeSnapshot.CreateResponseAsync(
                response,
                Options.Response,
                cancellationToken);
            exchange.SetResponse(capturedResponse);
            Attach(response, exchange.CreateSnapshot());
            return response;
        }
        catch (Exception exception)
        {
            exchange.SetFailure(exception);
            throw;
        }
    }

    internal static bool TryGetRecordedSnapshot(
        HttpResponseMessage response,
        HttpExchangeSnapshotOptions? options,
        out HttpExchangeSnapshot snapshot)
    {
        RecordedExchange? recorded;
        lock (RecordedExchangeGate)
        {
            RecordedExchanges.TryGetValue(response, out recorded);
        }

        if (recorded is null)
        {
            snapshot = null!;
            return false;
        }

        if (options is not null && !ReferenceEquals(options, recorded.Options))
        {
            throw new InvalidOperationException(
                "This response was captured by an HttpExchangeRecorder with different options. " +
                "Configure the options when creating the recorder, then call the response " +
                "snapshot assertion without separate exchange options.");
        }

        snapshot = recorded.Snapshot;
        return true;
    }

    private void Attach(HttpResponseMessage response, HttpExchangeSnapshot snapshot)
    {
        lock (RecordedExchangeGate)
        {
            RecordedExchanges.Remove(response);
            RecordedExchanges.Add(response, new RecordedExchange(snapshot, Options));
        }
    }

    private sealed record RecordedExchange(
        HttpExchangeSnapshot Snapshot,
        HttpExchangeSnapshotOptions Options);

    private sealed class PendingExchange(HttpExchangeRequestSnapshot request)
    {
        private readonly object _gate = new();
        private HttpExchangeSnapshot? _snapshot;

        public void SetResponse(HttpExchangeResponseSnapshot response)
        {
            lock (_gate)
            {
                _snapshot = new HttpExchangeSnapshot(request, response, Failure: null);
            }
        }

        public void SetFailure(Exception exception)
        {
            lock (_gate)
            {
                _snapshot = new HttpExchangeSnapshot(
                    request,
                    Response: null,
                    HttpExchangeFailureSnapshot.FromException(exception));
            }
        }

        public HttpExchangeSnapshot CreateSnapshot()
        {
            lock (_gate)
            {
                return _snapshot ?? throw new InvalidOperationException(
                    "An HTTP exchange is still in progress and cannot be snapshotted.");
            }
        }
    }
}
