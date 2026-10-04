# Test .NET isolated Azure Functions

Use `XBullet.EasyTesting.AzureFunctions` to resolve isolated-worker function classes from a test
service provider and invoke them directly with realistic contexts, trigger data, middleware,
bindings, retry state, and per-invocation DI scopes. It does not start Functions Core Tools or the
Azure Functions runtime.

## Install and build a test host

```shell
dotnet add package XBullet.EasyTesting.AzureFunctions
```

Import `XBullet.EasyTesting.AzureFunctions`, register each function that will be invoked, and add
the same application services or worker middleware required by that function:

```csharp
await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<ProcessOrderHttpFunction>()
    .AddFunction<CleanupTimerFunction>()
    .UseMiddleware<CorrelationMiddleware>()
    .ConfigureServices(services =>
        services.AddSingleton<IOrderService, TestOrderService>())
    .Build();
```

The host is asynchronous disposable. Each `InvokeAsync` call creates one DI scope, executes worker
middleware in registration order, invokes the function delegate, captures outputs, and disposes the
scope even when the function or middleware throws.

## Direct function invocation

For an HTTP function, build a request, resolve the real function class, and call its entry point:

```csharp
var request = host.HttpRequest(nameof(ProcessOrderHttpFunction), cancellationToken)
    .WithMethod(HttpMethod.Post)
    .WithUrl("/api/orders")
    .WithJsonBody(new CreateOrderRequest("order-42", 3))
    .Build();

var function = host.GetRequiredService<ProcessOrderHttpFunction>();
var response = await function.RunAsync(request, request.FunctionContext);
var body = await response.ReadBodyAsJsonAsync<AcceptedOrderResponse>(
    cancellationToken: cancellationToken);
```

Use `InvokeAsync` for middleware, trigger binding capture, output capture, and automatic invocation
scope management. See the [trigger recipes](azure-functions-triggers.md) for HTTP, timer, Kafka,
Service Bus, Queue Storage, Blob, Event Grid, and Event Hubs examples.

## Configure the function context

`CreateContext` returns a `TestFunctionContext` with non-null invocation, function-definition,
trace, binding, retry, feature, item, service, and cancellation state:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-context -->
```csharp
[Fact]
public async Task Context_exposes_complete_worker_state()
{
    await using var host = CreateHost(new RecordingTriggerInvocationSink());
    using var cancellation = new CancellationTokenSource();
    var marker = new InvocationMarker("feature-value");
    var context = host.CreateContext("CompleteContext", cancellation.Token)
        .WithTrace("00-test-trace-parent", "vendor=test")
        .WithRetry(2, 5)
        .WithBindingData("custom", "binding-value")
        .WithFeature(marker)
        .WithItem("item", "item-value");

    Assert.Equal("00-test-trace-parent", context.TraceContext.TraceParent);
    Assert.Equal("vendor=test", context.TraceContext.TraceState);
    Assert.Equal("binding-value", context.BindingContext.BindingData["custom"]);
    Assert.Equal(2, context.RetryContext.RetryCount);
    Assert.Equal(5, context.RetryContext.MaxRetryCount);
    Assert.Equal("CompleteContext", context.FunctionDefinition.Name);
    Assert.Equal("CompleteContext", context.FunctionDefinition.EntryPoint);
    Assert.Same(marker, context.Features.Get<InvocationMarker>());
    Assert.Equal("item-value", context.Items["item"]);
    Assert.Equal(cancellation.Token, context.CancellationToken);
}
```
<!-- end-snippet -->

Fluent context methods include `WithTrace`, `WithRetry`, `WithBindingData`, `WithFeature`, and
`WithItem`. Trigger builders apply their value, binding type, and broker metadata to this context.

## Middleware, retry state, and output bindings

