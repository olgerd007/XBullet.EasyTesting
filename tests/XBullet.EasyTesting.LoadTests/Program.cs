using System.Diagnostics;
using System.Globalization;
using System.Net;
using XBullet.EasyTesting.Http;

var options = LoadOptions.Parse(args);
Console.WriteLine(
    $"HTTP stub load test: {options.Requests:N0} requests, " +
    $"{options.Concurrency:N0} workers, {options.Rules:N0} rules");

await using var harness = new LoadHarness(options.Rules);
await harness.RunAsync(Math.Min(options.Requests, 2_000), options.Concurrency, recordLatency: false);

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
var result = await harness.RunAsync(options.Requests, options.Concurrency, recordLatency: true);
var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

Console.WriteLine($"Completed:  {result.Completed:N0}");
Console.WriteLine($"Errors:     {result.Errors:N0}");
Console.WriteLine($"Duration:   {result.Duration.TotalSeconds:F3} s");
Console.WriteLine($"Throughput: {result.Completed / result.Duration.TotalSeconds:N0} requests/s");
Console.WriteLine($"Latency:    p50 {result.Percentile(50):F3} ms | " +
                  $"p95 {result.Percentile(95):F3} ms | " +
                  $"p99 {result.Percentile(99):F3} ms");
Console.WriteLine($"Allocated:  {allocatedBytes / (double)result.Completed:N0} bytes/request");

return result.Errors == 0 && result.Completed == options.Requests ? 0 : 1;

internal sealed class LoadHarness : IAsyncDisposable
{
    private readonly StubHttpMessageHandler _handler;
    private readonly HttpClient _client;

    public LoadHarness(int ruleCount)
    {
        _handler = new StubHttpMessageHandler();
        _handler.When(HttpMethod.Get, "/target").Respond(HttpStatusCode.NoContent);
        for (var index = 1; index < ruleCount; index++)
        {
            _handler.When(HttpMethod.Get, $"/not-target-{index}")
                .Respond(HttpStatusCode.NoContent);
        }

        _client = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://load.test/")
        };
    }

    public async Task<LoadResult> RunAsync(int requests, int concurrency, bool recordLatency)
    {
        var nextRequest = -1;
        var completed = 0;
        var errors = 0;
        var latencies = recordLatency ? new long[requests] : [];
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await startGate.Task;
            while (true)
            {
                var requestIndex = Interlocked.Increment(ref nextRequest);
                if (requestIndex >= requests)
                {
                    return;
                }

                var requestStarted = Stopwatch.GetTimestamp();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "/target");
                    using var response = await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead);
                    if (response.StatusCode != HttpStatusCode.NoContent)
                    {
                        Interlocked.Increment(ref errors);
                    }
                    else
                    {
                        Interlocked.Increment(ref completed);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
                finally
                {
                    if (recordLatency)
                    {
                        latencies[requestIndex] = Stopwatch.GetTimestamp() - requestStarted;
                    }
                }
            }
        }).ToArray();

        var started = Stopwatch.GetTimestamp();
        startGate.SetResult();
        await Task.WhenAll(workers);
        return new LoadResult(
            completed,
            errors,
            Stopwatch.GetElapsedTime(started),
            latencies);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _handler.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed record LoadResult(
    int Completed,
    int Errors,
    TimeSpan Duration,
    long[] LatencyTicks)
{
    public double Percentile(int percentile)
    {
        if (LatencyTicks.Length == 0)
        {
            return 0;
        }

        Array.Sort(LatencyTicks);
        var index = (int)Math.Ceiling(percentile / 100d * LatencyTicks.Length) - 1;
        return LatencyTicks[Math.Max(index, 0)] * 1_000d / Stopwatch.Frequency;
    }
}

internal sealed record LoadOptions(int Requests, int Concurrency, int Rules)
{
    public static LoadOptions Parse(string[] args)
    {
        var requests = ReadPositiveInt(args, "--requests", 100_000);
        var concurrency = ReadPositiveInt(args, "--concurrency", Math.Max(Environment.ProcessorCount, 4));
        var rules = ReadPositiveInt(args, "--rules", 128);
        return new LoadOptions(requests, concurrency, rules);
    }

    private static int ReadPositiveInt(string[] args, string name, int defaultValue)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return defaultValue;
        }

        if (index == args.Length - 1 ||
            !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            throw new ArgumentException($"{name} must be followed by a positive integer.");
        }

        return value;
    }
}
