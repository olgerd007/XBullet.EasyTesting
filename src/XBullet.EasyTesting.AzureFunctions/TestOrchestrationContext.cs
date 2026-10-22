using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>
/// An in-memory orchestration context with test-controlled input, time, events, and dispatch.
/// </summary>
/// <remarks>
/// Timers use manually advanced UTC time, events are buffered in memory, and replay state is a test flag.
/// Durable history, persistence, retries, and automatic replay are not emulated. Configure input,
/// identity, handlers, and logging before starting the orchestrator.
/// </remarks>
public sealed partial class TestOrchestrationContext : TaskOrchestrationContext
{
    private readonly object _recordingGate = new();
    private readonly List<RecordedActivityCall> _activityCalls = [];
    private readonly List<RecordedSubOrchestrationCall> _subOrchestrationCalls = [];
    private readonly List<RecordedOrchestrationEvent> _sentEvents = [];
    private readonly List<ContinueAsNewOptions> _continueAsNewRequests = [];
    private readonly Dictionary<string, Func<string, object?, Task<object?>>> _activityHandlers = new(StringComparer.Ordinal);
    private readonly TestOrchestrationScheduler _scheduler = new();
    private Func<RecordedActivityCall, Task<object?>> _activityHandler;
    private Func<RecordedSubOrchestrationCall, Task<object?>> _subOrchestratorHandler = call =>
        Task.FromResult(DefaultResult(call.ResultType));
    private object? _input;
    private string _instanceId = "test-orchestration";
    private bool _isReplaying;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private TaskName _name = new("TestOrchestrator");
    private ParentOrchestrationInstance? _parent;
    private string _version = string.Empty;
    private IReadOnlyDictionary<string, object?> _properties = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    private TaskOrchestrationEntityFeature _entities = new StubOrchestrationEntityFeature();
    private object? _customStatus;
    private Func<Guid>? _guidGenerator;
    private long _guidSequence;
    private Func<string, string, int> _versionComparer = StringComparer.Ordinal.Compare;
    private Action<object?>? _customStatusHandler;
    private Action<ContinueAsNewOptions>? _continueAsNewHandler;

    /// <summary>Creates an in-memory context whose activity and child handlers return default result values.</summary>
    public TestOrchestrationContext()
        : this(call => Task.FromResult(DefaultResult(call.ResultType)))
    {
    }

    /// <summary>Initializes a context that dispatches activity calls through <paramref name="activityHandler"/>.</summary>
    /// <param name="activityHandler">
    /// A non-null asynchronous callback retained by the context and invoked once for every scheduled
    /// activity after the call is recorded. It must return a value assignable to the requested result type.
    /// </param>
    public TestOrchestrationContext(Func<RecordedActivityCall, Task<object?>> activityHandler)
    {
        ArgumentNullException.ThrowIfNull(activityHandler);
        _activityHandler = activityHandler;
    }