Delegate middleware and `IFunctionsWorkerMiddleware` run around the supplied invocation delegate.
For a multiple-output POCO, every public result property is captured and worker output attributes
provide binding types:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-middleware-outputs -->
```csharp
[Fact]
public async Task Invocation_pipeline_runs_middleware_and_asserts_multiple_outputs()
{
    var recorder = new RecordingTriggerInvocationSink();
    var middlewareSteps = new List<string>();
    await using var host = CreateHostBuilder(recorder)
        .UseMiddleware(async (context, next) =>
        {
            middlewareSteps.Add("before");
            context.Items["middleware"] = "visited";
            await next(context);
            middlewareSteps.Add("after");
        })
        .Build();
    var trigger = AzureFunctionTestHost.QueueTrigger()
        .WithJsonBody(new KafkaOrderMessage("order-output", 11))
        .Build();
    var context = trigger.ApplyTo(
        host.CreateContext("RouteOrder", TestContext.Current.CancellationToken)
            .WithRetry(1, 3));

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, MultipleBindingOutput>(
        context,
        (function, testContext) => function.RouteOrderAsync(trigger.Value, testContext));

    Assert.True(invocation.FunctionExecuted);
    Assert.Equal(["before", "after"], middlewareSteps);
    Assert.Equal("visited", invocation.Context.Items["middleware"]);
    invocation.Context.Bindings.Should()
        .HaveCount(2)
        .HaveValue(
            "QueueMessage",
            "{\"orderId\":\"order-output\",\"quantity\":11}")
        .HaveValue(
            "BlobDocument",
            "order=order-output;quantity=11;retry=1");
    Assert.Equal(2, invocation.Context.FunctionDefinition.OutputBindings.Count);
    Assert.Equal(
        "queueOutput",
        invocation.Context.FunctionDefinition.OutputBindings["QueueMessage"].Type);
    Assert.Equal(
        "blobOutput",
        invocation.Context.FunctionDefinition.OutputBindings["BlobDocument"].Type);

    var bindings = invocation.Context.Bindings;
    bindings.Should().Contain("QueueMessage").NotContain("missing");
    Assert.Throws<TestOutputBindingVerificationException>(() => bindings.Should().HaveCount(3));
    Assert.Throws<TestOutputBindingVerificationException>(() => bindings.Should().Contain("missing"));
    Assert.Throws<TestOutputBindingVerificationException>(() =>
        bindings.Should().HaveValue("QueueMessage", "different"));
    Assert.Throws<TestOutputBindingVerificationException>(() =>
        bindings.Should().NotContain("QueueMessage"));
    Assert.Throws<ArgumentException>(() => bindings.Should().Contain(" "));
    Assert.Throws<ArgumentException>(() => bindings.Should().NotContain(" "));
    Assert.Throws<KeyNotFoundException>(() => bindings.GetOutput<string>("missing"));
    Assert.Throws<InvalidCastException>(() => bindings.GetOutput<int>("QueueMessage"));
}
```
<!-- end-snippet -->

`TestFunctionBindings.Should()` asserts output count, names, and values. `GetInput<T>` and
`GetOutput<T>` provide typed access. Assertion failures report the recorded bindings; an absent or
wrongly typed output fails explicitly.

Retry state is input to the function, not a retry engine. `WithRetry(current, maximum)` lets the
test verify retry-aware application behavior, while the test invokes the function once.

## Invocation scopes and cleanup

Scoped services are shared by middleware and the function within one invocation, isolated from the
next invocation, and disposed after the pipeline returns:

<!-- snippet: tests/TestFunctions.IntegrationTests/AzureFunctionScopeTests.cs#docs-functions-invocation-scope -->
```csharp
[Fact]
public async Task Each_invocation_uses_one_isolated_scope()
{
    await using var host = CreateHost<RecordingMiddleware>();
    var recorder = host.GetRequiredService<InvocationRecorder>();
    var singleton = host.GetRequiredService<SingletonDependency>();
    var firstContext = host.CreateContext(
        "FirstInvocation",
        TestContext.Current.CancellationToken);
    var secondContext = host.CreateContext(
        "SecondInvocation",
        TestContext.Current.CancellationToken);

    await host.InvokeAsync<ScopedFunction>(firstContext, (function, context) =>
        function.RunAsync(context));
    await host.InvokeAsync<ScopedFunction>(secondContext, (function, context) =>
        function.RunAsync(context));

    var firstMiddleware = Assert.Single(
        recorder.Middleware,
        item => item.InvocationId == firstContext.InvocationId);
    var firstFunction = Assert.Single(
        recorder.Functions,
        item => item.InvocationId == firstContext.InvocationId);
    var secondMiddleware = Assert.Single(
        recorder.Middleware,
        item => item.InvocationId == secondContext.InvocationId);
    var secondFunction = Assert.Single(
        recorder.Functions,
        item => item.InvocationId == secondContext.InvocationId);

    Assert.Same(firstMiddleware.Scoped, firstFunction.Scoped);
    Assert.Same(secondMiddleware.Scoped, secondFunction.Scoped);
    Assert.Same(firstMiddleware.Scoped, firstMiddleware.ContextScoped);
    Assert.Same(secondMiddleware.Scoped, secondMiddleware.ContextScoped);
    Assert.Same(firstFunction.Scoped, firstFunction.ContextScoped);
    Assert.Same(secondFunction.Scoped, secondFunction.ContextScoped);
    Assert.NotSame(firstFunction.Scoped, secondFunction.Scoped);
    Assert.Null(firstFunction.InitialValue);
    Assert.Null(secondFunction.InitialValue);
    Assert.Same(singleton, firstMiddleware.Singleton);
    Assert.Same(singleton, firstFunction.Singleton);
    Assert.Same(singleton, secondMiddleware.Singleton);
    Assert.Same(singleton, secondFunction.Singleton);
    Assert.NotSame(firstMiddleware.Transient, firstFunction.Transient);
    Assert.NotSame(secondMiddleware.Transient, secondFunction.Transient);
    Assert.NotSame(firstFunction.Transient, secondFunction.Transient);
    Assert.True(firstFunction.Scoped.DisposedAsynchronously);
    Assert.True(secondFunction.Scoped.DisposedAsynchronously);
    AssertContextNoLongerUsesInvocationScope(firstContext, firstFunction, singleton);
    AssertContextNoLongerUsesInvocationScope(secondContext, secondFunction, singleton);
}
```
<!-- end-snippet -->

