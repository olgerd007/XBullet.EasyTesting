# XBullet.EasyTesting load tests

This executable applies concurrent load to `StubHttpMessageHandler`, including request capture,
rule matching, response creation, and exchange recording. It reports throughput, latency
percentiles, errors, and process-wide allocations per measured request.

Run the default workload in Release mode:

```shell
dotnet run --project tests/XBullet.EasyTesting.LoadTests -c Release
```

Tune the workload for the machine or scenario under test:

```shell
dotnet run --project tests/XBullet.EasyTesting.LoadTests -c Release -- \
  --requests 250000 --concurrency 32 --rules 256
```

The first configured rule matches the request. Additional rules model a large reusable stub
registry and make unnecessary rule evaluation visible. The harness warms up the runtime before
measuring and returns a non-zero exit code if any request fails.
