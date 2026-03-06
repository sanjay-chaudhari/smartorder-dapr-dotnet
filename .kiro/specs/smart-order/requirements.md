---
inclusion: always
---

# Requirements Document

## Introduction

SmartOrder is a production-quality e-commerce order processing system built on .NET 10 microservices with Dapr. It demonstrates all major Dapr building blocks through five collaborating services: OrderService, InventoryService, PaymentService, NotificationService, and WorkflowOrchestrator. The system handles the full order lifecycle — from placement through inventory reservation, payment processing, and customer notification — using a saga pattern for distributed coordination and compensation.

## Glossary

- **OrderService**: The microservice that accepts customer orders via REST API, manages order state, and publishes order events.
- **InventoryService**: The microservice that tracks product stock levels and processes reservation and release requests.
- **PaymentService**: The microservice that processes payment charges and issues refunds.
- **NotificationService**: The microservice that sends order confirmation and status update messages to customers.
- **WorkflowOrchestrator**: The microservice that coordinates the end-to-end order saga using the Dapr Workflow API.
- **DaprClient**: The Dapr .NET SDK client used for all Dapr building block interactions.
- **Sidecar**: The Dapr process running alongside each microservice container, providing building block capabilities.
- **StateStore**: The Dapr state management component backed by Redis, named "statestore".
- **PubSub**: The Dapr publish/subscribe component backed by Redis, named "pubsub".
- **SecretStore**: The Dapr secrets management component, named "secretstore".
- **ConfigStore**: The Dapr configuration API component, named "configstore".
- **Order**: A record representing a customer's purchase request, identified by a unique OrderId.
- **Saga**: A distributed transaction pattern where each step has a corresponding compensation action.
- **ETag**: An opaque version token used for optimistic concurrency control in state operations.
- **DeadLetterTopic**: A pub/sub topic that receives messages that could not be successfully processed after all retries.
- **TraceId**: A W3C trace context identifier propagated across all services for distributed tracing correlation.
- **ResiliencyPolicy**: A Dapr component configuration defining retry, circuit breaker, and timeout behaviors.
- **FeatureFlag**: A runtime-configurable boolean or numeric value retrieved from the Dapr Configuration API.

---

## Requirements

### Requirement 1: Order Placement API

**User Story:** As a customer, I want to submit an order via a REST API, so that I can purchase products from the catalog.

#### Acceptance Criteria

1. THE OrderService SHALL expose a `POST /orders` endpoint that accepts a `CreateOrderRequest` containing `ProductId`, `Quantity`, and `Price`.
2. WHEN a `POST /orders` request is received, THE OrderService SHALL validate that `Quantity` is greater than zero and `Price` is greater than zero.
3. WHEN a `POST /orders` request passes validation, THE OrderService SHALL generate a unique `OrderId` and assign the order a status of `Pending`.
4. WHEN a `POST /orders` request passes validation, THE OrderService SHALL return HTTP 202 Accepted with the generated `OrderId` and initial status.
5. IF a `POST /orders` request fails validation, THEN THE OrderService SHALL return HTTP 400 Bad Request with a structured error response identifying the invalid fields.
6. THE OrderService SHALL expose a `GET /orders/{orderId}` endpoint that returns the current state of an order.
7. WHEN a `GET /orders/{orderId}` request is received for a non-existent order, THE OrderService SHALL return HTTP 404 Not Found.
8. THE OrderService SHALL expose a `GET /health` endpoint that returns HTTP 200 with a JSON body indicating service health status.

---

### Requirement 2: Service Invocation — Order to Inventory

**User Story:** As the OrderService, I want to call InventoryService directly using Dapr service invocation, so that I can check and reserve stock without hardcoding service URLs.

#### Acceptance Criteria

1. WHEN processing a new order, THE OrderService SHALL invoke InventoryService using `DaprClient.InvokeMethodAsync` with the Dapr app ID `inventory-service`.
2. THE OrderService SHALL NOT use hardcoded URLs or `HttpClient` for any inter-service communication.
3. WHEN invoking InventoryService, THE OrderService SHALL use the HTTP invocation mode for the `POST /inventory/reserve` endpoint.
4. THE InventoryService SHALL expose a `POST /inventory/reserve` endpoint that accepts a `ReserveInventoryRequest` containing `ProductId`, `Quantity`, and `OrderId`.
5. THE InventoryService SHALL expose a `POST /inventory/release` endpoint that accepts a `ReleaseInventoryRequest` containing `ProductId`, `Quantity`, and `OrderId`.
6. THE InventoryService SHALL expose a `GET /health` endpoint that returns HTTP 200 with a JSON body indicating service health status.
7. IF the InventoryService invocation returns a non-success status, THEN THE OrderService SHALL treat the order as failed and update its status to `InventoryFailed`.
8. WHERE gRPC invocation is configured, THE WorkflowOrchestrator SHALL invoke PaymentService using gRPC mode via `DaprClient.InvokeMethodAsync` with the Dapr app ID `payment-service`.