The same cleanup guarantee holds on function failure:

<!-- snippet: tests/TestFunctions.IntegrationTests/AzureFunctionScopeTests.cs#docs-functions-failure-cleanup -->
```csharp
[Fact]
public async Task Invocation_scope_is_disposed_when_function_throws()
{
    await using var host = CreateHost<RecordingMiddleware>();
    var recorder = host.GetRequiredService<InvocationRecorder>();
    var singleton = host.GetRequiredService<SingletonDependency>();
    var context = host.CreateContext(
        "FailingFunction",
        TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        host.InvokeAsync<ScopedFunction>(context, (function, testContext) =>
            function.FailAsync(testContext)));

    var observation = Assert.Single(recorder.Functions);
    Assert.True(observation.Scoped.DisposedAsynchronously);
    AssertContextNoLongerUsesInvocationScope(context, observation, singleton);
}
```
<!-- end-snippet -->

Do not retain `context.InstanceServices` after invocation; the invocation provider has been disposed
and the context is restored to the host provider.

## Scenario resources and domain helpers

Use `RunInTestScenarioScopeAsync` to wrap setup, multiple invocations, and assertions in one isolated
resource lifecycle. `CreateTestScenarioScopeAsync` exposes the same lifecycle for manual ownership;
dispose its result asynchronously before disposing the host. Invocations inside a scenario still
create separate DI scopes, while registered singleton recorders or stubs retain state until the
scenario finishes.

Register mutable state with `UseScenarioResource(name, resource)`. It accepts the shared
`XBullet.EasyTesting.Hosting.ITestScenarioResource` contract. The Functions package references
`XBullet.EasyTesting` for these resource, cleanup, and diagnostic contracts. Register the resource
separately in DI when function constructors need it. Names are case-insensitively unique;
`$Invocation` is reserved for invocation metadata. Registered resources remain caller-owned and
are reset before and after each scenario, rather than disposed by it.

`FunctionScenario` provides borrowed scope access, guarded configuration through
`EnsureNotArranged`, and single-use `ArrangeAsync` with an `ArrangeCoreAsync` callback. Failure or
cancellation consumes the domain helper. `FunctionScopedTest` provides a borrowed `Host`, the test
cancellation token, and protected `RunAsync` overloads for callbacks with or without a result.
These counterparts require no ASP.NET entry point or web application factory.

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionScenarioTests.cs#docs-functions-scenario-resources -->
```csharp
[Fact]
public async Task Scenario_resources_span_invocations_and_reset_between_tests()
{
    var resource = new RecordingResource { Value = "stale" };
    await using var host = AzureFunctionTestHost.CreateBuilder()
        .AddFunction<RecordingFunction>()
        .ConfigureServices(services =>
        {
            services.AddSingleton(resource);
            services.AddScoped<InvocationDependency>();
        })
        .UseScenarioResource("Published state", resource)
        .Build();

    var scopedTest = new ExampleScopedTest(host, TestContext.Current.CancellationToken);
    await scopedTest.Run(async (scope, token) =>
    {
        Assert.Null(resource.Value);
        Assert.Same(resource, scope.GetResource<RecordingResource>("published STATE"));
        var scenario = new RecordingScenario(scope, "Published state").WithValue("arranged");
        await scenario.ArrangeAsync(token);

        var first = await scope.Host.InvokeAsync<RecordingFunction, string>(
            scope.Host.CreateContext("First", token), (function, _) => function.RunAsync());
        var second = await scope.Host.InvokeAsync<RecordingFunction, string>(
            scope.Host.CreateContext("Second", token), (function, _) => function.RunAsync());

        Assert.Equal("arranged", first.Result);
        Assert.Equal("arranged", second.Result);
        Assert.Equal(2, resource.Invocations.Count);
        Assert.NotSame(resource.Invocations[0], resource.Invocations[1]);
        Assert.All(resource.Invocations, dependency => Assert.True(dependency.Disposed));
    });

    Assert.Null(resource.Value);
    Assert.Empty(resource.Invocations);
    Assert.Equal(2, resource.ResetTokens.Count);
    await scopedTest.Run((scope, _) =>
    {
        Assert.Null(scope.GetResource<RecordingResource>("Published state").Value);
        return Task.CompletedTask;
    });
}
```
<!-- end-snippet -->

