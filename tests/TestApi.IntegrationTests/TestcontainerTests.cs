using System.Reflection;
using System.Text.Json;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Testcontainers;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class TestcontainerTests
{
    [Fact]
    public async Task Resource_waits_for_start_before_publishing_configuration_and_services()
    {
        var container = CreateContainer(out var proxy);
        var resource = new TestcontainerResource<IContainer>(
            container,
            _ => new Dictionary<string, string?>
            {
                ["ConnectionStrings:Dependency"] = "server=secret"
            },
            (_, services) => services.AddSingleton<Marker>(),
            maximumDiagnosticCharacters: 5);
        var configurationBuilder = new ConfigurationBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            resource.ConfigureConfiguration(configurationBuilder));

        await resource.StartAsync(TestContext.Current.CancellationToken);
        resource.ConfigureConfiguration(configurationBuilder);
        var services = new ServiceCollection();
        resource.ConfigureServices(services);

        Assert.Equal("server=secret", configurationBuilder.Build()["ConnectionStrings:Dependency"]);
        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Same(container, serviceProvider.GetRequiredService<IContainer>());
        Assert.NotNull(serviceProvider.GetService<Marker>());

        var diagnostics = JsonSerializer.Serialize(
            await resource.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("container-id", diagnostics);
        Assert.Contains("defgh", diagnostics);
        Assert.DoesNotContain("abcdefgh", diagnostics);
        Assert.DoesNotContain("server=secret", diagnostics);

        await resource.DisposeAsync();
        await resource.DisposeAsync();
        Assert.Equal(1, proxy.StartCount);
        Assert.Equal(1, proxy.DisposeCount);
    }

    [Fact]
    public async Task Generic_registration_integrates_with_scenario_host_and_typed_lookup()
    {
        var container = CreateContainer(out var proxy);
        using var factory = TestApiHostSettings.CreateBuilder()
            .UseTestcontainer(
                "dependency",
                _ => container,
                _ => new Dictionary<string, string?>
                {
                    ["Dependency:Endpoint"] = "localhost:54321"
                },
                configureServices: null,
                maximumDiagnosticCharacters: 100)
            .Build();

        await using (var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                "localhost:54321",
                scope.Services.GetRequiredService<IConfiguration>()["Dependency:Endpoint"]);
            Assert.Same(
                container,
                scope.GetTestcontainer<Program, IContainer>("dependency"));
            Assert.Equal(1, proxy.StartCount);
            Assert.Equal(0, proxy.DisposeCount);
        }

        Assert.Equal(1, proxy.DisposeCount);
    }

    [Fact]
    public void Module_registrations_are_lazy_and_composable_without_a_container_runtime()
    {
        var builder = new TestScenarioScopeBuilder();

        Assert.Same(
            builder,
            builder.UsePostgreSql(options =>
            {
                options.ResourceName = "postgres";
                options.ConfigurationKey = "ConnectionStrings:Orders";
                options.ConfigureBuilder(native => native);
            }));
        Assert.Same(builder, builder.UseSqlServer());
        Assert.Same(builder, builder.UseKafka());
        Assert.Same(builder, builder.UseRedis());
        Assert.Same(builder, builder.UseRabbitMq());
        Assert.Same(builder, builder.UseAzurite());
        Assert.Same(builder, builder.UseServiceBusEmulator(acceptLicenseAgreement: true));

        var hostBuilder = TestApiHostSettings.CreateBuilder();
        Assert.Same(hostBuilder, hostBuilder.UsePostgreSql());
        Assert.Same(hostBuilder, hostBuilder.UseSqlServer());
        Assert.Same(hostBuilder, hostBuilder.UseKafka());
        Assert.Same(hostBuilder, hostBuilder.UseRedis());
        Assert.Same(hostBuilder, hostBuilder.UseRabbitMq());
        Assert.Same(hostBuilder, hostBuilder.UseAzurite());
        Assert.Same(hostBuilder, hostBuilder.UseServiceBusEmulator(acceptLicenseAgreement: true));
    }

    [Fact]
    public async Task Module_options_and_resources_validate_lifecycle_and_failure_paths()
    {
        Assert.Throws<ArgumentException>(() =>
            new TestcontainerModuleOptions<object>(" ", "key"));
        Assert.Throws<ArgumentException>(() =>
            new TestcontainerModuleOptions<object>("resource", " "));
        Assert.Throws<ArgumentNullException>(() =>
            new TestcontainerModuleOptions<object>("resource", "key").ConfigureBuilder(null!));

        var options = new TestcontainerModuleOptions<object>("resource", "key");
        var unconfigured = new object();
        Assert.Same(unconfigured, options.Apply(unconfigured));
        var order = new List<int>();
        options.ConfigureBuilder(value => { order.Add(1); return value; });
        options.ConfigureBuilder(value => { order.Add(2); return value; });
        var native = new object();
        Assert.Same(native, options.Apply(native));
        Assert.Equal([1, 2], order);
        options.ResourceName = " ";
        Assert.Throws<ArgumentException>(options.Validate);
        options.ResourceName = "resource";
        options.ConfigurationKey = " ";
        Assert.Throws<ArgumentException>(options.Validate);

        var container = CreateContainer(out var proxy);
        Assert.Throws<ArgumentNullException>(() => new TestcontainerResource<IContainer>(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestcontainerResource<IContainer>(container, maximumDiagnosticCharacters: -1));
        var resource = new TestcontainerResource<IContainer>(container);
        Assert.Contains("false", JsonSerializer.Serialize(await resource.CaptureDiagnosticsAsync(
            TestContext.Current.CancellationToken)));
        Assert.Throws<ArgumentNullException>(() => resource.ConfigureConfiguration(null!));
        Assert.Throws<ArgumentNullException>(() => resource.ConfigureServices(null!));
        await resource.StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resource.StartAsync(TestContext.Current.CancellationToken));
        resource.ConfigureConfiguration(new ConfigurationBuilder());
        resource.ConfigureServices(new ServiceCollection());
        await resource.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await resource.StartAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() =>
            resource.ConfigureServices(new ServiceCollection()));

        var nullValues = new TestcontainerResource<IContainer>(
            CreateContainer(out _),
            _ => null!);
        await nullValues.StartAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() =>
            nullValues.ConfigureConfiguration(new ConfigurationBuilder()));
        await nullValues.DisposeAsync();

        var invalidKey = new TestcontainerResource<IContainer>(
            CreateContainer(out _),
            _ => new Dictionary<string, string?> { [" "] = "value" });
        await invalidKey.StartAsync(TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() =>
            invalidKey.ConfigureConfiguration(new ConfigurationBuilder()));
        await invalidKey.DisposeAsync();

        var failingContainer = CreateContainer(out var failingProxy);
        failingProxy.LogException = new InvalidOperationException("logs unavailable");
        var failingLogs = new TestcontainerResource<IContainer>(failingContainer);
        await failingLogs.StartAsync(TestContext.Current.CancellationToken);
        var diagnostics = JsonSerializer.Serialize(await failingLogs.CaptureDiagnosticsAsync(
            TestContext.Current.CancellationToken));
        Assert.Contains("logs unavailable", diagnostics);
        await failingLogs.DisposeAsync();

        var cancellationContainer = CreateContainer(out var cancellationProxy);
        cancellationProxy.LogException = new OperationCanceledException("cancelled");
        var cancelledLogs = new TestcontainerResource<IContainer>(cancellationContainer);
        await cancelledLogs.StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await cancelledLogs.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken));
        await cancelledLogs.DisposeAsync();

        var shortLogs = new TestcontainerResource<IContainer>(
            CreateContainer(out _),
            maximumDiagnosticCharacters: 100);
        await shortLogs.StartAsync(TestContext.Current.CancellationToken);
        Assert.Contains("abcdefgh", JsonSerializer.Serialize(
            await shortLogs.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken)));
        await shortLogs.DisposeAsync();
    }

    private static IContainer CreateContainer(out ContainerProxy proxy)
    {
        var container = DispatchProxy.Create<IContainer, ContainerProxy>();
        proxy = (ContainerProxy)(object)container;
        return container;
    }

    private sealed class Marker;

    private class ContainerProxy : DispatchProxy
    {
        public int StartCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Exception? LogException { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name switch
            {
                "StartAsync" => Start(),
                "DisposeAsync" => Dispose(),
                "GetLogsAsync" => LogException is null
                    ? Task.FromResult(("abcdefgh", "stderr-data"))
                    : Task.FromException<(string Stdout, string Stderr)>(LogException),
                "GetMappedPublicPorts" => new Dictionary<ushort, ushort> { [5432] = 54321 },
                "get_Id" => "container-id",
                "get_Name" => "easy-testing",
                "get_Image" => new DockerImage("example/dependency:1.0"),
                "get_Hostname" => "localhost",
                "get_State" => TestcontainersStates.Running,
                "get_Health" => TestcontainersHealthStatus.Healthy,
                _ => throw new NotSupportedException(
                    $"Unexpected fake container call: {targetMethod.Name}.")
            };
        }

        private Task Start()
        {
            StartCount++;
            return Task.CompletedTask;
        }

        private ValueTask Dispose()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
