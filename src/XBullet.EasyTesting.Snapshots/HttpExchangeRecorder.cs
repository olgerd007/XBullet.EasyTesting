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
    private readonly HttpExchangeSnapshotOptions? _sourceOptions;

    /// <summary>Creates a recorder with optional request and response capture settings.</summary>
    public HttpExchangeRecorder(HttpExchangeSnapshotOptions? options = null)
    {
        _sourceOptions = options;
        Options = HttpExchangeSnapshotOptionsDefaults.MergeGlobalOrDefault(options);
    }

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
            var capturedResponse = HttpExchangeSnapshot.CreateResponse(
                response,
                Options.Response,
                Options.Response.IncludeBody ? "{NotRead}" : null);
            exchange.SetResponse(capturedResponse);

            if (Options.Response.IncludeBody && response.Content is not null)
            {
                var content = response.Content;
                var contentType = content.Headers.ContentType;
                response.Content = new RecordingHttpContent(
                    content,
                    (body, exception) => exchange.SetResponseBody(
                        body,
                        contentType,
                        exception));
            }

            Attach(response, exchange);
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

        if (!IsCompatible(options, recorded))
        {
            throw new InvalidOperationException(
                "This response was captured by an HttpExchangeRecorder with different options. " +
                "Configure the options when creating the recorder, then call the response " +
                "snapshot assertion without separate exchange options.");
        }

        snapshot = recorded.Exchange.CreateSnapshot();
        return true;
    }

    internal static HttpExchangeSnapshotFormat ResolveFormat(
        HttpResponseMessage response,
        HttpExchangeSnapshotOptions? options)
    {
        RecordedExchange? recorded;
        lock (RecordedExchangeGate)
        {
            RecordedExchanges.TryGetValue(response, out recorded);
        }

        if (recorded is null)
        {
            return HttpExchangeSnapshotOptionsDefaults.MergeGlobalOrDefault(options).Format;
        }

        if (!IsCompatible(options, recorded))
        {
            throw new InvalidOperationException(
                "This response was captured by an HttpExchangeRecorder with different options. " +
                "Configure the options when creating the recorder, then call the response " +
                "snapshot assertion without separate exchange options.");
        }

        return recorded.Options.Format;
    }

    private void Attach(HttpResponseMessage response, PendingExchange exchange)
    {
        lock (RecordedExchangeGate)
        {
            RecordedExchanges.Remove(response);
            RecordedExchanges.Add(response, new RecordedExchange(exchange, Options, _sourceOptions));
        }
    }

    private static bool IsCompatible(
        HttpExchangeSnapshotOptions? options,
        RecordedExchange recorded) =>
        options is null ||
        ReferenceEquals(options, recorded.Options) ||
        ReferenceEquals(options, recorded.SourceOptions);

    private sealed record RecordedExchange(
        PendingExchange Exchange,
        HttpExchangeSnapshotOptions Options,
        HttpExchangeSnapshotOptions? SourceOptions);

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

        public void SetResponseBody(
            byte[] body,
            System.Net.Http.Headers.MediaTypeHeaderValue? contentType,
            Exception? exception)
        {
            object? bodySnapshot;
            HttpExchangeFailureSnapshot? bodyFailure = exception is null
                ? null
                : HttpExchangeFailureSnapshot.FromException(exception);
            try
            {
                bodySnapshot = HttpExchangeSnapshot.CreateBodySnapshot(body, contentType);
            }
            catch (Exception captureException)
            {
                bodySnapshot = new ControllerBinaryBodySnapshot(
                    "base64",
                    Convert.ToBase64String(body));
                bodyFailure ??= HttpExchangeFailureSnapshot.FromException(captureException);
            }

            lock (_gate)
            {
                if (_snapshot?.Response is null)
                {
                    return;
                }

                _snapshot = _snapshot with
                {
                    Response = _snapshot.Response with
                    {
                        Body = bodySnapshot,
                        BodyFailure = bodyFailure
                    }
                };
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
