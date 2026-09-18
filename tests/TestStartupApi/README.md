# Startup API template

This sample is a conventional `Startup`-based ASP.NET Core API. It shows the production wiring for:

- Azure AD JWT bearer authentication and an `orders.write` scope policy;
- a typed HTTP client for an external orders service;
- EF Core storage backed by SQLite;
- JSON event publishing through a Confluent Kafka producer.

`POST /api/orders/import/{externalId}` exercises all four integrations. Replace the placeholder
Azure AD values and the local infrastructure endpoints in `appsettings.json` for a deployed service.
The matching `TestStartupApi.IntegrationTests` project replaces the typed REST client with a scoped,
service-specific stub client. It also replaces Kafka, time, and storage while keeping the real
controller and `Startup` pipeline.

`IExternalOrdersClient` and `IExternalCustomersClient` contain list, get, create, update, and delete
operations. Their thin strict stubs share `StubServiceClientBase<TOperation>`, which owns response
arrangement, call recording, reset, diagnostics, and verification behavior.

`OrderScenario` adds order-domain vocabulary on top of the generic scenario builder. It composes
external-service arrangements and database seeds, exposes route helpers, and centralizes database
and Kafka verification for success, missing-order, conflict, and authorization cases.
`AsApiUser()` supplies the application's normal Azure AD object identifier and required order scope;
its optional callback adds or overrides claims for exceptional authorization scenarios.
External orders can include line items; the import route persists them as child entities and includes
their quantities, prices, and calculated line totals in both the API response and Kafka event.
The `Features:OrderItems` boolean controls line-item import. Tests override it per isolated scope with
`scenario.EnableFeature(FeatureNames.OrderItems)` or `scenario.DisableFeature(...)`.
Orders also support shipments. Each `OrderShipment` owns shipment details that reference the exact
order items being shipped. Creating a shipment validates remaining quantities, calls the external
post provider, and persists the provider ID, carrier, tracking number, and item-level allocations.