---

### Requirement 3: Pub/Sub Messaging — Order Events

**User Story:** As the OrderService, I want to publish order events to a shared topic, so that PaymentService and NotificationService can react to new orders independently.

#### Acceptance Criteria

1. WHEN an order is accepted and persisted, THE OrderService SHALL publish an `OrderPlacedEvent` to the topic `order-placed` on the PubSub component `pubsub`.
2. THE PaymentService SHALL subscribe to the topic `order-placed` on the PubSub component `pubsub` using the `[Topic]` attribute on its subscriber endpoint.
3. THE NotificationService SHALL subscribe to the topic `order-placed` on the PubSub component `pubsub` using the `[Topic]` attribute on its subscriber endpoint.
4. WHEN the NotificationService successfully processes an `OrderPlacedEvent`, THE NotificationService SHALL publish a `NotificationSentEvent` to the topic `notification-sent` on the PubSub component `pubsub`.
5. THE PaymentService SHALL expose `app.MapSubscribeHandler()` so that the Dapr sidecar can discover its subscriptions.
6. THE NotificationService SHALL expose `app.MapSubscribeHandler()` so that the Dapr sidecar can discover its subscriptions.
7. IF a subscriber endpoint returns a non-success HTTP status after all retries, THEN THE PubSub component SHALL route the message to the dead-letter topic `order-placed-deadletter`.
8. WHEN a subscriber endpoint successfully processes a message, THE subscriber SHALL return HTTP 200 to acknowledge success.
9. WHEN a subscriber endpoint receives a message it cannot process and should discard, THE subscriber SHALL return HTTP 404 to drop the message without triggering a retry.
10. WHEN a subscriber endpoint encounters a transient error, THE subscriber SHALL return HTTP 500 to signal the Dapr sidecar to retry delivery.
11. THE PaymentService SHALL expose a `GET /health` endpoint that returns HTTP 200 with a JSON body indicating service health status.
12. THE NotificationService SHALL expose a `GET /health` endpoint that returns HTTP 200 with a JSON body indicating service health status.

---

### Requirement 4: State Management — Order State

**User Story:** As the OrderService, I want to persist and retrieve order state using the Dapr state store, so that order data survives service restarts and is accessible across requests.

#### Acceptance Criteria

1. WHEN an order is created, THE OrderService SHALL save the order record to the StateStore using `DaprClient.SaveStateAsync` with the key pattern `order-{orderId}`.
2. WHEN retrieving an order, THE OrderService SHALL read the order record from the StateStore using `DaprClient.GetStateAndETagAsync` to obtain both the value and its ETag.
3. WHEN updating an order's status, THE OrderService SHALL use `DaprClient.TrySaveStateAsync` with the previously retrieved ETag to perform an optimistic concurrency check.
4. IF an optimistic concurrency update fails due to an ETag mismatch, THEN THE OrderService SHALL retry the read-modify-write cycle up to 3 times before returning HTTP 409 Conflict.
5. WHEN creating an order that involves multiple state keys, THE OrderService SHALL use `DaprClient.ExecuteStateTransactionAsync` to write all keys atomically.
6. THE OrderService SHALL use the state store component named `statestore` for all state operations.
7. IF a state store operation throws a `DaprException`, THEN THE OrderService SHALL log the exception with structured properties `{ServiceName}`, `{OperationName}`, and `{OrderId}`, and return HTTP 503 Service Unavailable.
8. WHEN saving financial or inventory state, THE OrderService SHALL use `StateOptions` with `Consistency.Strong` to ensure strong consistency guarantees.

---

### Requirement 5: Dapr Workflow — Order Saga

**User Story:** As the WorkflowOrchestrator, I want to coordinate the order processing saga using Dapr Workflow, so that each step executes reliably and failures trigger appropriate compensation.

#### Acceptance Criteria