    /// <summary>Gets or sets the delegate used to execute scheduled activities.</summary>
    /// <value>
    /// The non-null callback used for future calls. Assigning a new callback does not affect calls
    /// already recorded or awaiting a previous handler. Named activity registrations take precedence.
    /// </value>
    public Func<RecordedActivityCall, Task<object?>> ActivityHandler
    {
        get => _activityHandler;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _activityHandler = value;
        }
    }

    /// <summary>Gets activity calls in scheduling order.</summary>
    /// <value>
    /// A live read-only view in scheduling order. Calls retain their input and options without cloning.
    /// </value>
    public IReadOnlyList<RecordedActivityCall> ActivityCalls => _activityCalls;

    /// <summary>Gets or sets the callback invoked for each recorded sub-orchestrator call.</summary>
    /// <value>A non-null callback. The default returns the requested result type's default value.</value>
    public Func<RecordedSubOrchestrationCall, Task<object?>> SubOrchestratorHandler
    {
        get => _subOrchestratorHandler;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _subOrchestratorHandler = value;
        }
    }

    /// <summary>Gets child orchestration calls in scheduling order.</summary>
    /// <value>A live read-only view; inputs and options are retained without cloning.</value>
    public IReadOnlyList<RecordedSubOrchestrationCall> SubOrchestrationCalls => _subOrchestrationCalls;

    /// <summary>Gets outbound events in sending order.</summary>
    /// <value>A live read-only view. Events sent to this context's instance are also delivered locally.</value>
    public IReadOnlyList<RecordedOrchestrationEvent> SentEvents => _sentEvents;

    /// <summary>Gets continuation requests in invocation order without restarting the orchestrator.</summary>
    /// <value>Options copied on invocation; input objects are retained without cloning.</value>
    public IReadOnlyList<ContinueAsNewOptions> ContinueAsNewRequests => _continueAsNewRequests;

    /// <summary>Gets the most recent custom status supplied by the test or orchestrator.</summary>
    /// <value>The retained status object, initially null.</value>
    public object? CustomStatus => Volatile.Read(ref _customStatus);

    /// <summary>Sets the input returned by <see cref="GetInput{T}"/>.</summary>
    /// <param name="input">The input retained without serialization or cloning; null represents absent input.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithInput(object? input)
    {
        _input = input;
        return this;
    }

    /// <summary>Sets the orchestration instance identifier.</summary>
    /// <param name="instanceId">A non-empty identifier, compared ordinally when delivering self-directed events.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithInstanceId(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        _instanceId = instanceId;
        return this;
    }

    /// <summary>Sets the replay flag without replaying history or caching dispatch results.</summary>
    /// <param name="isReplaying">Whether replay-aware branches and logging should observe replay state.</param>
    /// <returns>This context, for chaining. Existing replay-safe loggers observe subsequent flag changes.</returns>
    public TestOrchestrationContext WithReplayState(bool isReplaying)
    {
        Volatile.Write(ref _isReplaying, isReplaying);
        return this;
    }

    /// <summary>Configures the underlying logger factory before replay-safe loggers are created.</summary>
    /// <param name="loggerFactory">The non-null caller-owned factory. The context does not dispose it.</param>
    /// <returns>This context, for chaining.</returns>
    public TestOrchestrationContext WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>Advances simulated time and completes timers already scheduled on or before the new time.</summary>
    /// <param name="utcDateTime">A UTC timestamp at or after the current time, initially the Unix epoch.</param>
    /// <returns>This context, for chaining. Timer continuations run asynchronously.</returns>
    public TestOrchestrationContext AdvanceTimeTo(DateTime utcDateTime)
    {
        _scheduler.AdvanceTo(utcDateTime);
        return this;
    }

    /// <summary>Advances simulated UTC time by a non-negative interval and completes due timers.</summary>
    /// <param name="duration">The non-negative simulated interval; no wall-clock delay occurs.</param>
    /// <returns>This context, for chaining. Timer continuations run asynchronously.</returns>
    public TestOrchestrationContext AdvanceTimeBy(TimeSpan duration)
    {
        _scheduler.AdvanceBy(duration);
        return this;
    }

    /// <summary>Delivers an external event to the oldest matching wait or buffers it until a wait is created.</summary>
    /// <param name="eventName">A non-empty case-insensitive event name.</param>
    /// <param name="payload">The event payload retained without cloning; null is allowed for nullable result types.</param>
    public void RaiseEvent(string eventName, object? payload) => _scheduler.RaiseEvent(eventName, payload);

    /// <inheritdoc />
    public override TaskName Name => _name;

    /// <inheritdoc />
    public override string InstanceId => _instanceId;

    /// <inheritdoc />
    public override ParentOrchestrationInstance? Parent => _parent;

    /// <inheritdoc />
    public override DateTime CurrentUtcDateTime => _scheduler.UtcNow;

    /// <summary>Gets the test-controlled replay flag.</summary>
    /// <value>False by default; set with <see cref="WithReplayState"/>. Durable history is not replayed.</value>
    public override bool IsReplaying => Volatile.Read(ref _isReplaying);

    /// <inheritdoc />
    public override string Version => _version;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> Properties => _properties;

    /// <inheritdoc />
    public override TaskOrchestrationEntityFeature Entities => _entities;

    /// <inheritdoc />
    public override ILoggerFactory ReplaySafeLoggerFactory => base.ReplaySafeLoggerFactory;

    /// <inheritdoc />
    protected override ILoggerFactory LoggerFactory => _loggerFactory;

    /// <summary>Gets input previously supplied by <see cref="WithInput"/> without serialization.</summary>
    /// <typeparam name="T">The type assignable from the supplied input.</typeparam>
    /// <returns>The retained input, or default when no input was supplied.</returns>
    /// <exception cref="InvalidOperationException">The non-null input cannot be assigned to T.</exception>
    public override T GetInput<T>() => _input is null ? default! : ConvertResult<T>(_input, "Orchestration input");

    /// <inheritdoc />
    public override async Task<TResult> CallActivityAsync<TResult>(
        TaskName name,
        object? input = null,
        TaskOptions? options = null)
    {
        var call = new RecordedActivityCall(name.Name, input, options, typeof(TResult));
        Func<string, object?, Task<object?>>? namedHandler;
        lock (_recordingGate)
        {
            _activityCalls.Add(call);
            _activityHandlers.TryGetValue(name.Name, out namedHandler);
        }

        var result = namedHandler is null
            ? await ActivityHandler(call).ConfigureAwait(false)
            : await namedHandler(name.Name, input).ConfigureAwait(false);
        return ConvertResult<TResult>(result, $"Activity '{name}'");
    }

    /// <summary>
    /// Schedules an in-memory timer driven by manual time advancement.
    /// </summary>
    /// <param name="fireAt">A UTC timestamp. Past or current timestamps complete immediately.</param>
    /// <param name="cancellationToken">Cancels the pending timer with the caller's token.</param>
    /// <returns>A task completed when simulated time reaches fireAt, or canceled when cancellation wins.</returns>
    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken) =>
        _scheduler.CreateTimer(fireAt, cancellationToken);

    /// <summary>Waits for the next matching buffered or raised event.</summary>
    /// <typeparam name="T">The type assignable from the event payload.</typeparam>
    /// <param name="eventName">A non-empty case-insensitive event name. Each event completes one wait in FIFO order.</param>
    /// <param name="cancellationToken">Cancels and removes this wait without consuming a future event.</param>
    /// <returns>The next event payload. Incompatible payloads fault the wait with InvalidOperationException.</returns>
    public override async Task<T> WaitForExternalEvent<T>(
        string eventName,
        CancellationToken cancellationToken = default)
    {
        var payload = await _scheduler.WaitForEvent(eventName, cancellationToken).ConfigureAwait(false);
        return ConvertResult<T>(payload, $"External event '{eventName}'");
    }

    /// <summary>Records an outbound event and delivers it locally when the target is this instance.</summary>
    /// <param name="instanceId">The non-empty target instance ID. Remote delivery is left to the test.</param>
    /// <param name="eventName">The non-empty case-insensitive event name.</param>
    /// <param name="payload">The non-null payload retained without cloning.</param>
    public override void SendEvent(string instanceId, string eventName, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(payload);
        lock (_recordingGate)
        {
            _sentEvents.Add(new RecordedOrchestrationEvent(instanceId, eventName, payload));
        }
        if (string.Equals(instanceId, InstanceId, StringComparison.Ordinal))
        {
            RaiseEvent(eventName, payload);
        }
    }

    /// <summary>Records the latest custom status and invokes an optional configured callback.</summary>
    /// <param name="customStatus">The status retained without cloning; null clears the status.</param>
    public override void SetCustomStatus(object? customStatus)
    {
        Volatile.Write(ref _customStatus, customStatus);
        _customStatusHandler?.Invoke(customStatus);
    }

    /// <summary>Records a child orchestration call and dispatches it to SubOrchestratorHandler.</summary>
    /// <typeparam name="TResult">The type assignable from the handler result.</typeparam>
    /// <param name="orchestratorName">The child orchestrator's task name.</param>
    /// <param name="input">The input retained without cloning.</param>
    /// <param name="options">Optional task or child-instance options, recorded without executing retry policies.</param>
    /// <returns>The handler result. Handler failures propagate and incompatible results fault the task.</returns>
    public override async Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName,
        object? input = null,
        TaskOptions? options = null)
    {
        var call = new RecordedSubOrchestrationCall(orchestratorName.Name, input, options, typeof(TResult));
        lock (_recordingGate)
        {
            _subOrchestrationCalls.Add(call);
        }
        var result = await SubOrchestratorHandler(call).ConfigureAwait(false);
        return ConvertResult<TResult>(result, $"Sub-orchestrator '{orchestratorName}'");
    }

    /// <summary>Records a continuation request without restarting or modifying the current execution.</summary>
    /// <param name="newInput">Input for the requested next execution, retained without cloning.</param>
    /// <param name="preserveUnprocessedEvents">The recorded event-preservation decision; current event queues are unchanged.</param>
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true) =>
        ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = newInput,
            PreserveUnprocessedEvents = preserveUnprocessedEvents
        });

    /// <summary>Copies and records a continuation request, then invokes an optional callback.</summary>
    /// <param name="options">Non-null next-execution options. No history reset or runtime restart occurs.</param>
    public override void ContinueAsNew(ContinueAsNewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var recorded = new ContinueAsNewOptions
        {
            NewInput = options.NewInput,
            PreserveUnprocessedEvents = options.PreserveUnprocessedEvents,
            NewVersion = options.NewVersion
        };
        lock (_recordingGate)
        {
            _continueAsNewRequests.Add(recorded);
        }

        _continueAsNewHandler?.Invoke(recorded);
    }

    /// <summary>Creates a repeatable GUID from the instance ID and sequence, or invokes the configured generator.</summary>
    /// <returns>A test GUID. The default sequence is not the Durable runtime's GUID algorithm.</returns>
    public override Guid NewGuid()
    {
        if (_guidGenerator is not null)
        {
            return _guidGenerator();
        }

        var key = FormattableString.Invariant($"{InstanceId}:{Interlocked.Increment(ref _guidSequence)}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Compares the configured version using ordinal ordering or a custom comparison.</summary>
    /// <param name="version">The non-null version to compare with. Empty strings are allowed.</param>
    /// <returns>A negative, zero, or positive result for the configured version relative to version.</returns>
    public override int CompareVersionTo(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _versionComparer(Version, version);
    }

    private static TResult ConvertResult<TResult>(object? result, string operation)
    {
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        if (result is null && default(TResult) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"{operation} returned {result?.GetType().FullName ?? "null"}, " +
            $"which cannot be assigned to {typeof(TResult).FullName}.");
    }

    private static object? DefaultResult(Type resultType) =>
        resultType.IsValueType ? Array.CreateInstance(resultType, 1).GetValue(0) : null;
}
