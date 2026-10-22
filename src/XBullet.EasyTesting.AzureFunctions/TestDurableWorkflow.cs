using System.Runtime.ExceptionServices;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Connects an in-memory Durable client to named orchestrators and activities through a borrowed function host.</summary>
/// <remarks>
/// Each function uses the host's middleware and a fresh DI scope. Payloads use direct assignment by
/// default; WithSerialization enables SDK converter snapshots. Time is advanced on individual contexts. This helper does not implement replay,
/// persistence, continuations, scheduled starts, or production lifecycle policies. WithRetries enables bounded retry simulation.
/// Dispose this workflow before its host; register the workflow, rather than its client, as a scenario resource.
/// </remarks>
public sealed partial class TestDurableWorkflow : ITestScenarioResource, IAsyncDisposable
{
    private readonly object _gate = new();
    private AzureFunctionTestHost? _host;
    private readonly TimeSpan _shutdownTimeout;
    private readonly bool _ownsClient;
    private readonly Dictionary<string, Func<WorkflowInstance, Task<object?>>> _orchestrators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<object?, CancellationToken, Task<object?>>> _activities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowInstance> _instances = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _activityExecutions = [];
    private readonly HashSet<Task> _childExecutions = [];
    private bool _registrationsFrozen;
    private bool _resetting;
    private bool _disposed;
    private long _childSequence;

    /// <summary>Creates a workflow that can be registered in DI and as a scenario resource before attaching its host.</summary>
    /// <param name="client">An optional caller-owned client registered in application DI. Its schedule, lookup, event, and wait handlers must be unset; this workflow wires them for its lifetime. Null creates an owned client.</param>
    /// <param name="shutdownTimeout">A positive finite wall-clock cleanup limit, defaulting to five seconds. User code must honor cancellation.</param>
    public TestDurableWorkflow(TestDurableTaskClient? client = null, TimeSpan? shutdownTimeout = null)
    {
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        if (_shutdownTimeout <= TimeSpan.Zero || _shutdownTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        }

        if (client is not null && (client.ScheduleHandler is not null || client.GetInstanceHandler is not null
            || client.RaiseEventHandler is not null || client.WaitForStartHandler is not null || client.WaitForCompletionHandler is not null))
        {
            throw new ArgumentException("The supplied client already has a handler required by the connected workflow.", nameof(client));
        }

        _ownsClient = client is null;
        Client = client ?? new TestDurableTaskClient("workflow-client");
        Client.ScheduleHandler = (call, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (call.Options?.StartAt is not null)
                {
                    throw new InvalidOperationException("Connected workflows do not implement scheduled starts.");
                }

                Start(call.OrchestratorName, call.InstanceId, call.Input, call.Options?.Version, null, token);
                return Task.FromResult(call.InstanceId);
            };
        Client.GetInstanceHandler = (id, include, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ReadMetadata(id, include));
            };
        Client.RaiseEventHandler = (id, name, payload, token) =>
            {
                token.ThrowIfCancellationRequested();
                RouteEvent(id, name, payload);
                return Task.CompletedTask;
            };
        Client.WaitForStartHandler = async (id, include, token) =>
            {
                var instance = Find(id);
                await instance.Started.Task.WaitAsync(token).ConfigureAwait(false);
                return BuildMetadata(instance, include);
            };
        Client.WaitForCompletionHandler = async (id, include, token) =>
            {
                var instance = Find(id);
                await instance.Completed.Task.WaitAsync(token).ConfigureAwait(false);
                return BuildMetadata(instance, include);
            };
    }

    /// <summary>Gets the client wired for scheduling, metadata, events, and start/completion waits.</summary>
    /// <value>Use this client in starters or register it as DurableTaskClient in application DI. Do not replace its workflow handlers or reset it independently.</value>
    public TestDurableTaskClient Client { get; }

    /// <summary>Attaches the host used for every subsequent function invocation.</summary>
    /// <param name="host">The non-null caller-owned host containing the registered function classes. Attach exactly once, before scheduling.</param>
    /// <returns>This workflow for chaining. Dispose the workflow before the host.</returns>
    public TestDurableWorkflow UseHost(AzureFunctionTestHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ObjectDisposedException.ThrowIf(host.IsDisposed, host);
        lock (_gate)
        {
            EnsureAvailable();
            if (_host is not null)
            {
                throw new InvalidOperationException("A connected workflow host is already attached.");
            }

            _host = host;
        }

        return this;
    }

    /// <summary>Gets the context of a scheduled root or child instance.</summary>
    /// <param name="instanceId">The non-empty case-sensitive instance ID.</param>
    /// <returns>The borrowed context for advancing time and inspecting calls. Do not replace its identity or dispatch configuration while it executes.</returns>
    public TestOrchestrationContext GetContext(string instanceId) => Find(instanceId).Context;

    /// <summary>Waits for one instance's result and propagates its original failure.</summary>
    /// <typeparam name="TResult">The assignable result type, or deserialization target when serialization is enabled; null requires a nullable type.</typeparam>
    /// <param name="instanceId">The non-empty case-sensitive root or child instance ID.</param>
    /// <param name="cancellationToken">Cancels this wait without canceling execution. Use a timeout or cancellation for workflows waiting on events or timers.</param>
    /// <returns>The result after its invocation scope has closed.</returns>
    public async Task<TResult> GetResultAsync<TResult>(string instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = Find(instanceId);
        var output = await WaitForOutputAsync(instance, cancellationToken).ConfigureAwait(false);
        return ConvertValue<TResult>(output, $"Workflow '{instanceId}' result");
    }

    private static async Task<object?> WaitForOutputAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        await instance.Completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (instance.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return instance.Output;
    }

    private WorkflowInstance Find(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_gate)
        {
            EnsureAvailable();
            return _instances.TryGetValue(instanceId, out var instance) ? instance
                : throw new KeyNotFoundException($"No connected workflow instance '{instanceId}' exists.");
        }
    }

    private void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_host is { IsDisposed: true })
        {
            throw new ObjectDisposedException(nameof(AzureFunctionTestHost));
        }
        if (_resetting)
        {
            throw new InvalidOperationException("Connected workflow cleanup is in progress.");
        }
    }

    private AzureFunctionTestHost Host => _host
        ?? throw new InvalidOperationException("Attach a function host with UseHost before scheduling a connected workflow.");

    private static T ConvertValue<T>(object? value, string description) =>
        value is SerializedDurablePayload payload ? payload.Read<T>()
            : value is T typed ? typed : value is null && default(T) is null ? default!
            : throw new InvalidOperationException($"{description} is not assignable to {typeof(T).FullName}.");

    private sealed class WorkflowInstance(TaskName name, object? input, TestOrchestrationContext context, CancellationTokenSource cancellation)
    {
        public TaskName Name { get; } = name;
        public object? Input { get; } = input;
        public TestOrchestrationContext Context { get; } = context;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token { get; } = cancellation.Token;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OrchestrationRuntimeStatus Status { get; set; } = OrchestrationRuntimeStatus.Pending;
        public object? Output { get; set; }
        public Exception? Failure { get; set; }
        public DateTimeOffset CreatedAt { get; } = new(context.CurrentUtcDateTime);
    }
}