1. THE WorkflowOrchestrator SHALL implement an order saga workflow with the following sequential steps: `ValidateOrder`, `ReserveInventory`, `ProcessPayment`, `SendNotification`.
2. WHEN a workflow is started, THE WorkflowOrchestrator SHALL execute `ValidateOrder` as the first activity using `WorkflowActivityContext`.
3. WHEN `ValidateOrder` succeeds, THE WorkflowOrchestrator SHALL execute `ReserveInventory` as the second activity.
4. WHEN `ReserveInventory` succeeds, THE WorkflowOrchestrator SHALL execute `ProcessPayment` as the third activity.
5. WHEN `ProcessPayment` succeeds, THE WorkflowOrchestrator SHALL execute `SendNotification` as the fourth activity.
6. IF `ProcessPayment` fails, THEN THE WorkflowOrchestrator SHALL execute a `ReleaseInventoryReservation` compensation activity before marking the workflow as failed.
7. IF `ProcessPayment` fails after a charge was already applied, THEN THE WorkflowOrchestrator SHALL execute a `RefundPayment` compensation activity to reverse the charge before marking the workflow as failed.
8. IF `ReserveInventory` fails, THEN THE WorkflowOrchestrator SHALL mark the workflow as failed without executing `ProcessPayment` or `SendNotification`.
9. THE WorkflowOrchestrator SHALL expose a `POST /workflow/orders` endpoint to start a new order saga workflow.
10. THE WorkflowOrchestrator SHALL expose a `GET /workflow/orders/{instanceId}` endpoint to query the current status of a running or completed workflow.
11. THE WorkflowOrchestrator SHALL expose a `GET /health` endpoint that returns HTTP 200 with a JSON body indicating service health status.
12. WHEN a workflow completes successfully, THE WorkflowOrchestrator SHALL record the final workflow status as `Completed`.
13. WHEN a workflow fails after compensation, THE WorkflowOrchestrator SHALL record the final workflow status as `Failed` with a reason string.
14. THE WorkflowOrchestrator SHALL register the order saga workflow and all activity classes using `builder.Services.AddDaprWorkflow()` in `Program.cs`.

---

### Requirement 6: Secrets Management

**User Story:** As a developer, I want all sensitive configuration values retrieved from the Dapr secret store, so that secrets are never hardcoded in source code or configuration files.

#### Acceptance Criteria

1. THE OrderService SHALL retrieve all connection strings and API keys using `DaprClient.GetSecretAsync` with the secret store component named `secretstore`.
2. THE PaymentService SHALL retrieve all connection strings and API keys using `DaprClient.GetSecretAsync` with the secret store component named `secretstore`.
3. THE NotificationService SHALL retrieve all connection strings and API keys using `DaprClient.GetSecretAsync` with the secret store component named `secretstore`.
4. THE SecretStore component SHALL be configured to use the local file secret store for local development environments, pointing to `/components/secrets.json`.
5. THE SecretStore component configuration file SHALL be located at `components/secretstore.yaml`.
6. No service SHALL contain hardcoded secrets, connection strings, or API keys in `appsettings.json`, environment variables, or source code.
7. IF a `DaprException` is thrown during secret retrieval at startup, THEN THE affected service SHALL log the error with structured properties `{ServiceName}` and `{SecretName}` and terminate with a non-zero exit code.

---

### Requirement 7: Observability — Distributed Tracing

**User Story:** As a developer, I want distributed traces correlated across all services, so that I can diagnose latency and failures in the order processing pipeline.

#### Acceptance Criteria

1. THE OrderService SHALL enable OpenTelemetry distributed tracing via the Dapr sidecar configuration.
2. THE InventoryService SHALL enable OpenTelemetry distributed tracing via the Dapr sidecar configuration.
3. THE PaymentService SHALL enable OpenTelemetry distributed tracing via the Dapr sidecar configuration.
4. THE NotificationService SHALL enable OpenTelemetry distributed tracing via the Dapr sidecar configuration.
5. THE WorkflowOrchestrator SHALL enable OpenTelemetry distributed tracing via the Dapr sidecar configuration.
6. WHEN a request enters any service, THE service SHALL propagate W3C trace context headers (`traceparent`, `tracestate`) to all outbound Dapr calls.
7. WHEN writing a log entry, EVERY service SHALL include structured log properties `{TraceId}` and `{SpanId}` derived from the current activity context.
8. THE docker-compose configuration SHALL include a Zipkin container reachable at `http://zipkin:9411` for trace visualization.
9. THE Dapr sidecar configuration for each service SHALL export traces to the Zipkin endpoint at `http://zipkin:9411/api/v2/spans`.
10. WHEN writing a log entry, EVERY service SHALL include the structured log property `{ServiceName}` identifying the originating service.

---

### Requirement 8: Configuration API — Feature Flags

