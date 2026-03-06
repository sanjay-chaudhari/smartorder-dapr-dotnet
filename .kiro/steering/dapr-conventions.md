---
inclusion: always
---


# Dapr Development Conventions

## Project Context
SmartOrder is a .NET 10 multi-microservice application demonstrating all Dapr building blocks.
Each service is an ASP.NET Core Minimal API with a Dapr sidecar running alongside it.

## DaprClient Registration
- Always register via dependency injection: `builder.Services.AddDaprClient();`
- Inject `DaprClient` or `IDaprClient` through constructor injection
- Never instantiate with `new DaprClient()` directly
- Use `IDaprClient` interface in all classes to enable unit test mocking

## App ID Naming
- Use kebab-case for all Dapr app IDs
- Defined IDs: `order-service`, `inventory-service`, `payment-service`, `notification-service`, `workflow-orchestrator`

## Component Names
- State store: `statestore`
- Pub/sub broker: `pubsub`
- Secret store: `secretstore`
- Configuration store: `configstore`
- Resiliency policy: `resiliency`

## Service Invocation
- Use `daprClient.InvokeMethodAsync<TRequest, TResponse>(appId, methodName, request, cancellationToken)` for all inter-service calls
- Never use `HttpClient` directly for inter-service communication
- Demonstrate both HTTP and gRPC invocation modes where applicable

## Pub/Sub Messaging
- Topic names use kebab-case: `order-placed`, `payment-processed`, `notification-sent`
- Decorate subscriber endpoints with `[Topic("pubsub", "topic-name")]`
- Always call `app.MapSubscribeHandler()` in `Program.cs` of every subscriber service
- Include a dead-letter topic for each primary topic: `{topic-name}-deadletter`
- Return HTTP 200 for successful processing, 404 to drop a message, 500 to trigger a retry

## State Management
- Key pattern: `{entity-type}-{id}` — example: `order-abc123`
- Use `StateOptions` with `Consistency.Strong` for all financial or inventory data
- Use ETags for optimistic concurrency on update operations
- Use state transactions (`ExecuteStateTransactionAsync`) for atomic multi-key updates

## Workflow (Saga Pattern)
- Implement order saga steps as `WorkflowActivity` classes
- Saga sequence: `ValidateOrder` → `ReserveInventory` → `ProcessPayment` → `SendNotification`
- Compensation activities: `ReleaseInventoryReservation`, `RefundPayment`
- Register workflow and activities in `Program.cs` using `builder.Services.AddDaprWorkflow()`

## Secrets Management
- Retrieve all secrets via `daprClient.GetSecretAsync("secretstore", "secret-name", cancellationToken)`
- Never read secrets from `appsettings.json`, environment variables, or hardcoded strings
- Local dev uses the Dapr local file secret store pointing to `/components/secrets.json`

## Configuration API
- Read feature flags via `daprClient.GetConfigurationAsync("configstore", new[] { "key" }, cancellationToken)`
- Subscribe to configuration changes for hot-reload without service restart

## Resiliency
- Define all retry, circuit breaker, and timeout policies in `/components/resiliency.yaml`
- Apply resiliency policies to `order-service` → `inventory-service` calls as the primary example
- Retry policy: 3 retries with exponential backoff
- Circuit breaker: opens after 5 failures within 10 seconds
- Timeout: 5 seconds per Dapr operation

## Observability
- Enable W3C trace context propagation across all services
- Log `TraceId` and `SpanId` as structured properties in every Dapr operation log entry
- Use `ILogger<T>` — never use static loggers or `Console.WriteLine`

## Component Files
- All Dapr YAML component files live in `/components/`
- Local dev components target Redis and local file secret store
- Production component variants (commented out) target AWS services in the same files