In this compiled example, `ExampleScopedTest` exposes the protected runner, and `RecordingScenario`
sets the borrowed resource's arranged value. The complete helpers and resource implementation are in
[`FunctionScenarioTests`](../../tests/TestFunctions.IntegrationTests/FunctionScenarioTests.cs).

Use `scope.Context.OnCleanup`, `DisposeWithScenario(IDisposable)`, or
`DisposeWithScenario(IAsyncDisposable)` to transfer ownership of temporary scenario objects.
Cleanup runs in reverse registration order, continues after individual failures, and then resets
every registered resource. Repeated scope disposal has no effect.

Scenarios on one host run sequentially through a gate, including resource resets and cleanup.
Cancellation while waiting does not release another scenario's gate. Initialization failures also
perform cleanup and release the gate. Nested scenarios on the same host are unsupported.

The host service provider is built once. Scenarios isolate registered resource state and owned
cleanup; they do not recreate singleton services or apply per-scenario DI overrides. Direct
`InvokeAsync` calls retain their existing invocation-only lifecycle and do not reset resources.
Avoid mixing direct invocations from another test with an active resource scenario. Use separate
hosts when tests need independent singleton state or concurrent scenarios.

## Failure diagnostics

Function, middleware, and invocation-disposal failures receive the shared `TestScenarioDiagnostics`
object in `exception.Data[TestScenarioDiagnostics.ExceptionDataKey]`. Invocation diagnostics use
the active scenario ID, or the invocation ID for a direct call. The reserved `$Invocation` entry
contains the invocation ID, function name, and captured input/output binding names. Binding values
remain available through the caller-owned context; the metadata snapshot does not traverse service
providers, HTTP streams, or other binding object graphs.

The scenario runner also captures failures thrown by arrangement or assertions outside invocation.
Resources are captured before reset and disposal; a failing diagnostic callback is represented as
a `CaptureFailure` value without preventing other captures or cleanup. Resources should return
detached, serializable snapshots containing safe test data.

Original failure and cancellation exceptions retain their identity and cancellation token when
cleanup also fails. Invocation disposal and scenario cleanup errors are aggregated in the original
exception's `Data` under `XBullet.EasyTesting.TestScenarioCleanupException`. Cleanup and automatic
diagnostic capture use non-cancelable tokens. Cleanup failures after an otherwise successful test
are thrown as exceptions; scenario cleanup failures are aggregated. The gate is released even when
cleanup fails.

No automatic reset or diagnostic capture runs when test code resolves a function and calls its
method directly. Invoke through the host and use the scenario runner for this lifecycle.

## Troubleshooting

- **Function cannot be resolved:** add it with `AddFunction<TFunction>()` and register every
  constructor dependency in `ConfigureServices`.
- **Middleware did not run:** invoke through `host.InvokeAsync`; directly calling a function method
  intentionally bypasses the test host pipeline.
- **Binding metadata is missing:** use `BuildTrigger`, `JsonTrigger`, or another capturable trigger
  builder and pass the resulting `TestTriggerData<T>` to `InvokeAsync`.
- **Output assertion fails:** confirm the function returned the output POCO and that public property
  names match the asserted binding names. Failure diagnostics list captured values and types.
- **A retry was expected:** the test host exposes retry context but does not schedule retries; invoke
  again explicitly or test deployed retry behavior at the runtime level.

## What this layer proves

These tests cover function code, DI, serialization performed by the builders, context access,
middleware ordering, retry-aware branches, and invocation results. They do not validate host
indexing, binding expressions, extension configuration, broker connectivity, checkpoints, scaling,
or deployment settings. Keep a smaller runtime or deployed-environment suite for those concerns.

Durable orchestration has a narrower supported surface. See
[Durable activity dispatch](azure-functions-durable.md) before testing an orchestrator.

Browse the [Azure Functions API reference](../api/packages/xbullet-easytesting-azurefunctions.md)
for every host, context, trigger builder, and invocation result.