**User Story:** As an operator, I want to manage feature flags via the Dapr Configuration API, so that I can change application behavior at runtime without restarting services.

#### Acceptance Criteria

1. WHEN OrderService starts, THE OrderService SHALL read the configuration keys `max-order-quantity` and `discount-enabled` from the ConfigStore component named `configstore`.
2. WHEN `max-order-quantity` is set, THE OrderService SHALL reject any `CreateOrderRequest` where `Quantity` exceeds the configured maximum, returning HTTP 422 Unprocessable Entity.
3. WHEN `discount-enabled` is `true`, THE OrderService SHALL apply a discount to the order price as defined by the discount business rule.
4. THE OrderService SHALL subscribe to configuration change notifications for `max-order-quantity` and `discount-enabled` so that updated values take effect without a service restart.
5. WHEN a configuration value changes, THE OrderService SHALL log the change with structured properties `{ConfigKey}` and `{NewValue}`.
6. IF the ConfigStore is unavailable at startup, THEN THE OrderService SHALL use default values (`max-order-quantity` = 100, `discount-enabled` = false) and log a warning.
7. THE ConfigStore component configuration file SHALL be located at `components/configuration.yaml`.

---

### Requirement 9: Resiliency Policies

**User Story:** As an operator, I want resiliency policies applied to inter-service calls, so that transient failures do not cause cascading outages.

#### Acceptance Criteria

1. THE ResiliencyPolicy component SHALL define a retry policy with 3 maximum retries and exponential backoff for service invocation calls.
2. THE ResiliencyPolicy component SHALL define a circuit breaker policy that opens after 5 consecutive failures within a 10-second window.
3. THE ResiliencyPolicy component SHALL define a timeout policy of 5 seconds per Dapr operation.
4. THE ResiliencyPolicy component configuration file SHALL be located at `components/resiliency.yaml`.
5. WHEN OrderService invokes InventoryService, THE Dapr sidecar SHALL apply the resiliency policy defined in `resiliency.yaml`.
6. WHEN the circuit breaker is open, THE OrderService SHALL receive a `DaprException` indicating the circuit is open, log the event with `{ServiceName}` and `{TargetService}`, and return HTTP 503 Service Unavailable.
7. WHEN a retried call eventually succeeds within the retry policy, THE OrderService SHALL continue normal processing without surfacing the transient failure to the caller.

---

### Requirement 10: Docker Compose Infrastructure

**User Story:** As a developer, I want a single `docker-compose.yml` to run the entire SmartOrder system locally, so that I can develop and test all services together without manual setup.

#### Acceptance Criteria

1. THE docker-compose configuration SHALL define one container per microservice: `order-service`, `inventory-service`, `payment-service`, `notification-service`, `workflow-orchestrator`.
2. THE docker-compose configuration SHALL define a Dapr sidecar container for each microservice using the `daprd` image.
3. THE docker-compose configuration SHALL define a Redis container used as both the StateStore and PubSub broker.
4. THE docker-compose configuration SHALL define a Zipkin container for distributed trace visualization.
5. ALL service containers and sidecar containers SHALL be connected to a shared Docker network named `dapr-network`.
6. EACH microservice container SHALL mount the `components/` directory so that Dapr component YAML files are available to the sidecar.
7. EACH microservice container SHALL define a health check that calls the service's `GET /health` endpoint with a 30-second interval and 3 retries.
8. THE docker-compose configuration SHALL not hardcode any secrets; all secret values SHALL be provided via the local file secret store mounted into the container.

---

### Requirement 11: Dapr Component Configuration Files

**User Story:** As a developer, I want all Dapr components defined as YAML files in the `components/` directory, so that the system is fully reproducible and version-controlled.

#### Acceptance Criteria

1. THE StateStore component SHALL be defined in `components/statestore.yaml` with type `state.redis` targeting the Redis container.
2. THE PubSub component SHALL be defined in `components/pubsub.yaml` with type `pubsub.redis` targeting the Redis container.
3. THE SecretStore component SHALL be defined in `components/secretstore.yaml` with type `secretstores.local.file` pointing to a local secrets JSON file.
4. THE ResiliencyPolicy SHALL be defined in `components/resiliency.yaml` with the retry, circuit breaker, and timeout policies specified in Requirement 9.
5. THE ConfigStore component SHALL be defined in `components/configuration.yaml` with type `configuration.redis` targeting the Redis container.
6. ALL component YAML files SHALL specify `scopes` to restrict each component to only the services that require it.

---

### Requirement 12: Project Structure and .NET Standards

