using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableWorkflow
{
    /// <summary>Registers an orchestrator delegate under a case-sensitive task name.</summary>
    /// <typeparam name="TFunction">The function class already registered in the borrowed host.</typeparam>
    /// <typeparam name="TResult">The result returned by the orchestrator.</typeparam>
    /// <param name="name">The non-empty task name. Duplicate registrations are rejected.</param>
    /// <param name="invoke">The non-null callback receiving the scoped function, orchestration context, and worker context with its execution token.</param>
    /// <returns>This workflow for chaining. Register before the first schedule or after reset.</returns>
    public TestDurableWorkflow AddOrchestrator<TFunction, TResult>(
        string name,
        Func<TFunction, TestOrchestrationContext, TestFunctionContext, Task<TResult>> invoke)
        where TFunction : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(invoke);
        lock (_gate)
        {
            EnsureRegistrationAvailable();
            if (!_orchestrators.TryAdd(name, async instance =>
            {
                var worker = Host.CreateContext(name, instance.Token)
                    .WithInputBinding("context", instance.Context, "orchestrationTrigger");
                var result = await Host.InvokeAsync<TFunction, TResult>(worker,
                    (function, context) => invoke(function, instance.Context, context)).ConfigureAwait(false);
                RequireExecution(result, name);
                return CapturePayload(result.Result);
            }))
            {
                throw new ArgumentException($"Orchestrator '{name}' is already registered.", nameof(name));
            }
        }

        return this;
    }

    /// <summary>Registers an orchestrator without a result.</summary>
    /// <typeparam name="TFunction">The function class already registered in the borrowed host.</typeparam>
    /// <param name="name">The non-empty, unique, case-sensitive task name.</param>
    /// <param name="invoke">The non-null callback borrowing the scoped function and contexts.</param>
    /// <returns>This workflow for chaining.</returns>
    public TestDurableWorkflow AddOrchestrator<TFunction>(
        string name,
        Func<TFunction, TestOrchestrationContext, TestFunctionContext, Task> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return AddOrchestrator<TFunction, object?>(name, async (function, context, worker) =>
        {
            await invoke(function, context, worker).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>Registers an activity delegate under a case-sensitive task name.</summary>
    /// <typeparam name="TFunction">The function class already registered in the borrowed host.</typeparam>
    /// <typeparam name="TInput">The assignable activity input type, or deserialization target when serialization is enabled.</typeparam>
    /// <typeparam name="TResult">The activity result type.</typeparam>
    /// <param name="name">The non-empty task name. Duplicate registrations are rejected.</param>
    /// <param name="invoke">The non-null callback receiving the scoped function, input, and worker context with its execution token.</param>
    /// <returns>This workflow for chaining. Every activity call gets a separate invocation scope.</returns>
    public TestDurableWorkflow AddActivity<TFunction, TInput, TResult>(
        string name,
        Func<TFunction, TInput, TestFunctionContext, Task<TResult>> invoke)
        where TFunction : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(invoke);
        lock (_gate)
        {
            EnsureRegistrationAvailable();
            if (!_activities.TryAdd(name, async (input, token) =>
            {
                var typedInput = ConvertValue<TInput>(input, $"Activity '{name}' input");
                var worker = Host.CreateContext(name, token).WithInputBinding("input", typedInput, "activityTrigger");
                var result = await Host.InvokeAsync<TFunction, TResult>(worker,
                    (function, context) => invoke(function, typedInput, context)).ConfigureAwait(false);
                RequireExecution(result, name);
                return CapturePayload(result.Result);
            }))
            {
                throw new ArgumentException($"Activity '{name}' is already registered.", nameof(name));
            }
        }

        return this;
    }

    /// <summary>Registers an activity without a result.</summary>
    /// <typeparam name="TFunction">The function class already registered in the borrowed host.</typeparam>
    /// <typeparam name="TInput">The assignable input type.</typeparam>
    /// <param name="name">The non-empty, unique, case-sensitive task name.</param>
    /// <param name="invoke">The non-null callback borrowing the scoped function and worker context.</param>
    /// <returns>This workflow for chaining.</returns>
    public TestDurableWorkflow AddActivity<TFunction, TInput>(
        string name,
        Func<TFunction, TInput, TestFunctionContext, Task> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return AddActivity<TFunction, TInput, object?>(name, async (function, input, worker) =>
        {
            await invoke(function, input, worker).ConfigureAwait(false);
            return null;
        });
    }

    private void EnsureRegistrationAvailable()
    {
        EnsureAvailable();
        if (_registrationsFrozen)
        {
            throw new InvalidOperationException("Register functions before scheduling, or reset the workflow first.");
        }
    }

    private static void RequireExecution(TestFunctionInvocationResult result, string name)
    {
        if (!result.FunctionExecuted)
        {
            throw new InvalidOperationException($"Connected function '{name}' was short-circuited by middleware.");
        }
    }
}
