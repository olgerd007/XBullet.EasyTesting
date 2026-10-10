# Specialized hosts and dependencies

Choose one of these packages only when its boundary is part of the requested test. Follow installed
APIs and release-matched documentation; the links below track the framework's `main` branch.

| Boundary | Package | Evidence and limitations |
| --- | --- | --- |
| Azure SDK response, paging, transport, credentials | `XBullet.EasyTesting.Azure` | Helpers can exercise real SDK serialization and pipelines; fabricated responses do not prove live Azure behavior |
| Production database or other service in a container | `XBullet.EasyTesting.Testcontainers` | Requires a Docker-compatible runtime; start, reset, diagnose, and dispose resources with clear ownership |
| Distributed application | `XBullet.EasyTesting.Aspire` | Use the distributed-application host and explicit resource readiness; a stubbed service proves a narrower contract |
| .NET isolated Azure Functions | `XBullet.EasyTesting.AzureFunctions` | Use dedicated worker-host and trigger helpers; do not model the worker as an ASP.NET controller |

Check runtime prerequisites before scheduling validation. Container license acceptance, live
service credentials, paid resources, or external publication require the user's existing
authorization; adding a test is not authorization to provision cloud infrastructure.

Functions Durable support emulates `CallActivityAsync`; it does not establish timer, external
event, sub-orchestrator, or replay behavior. Keep those claims outside emulated tests and use an
appropriate real-runtime test when required.

- [Azure SDK testing](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Azure/README.md)
- [Container resources](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Testcontainers/README.md)
- [Aspire host](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Aspire/README.md)
- [Functions host](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions.md)
- [Durable Functions boundary](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-durable.md)