**User Story:** As a developer, I want a consistent project structure and coding standards enforced across all services, so that the codebase is maintainable and onboarding is straightforward.

#### Acceptance Criteria

1. ALL projects SHALL target `net10.0` in their `.csproj` files.
2. ALL projects SHALL enable nullable reference types via `<Nullable>enable</Nullable>` in their `.csproj` files.
3. ALL DTOs SHALL be defined as C# `record` types with positional parameters.
4. ALL async methods SHALL accept a `CancellationToken` parameter.
5. THE DaprClient SHALL be registered via dependency injection using `builder.Services.AddDaprClient()` in every service's `Program.cs`.
6. ALL Dapr calls SHALL be wrapped in `try/catch` blocks that catch `DaprException` and log the error before rethrowing or returning an appropriate HTTP error response.
7. ALL services SHALL use `ILogger<T>` with structured log properties `{ServiceName}`, `{OperationName}`, and where applicable `{OrderId}`.
8. NO service SHALL use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on any `Task` or `ValueTask`.
9. ALL services SHALL use ASP.NET Core Minimal APIs; no MVC controllers SHALL be used.
10. WHEN an unhandled exception occurs in a Minimal API endpoint, THE service SHALL return `Results.Problem()` with an appropriate HTTP status code rather than propagating the raw exception.
11. ALL projects SHALL enable implicit usings via `<ImplicitUsings>enable</ImplicitUsings>` in their `.csproj` files.
12. ALL library and infrastructure code SHALL use `ConfigureAwait(false)` on every awaited call.
13. EACH service SHALL follow the per-service folder layout: `Program.cs`, `Endpoints/`, `Services/`, `Models/`, and `Components/` (where Dapr workflow activities apply).
14. THE WorkflowOrchestrator SHALL register the order saga workflow and all activity classes using `builder.Services.AddDaprWorkflow()` in `Program.cs`.
15. THE project directory structure SHALL follow the layout defined in the application overview: `src/`, `components/`, `tests/`, `docker-compose.yml`.

---

### Requirement 13: Unit Testing

**User Story:** As a developer, I want unit tests for all business logic, so that I can verify correctness in isolation without running Dapr infrastructure.

#### Acceptance Criteria

1. THE `tests/OrderService.Tests` project SHALL contain xUnit unit tests with FluentAssertions for all business logic classes in OrderService.
2. THE `tests/InventoryService.Tests` project SHALL contain xUnit unit tests with FluentAssertions for all business logic classes in InventoryService.
3. ALL unit tests SHALL mock `IDaprClient` using NSubstitute to avoid real Dapr sidecar dependencies.
4. Test class names SHALL follow the pattern `{ClassName}Tests` and test method names SHALL follow the pattern `{MethodName}_When{Condition}_Should{ExpectedResult}`.
5. THE unit test suite SHALL achieve at least 80% line coverage for all business logic classes.
6. WHEN testing order validation logic, THE unit tests SHALL cover valid inputs, zero quantity, negative price, and quantity exceeding `max-order-quantity`.
7. WHEN testing state management logic, THE unit tests SHALL cover successful save, ETag mismatch on update, and `DaprException` on save failure.
8. WHEN testing pub/sub publish logic, THE unit tests SHALL cover successful publish and `DaprException` on publish failure.

---

### Requirement 14: Integration Testing

**User Story:** As a developer, I want integration tests that exercise real Dapr building blocks, so that I can verify end-to-end behavior before deployment.

#### Acceptance Criteria

1. THE `tests/Integration.Tests` project SHALL use TestContainers to spin up a Dapr sidecar and Redis container for each integration test run.
2. THE integration test suite SHALL include at least one test per Dapr building block: service invocation, pub/sub, state management, workflow, secrets, and configuration.
3. WHEN testing service invocation, THE integration tests SHALL verify that OrderService successfully calls InventoryService via Dapr and receives a valid response.
4. WHEN testing pub/sub, THE integration tests SHALL verify that a message published to `order-placed` is received by a subscriber within 5 seconds.
5. WHEN testing state management, THE integration tests SHALL verify the round-trip property: saving a state value and then reading it back returns an equivalent object.
6. WHEN testing the order saga workflow, THE integration tests SHALL verify that a successful order progresses through all four steps and reaches `Completed` status.
7. WHEN testing the order saga workflow, THE integration tests SHALL verify that a payment failure triggers the `ReleaseInventoryReservation` compensation and the workflow reaches `Failed` status.
8. IF an integration test container fails to start within 60 seconds, THEN THE test SHALL be marked as failed with a descriptive timeout message.
