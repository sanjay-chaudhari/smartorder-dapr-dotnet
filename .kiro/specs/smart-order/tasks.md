# Implementation Plan: SmartOrder

## Overview

Implement the SmartOrder .NET 10 Dapr microservices application in layers: solution scaffolding first, then Dapr component files, then each service with its models/endpoints/business logic, then the workflow saga, then Docker Compose, and finally the full test suite. Each task builds on the previous and ends with all code wired together.

## Tasks

- [x] 1. Solution scaffolding — create SmartOrder.sln, all .csproj files, and folder structure
  - Create `SmartOrder.sln` at the repo root
  - Create `src/OrderService/OrderService.csproj` targeting `net10.0` with `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, and NuGet refs: `Dapr.AspNetCore`, `Dapr.Client`, `FluentValidation.AspNetCore`
  - Create `src/InventoryService/InventoryService.csproj` with same base settings and `Dapr.AspNetCore`, `Dapr.Client`
  - Create `src/PaymentService/PaymentService.csproj` with same base settings and `Dapr.AspNetCore`, `Dapr.Client`
  - Create `src/NotificationService/NotificationService.csproj` with same base settings and `Dapr.AspNetCore`, `Dapr.Client`
  - Create `src/WorkflowOrchestrator/WorkflowOrchestrator.csproj` with same base settings and `Dapr.AspNetCore`, `Dapr.Client`, `Dapr.Workflow`
  - Create `tests/OrderService.Tests/OrderService.Tests.csproj` with `xunit`, `FluentAssertions`, `NSubstitute`, `FsCheck.Xunit`, project ref to OrderService
  - Create `tests/InventoryService.Tests/InventoryService.Tests.csproj` with same test deps, project ref to InventoryService
  - Create `tests/Integration.Tests/Integration.Tests.csproj` with `xunit`, `FluentAssertions`, `Testcontainers.Redis`, `Testcontainers`
  - Add all projects to `SmartOrder.sln`
  - Create empty placeholder `Program.cs` in each `src/` service so the solution builds
  - _Requirements: 12.1, 12.2, 12.11, 12.15_


- [x] 2. Dapr component YAML files and secrets
  - Create `components/statestore.yaml` — type `state.redis`, host `redis:6379`, `actorStateStore: true`, scopes: `order-service`, `inventory-service`, `workflow-orchestrator`
  - Create `components/pubsub.yaml` — type `pubsub.redis`, host `redis:6379`, scopes: `order-service`, `payment-service`, `notification-service`
  - Create `components/secretstore.yaml` — type `secretstores.local.file`, `secretsFile: /components/secrets.json`, scopes: `order-service`, `payment-service`, `notification-service`
  - Create `components/configuration.yaml` — type `configuration.redis`, host `redis:6379`, scopes: `order-service`
  - Create `components/resiliency.yaml` — retry policy `retryThreeTimes` (exponential, maxRetries=3, maxInterval=10s), circuit breaker `simpleCB` (consecutiveFailures>=5, interval=10s, timeout=30s), timeout `general` (5s), target: `inventory-service` from `order-service`
  - Create `components/zipkin.yaml` — Dapr `Configuration` kind, samplingRate=1, zipkin endpoint `http://zipkin:9411/api/v2/spans`
  - Create `components/secrets.json` with placeholder keys: `payment-api-key`, `smtp-password`, `redis-connection-string`
  - _Requirements: 6.4, 6.5, 8.7, 9.1, 9.2, 9.3, 9.4, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 3. OrderService — models, interfaces, and FluentValidation
  - Create `src/OrderService/Models/OrderModels.cs` with all record types: `CreateOrderRequest`, `CreateOrderResponse`, `OrderResponse`, `Order`, `OrderPlacedEvent`, and `OrderStatus` enum
  - Create `src/OrderService/Services/IOrderService.cs`, `IOrderStateService.cs`, `IConfigurationService.cs` interfaces
  - Create `src/OrderService/Services/OrderValidator.cs` — FluentValidation `AbstractValidator<CreateOrderRequest>` checking `Quantity > 0` and `Price > 0`
  - _Requirements: 1.2, 1.5, 12.3, 12.9_

- [x] 4. OrderService — ConfigurationService (Dapr Configuration API)
  - Create `src/OrderService/Services/ConfigurationService.cs` implementing `IConfigurationService`
  - On startup call `DaprClient.GetConfigurationAsync("configstore", ["max-order-quantity", "discount-enabled"], ct)` with `ConfigureAwait(false)`
  - Subscribe to configuration changes via `DaprClient.SubscribeConfigurationAsync` for hot-reload
  - Log config changes with `{ConfigKey}` and `{NewValue}` structured properties
  - Fall back to defaults (`max-order-quantity=100`, `discount-enabled=false`) and log a warning if ConfigStore is unavailable
  - _Requirements: 8.1, 8.4, 8.5, 8.6_


- [x] 5. OrderService — OrderStateService (Dapr state management with ETag)
  - Create `src/OrderService/Services/OrderStateService.cs` implementing `IOrderStateService`
  - Implement `SaveOrderAsync` using `DaprClient.ExecuteStateTransactionAsync("statestore", ...)` with `StateOptions { Consistency = Consistency.Strong }` for atomic writes
  - Implement `GetOrderAsync` using `DaprClient.GetStateAndETagAsync<Order>("statestore", "order-{orderId}", ct)` with `ConfigureAwait(false)`
  - Implement `UpdateOrderStatusAsync` with a read-modify-write loop: up to 3 attempts using `DaprClient.TrySaveStateAsync` with the retrieved ETag; throw `ConcurrencyException` after 3 failures
  - Wrap all Dapr calls in `try/catch (DaprException)`, log with `{ServiceName}`, `{OperationName}`, `{OrderId}`, and rethrow
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_

  - [x]* 5.1 Write property test for state key pattern (Property 8)
    - **Property 8: State key pattern is always order-{orderId}**
    - **Validates: Requirements 4.1, 4.6**

  - [x]* 5.2 Write property test for state round-trip (Property 9)
    - **Property 9: State round-trip preserves order data**
    - **Validates: Requirements 4.2, 14.5**

  - [x]* 5.3 Write property test for ETag retry count (Property 10)
    - **Property 10: ETag conflict retries exactly 3 times before failing**
    - **Validates: Requirements 4.4**

- [x] 6. OrderService — core OrderService business logic
  - Create `src/OrderService/Services/OrderService.cs` implementing `IOrderService`
  - Inject `IDaprClient`, `IOrderStateService`, `IConfigurationService`, `ILogger<OrderService>`, `OrderValidator`
  - Implement `CreateOrderAsync`: validate request (FluentValidation + max-order-quantity check → 422), generate `OrderId`, apply discount if `discount-enabled`, call `IOrderStateService.SaveOrderAsync`, invoke InventoryService via `DaprClient.InvokeMethodAsync("inventory-service", "inventory/reserve", request, ct)`, on inventory failure set status `InventoryFailed` and return without publishing, on success publish `OrderPlacedEvent` via `DaprClient.PublishEventAsync("pubsub", "order-placed", event, ct)`
  - Retrieve secrets at startup via `DaprClient.GetSecretAsync("secretstore", ..., ct)`; log and terminate on `DaprException`
  - Log all operations with `{ServiceName}`, `{OperationName}`, `{OrderId}`, `{TraceId}`, `{SpanId}` using `ILogger.BeginScope`
  - _Requirements: 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.7, 3.1, 4.1, 6.1, 7.7, 7.10, 8.2, 8.3, 12.6, 12.7_

  - [x]* 6.1 Write property test for valid order creation (Property 1)
    - **Property 1: Valid order creation returns 202 with Pending status**
    - **Validates: Requirements 1.2, 1.3, 1.4**

  - [x]* 6.2 Write property test for invalid order rejection (Property 2)
    - **Property 2: Invalid order inputs are rejected with 400**
    - **Validates: Requirements 1.2, 1.5**

  - [x]* 6.3 Write property test for inventory invocation (Property 4)
    - **Property 4: Order creation triggers inventory service invocation**
    - **Validates: Requirements 2.1, 2.3**

  - [x]* 6.4 Write property test for inventory failure handling (Property 5)
    - **Property 5: Inventory failure marks order as InventoryFailed**
    - **Validates: Requirements 2.7**

  - [x]* 6.5 Write property test for OrderPlacedEvent publish (Property 6)
    - **Property 6: Accepted order publishes OrderPlacedEvent**
    - **Validates: Requirements 3.1, 3.8**

  - [x]* 6.6 Write property test for DaprException → 503 (Property 11)
    - **Property 11: DaprException on state operation returns HTTP 503**
    - **Validates: Requirements 4.7, 12.6**

  - [x]* 6.7 Write property test for max-order-quantity enforcement (Property 15)
    - **Property 15: Quantity exceeding max-order-quantity is rejected with 422**
    - **Validates: Requirements 8.2**

  - [x]* 6.8 Write property test for discount application (Property 16)
    - **Property 16: Discount is applied when discount-enabled is true**
    - **Validates: Requirements 8.3**


- [x] 7. OrderService — endpoints and Program.cs
  - Create `src/OrderService/Endpoints/OrderEndpoints.cs` with `MapOrderEndpoints(this WebApplication app)` extension method
  - `POST /orders` — call `IOrderService.CreateOrderAsync`, return `Results.Accepted` (202) on success, `Results.ValidationProblem` on validation failure, `Results.Problem(statusCode: 422)` on quantity exceeded, `Results.Problem(statusCode: 503)` on `DaprException`, `Results.Problem()` on unhandled exception
  - `GET /orders/{orderId}` — call `IOrderStateService.GetOrderAsync`, return `Results.Ok` or `Results.NotFound`
  - `GET /health` — return `Results.Ok(new { status = "healthy" })`
  - Create `src/OrderService/Program.cs`: register `builder.Services.AddDaprClient()`, `builder.Services.AddHealthChecks()`, register `IOrderService`, `IOrderStateService`, `IConfigurationService`, `OrderValidator` via `AddOrderServices()` extension; call `app.MapOrderEndpoints()`, `app.MapHealthChecks("/health")`
  - _Requirements: 1.1, 1.4, 1.5, 1.6, 1.7, 1.8, 12.5, 12.9, 12.10_

  - [x]* 7.1 Write property test for non-existent order lookup (Property 3)
    - **Property 3: Non-existent order lookup returns 404**
    - **Validates: Requirements 1.7**

- [x] 8. Checkpoint — OrderService unit tests
  - Create `tests/OrderService.Tests/OrderServiceTests.cs` with xUnit + FluentAssertions + NSubstitute mocks for `IDaprClient`
  - Cover: valid order creation, zero quantity → 400, negative price → 400, quantity > max → 422, inventory failure → `InventoryFailed`, `DaprException` on publish → 503
  - Create `tests/OrderService.Tests/OrderStateServiceTests.cs`
  - Cover: successful save, ETag mismatch retry → success on 2nd attempt, 3 ETag failures → `ConcurrencyException`, `DaprException` on save → logged + rethrown
  - Create `tests/OrderService.Tests/ConfigurationServiceTests.cs`
  - Cover: config read on startup, default fallback when ConfigStore unavailable, config change notification triggers update
  - Follow naming: `{MethodName}_When{Condition}_Should{ExpectedResult}`
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 13.1, 13.3, 13.4, 13.6, 13.7, 13.8_

- [x] 9. InventoryService — models, service, endpoints, and Program.cs
  - Create `src/InventoryService/Models/InventoryModels.cs` with records: `ReserveInventoryRequest`, `ReserveInventoryResponse`, `ReleaseInventoryRequest`, `ReleaseInventoryResponse`, `InventoryItem`
  - Create `src/InventoryService/Services/IInventoryService.cs` and `InventoryService.cs`
  - `ReserveAsync`: read `InventoryItem` via `GetStateAndETagAsync("statestore", "inventory-{productId}", ct)`, check `AvailableQuantity >= Quantity`, update with `TrySaveStateAsync` using `StateOptions { Consistency = Consistency.Strong }`, return `ReserveInventoryResponse`
  - `ReleaseAsync`: read, increment `AvailableQuantity`, decrement `ReservedQuantity`, save with ETag
  - Wrap all Dapr calls in `try/catch (DaprException)`, log with `{ServiceName}`, `{OperationName}`
  - Create `src/InventoryService/Endpoints/InventoryEndpoints.cs` — `POST /inventory/reserve`, `POST /inventory/release`, `GET /health`
  - Create `src/InventoryService/Program.cs` — `AddDaprClient()`, `AddHealthChecks()`, register `IInventoryService`, map endpoints
  - _Requirements: 2.4, 2.5, 2.6, 4.8, 12.5, 12.6, 12.7_

  - [x]* 9.1 Write unit tests for InventoryService
    - Cover: successful reservation, insufficient stock → `Success=false`, release logic, `DaprException` handling
    - Test class: `InventoryServiceTests`, follow naming convention
    - _Requirements: 13.2, 13.3, 13.4_


- [x] 10. PaymentService — models, service, endpoints, and Program.cs
  - Create `src/PaymentService/Models/PaymentModels.cs` with records: `ProcessPaymentRequest`, `ProcessPaymentResponse`, `RefundPaymentRequest`, `RefundPaymentResponse`
  - Create `src/PaymentService/Services/IPaymentService.cs` and `PaymentService.cs`
  - On startup retrieve `payment-api-key` via `DaprClient.GetSecretAsync("secretstore", "payment-api-key", ct)`; log `{ServiceName}`, `{SecretName}` and terminate on `DaprException`
  - Implement `ProcessPaymentAsync` and `RefundAsync` with structured logging (`{ServiceName}`, `{OperationName}`, `{OrderId}`, `{TraceId}`, `{SpanId}`)
  - Create `src/PaymentService/Endpoints/PaymentEndpoints.cs`
  - `POST /payments/process` — gRPC invocation target; wrap in try/catch returning `Results.Problem(statusCode: 503)` on `DaprException`
  - `POST /subscribe/order-placed` — decorated with `[Topic("pubsub", "order-placed")]`; return 200 on success, 404 to drop, 500 to retry
  - `GET /health`
  - Create `src/PaymentService/Program.cs` — `AddDaprClient()`, `AddHealthChecks()`, register `IPaymentService`, `app.MapSubscribeHandler()`, map endpoints
  - _Requirements: 3.2, 3.5, 3.7, 3.8, 3.9, 3.10, 3.11, 6.2, 6.7, 12.5, 12.6, 12.7_

  - [x]* 10.1 Write property test for secrets retrieval (Property 17 — PaymentService)
    - **Property 17: Secrets are retrieved via DaprClient, never hardcoded**
    - **Validates: Requirements 6.2**

- [x] 11. NotificationService — models, service, endpoints, and Program.cs
  - Create `src/NotificationService/Models/NotificationModels.cs` with records: `SendNotificationRequest`, `NotificationSentEvent`
  - Create `src/NotificationService/Services/INotificationService.cs` and `NotificationService.cs`
  - On startup retrieve `smtp-password` via `DaprClient.GetSecretAsync("secretstore", "smtp-password", ct)`; log and terminate on `DaprException`
  - Implement `SendNotificationAsync`: process the notification, then publish `NotificationSentEvent` via `DaprClient.PublishEventAsync("pubsub", "notification-sent", event, ct)` with `ConfigureAwait(false)`
  - Create `src/NotificationService/Endpoints/NotificationEndpoints.cs`
  - `POST /subscribe/order-placed` — decorated with `[Topic("pubsub", "order-placed")]`; return 200 on success, 404 to drop, 500 to retry
  - `GET /health`
  - Create `src/NotificationService/Program.cs` — `AddDaprClient()`, `AddHealthChecks()`, register `INotificationService`, `app.MapSubscribeHandler()`, map endpoints
  - _Requirements: 3.3, 3.4, 3.6, 3.7, 3.8, 3.9, 3.10, 3.12, 6.3, 6.7, 12.5, 12.6, 12.7_

  - [x]* 11.1 Write property test for NotificationSentEvent publish (Property 7)
    - **Property 7: NotificationService publishes NotificationSentEvent after processing**
    - **Validates: Requirements 3.4**

  - [x]* 11.2 Write property test for secrets retrieval (Property 17 — NotificationService)
    - **Property 17: Secrets are retrieved via DaprClient, never hardcoded**
    - **Validates: Requirements 6.3**

- [x] 12. Checkpoint — ensure OrderService, InventoryService, PaymentService, NotificationService build and all unit tests pass
  - Ensure all tests pass, ask the user if questions arise.


- [x] 13. WorkflowOrchestrator — models and workflow activity classes
  - Create `src/WorkflowOrchestrator/Models/WorkflowModels.cs` with all records: `StartWorkflowRequest`, `StartWorkflowResponse`, `WorkflowStatusResponse`, `OrderSagaInput`, `OrderSagaResult`, `ValidateOrderInput`, `ValidationResult`, `ReserveInventoryInput`, `ReservationResult`, `ProcessPaymentInput`, `PaymentResult`, `SendNotificationInput`, `NotificationResult`, `ReleaseInventoryInput`, `RefundPaymentInput`; and `WorkflowStatus` enum
  - Create `src/WorkflowOrchestrator/Components/ValidateOrderActivity.cs` — extends `WorkflowActivity<ValidateOrderInput, ValidationResult>`; validates quantity > 0 and price > 0
  - Create `src/WorkflowOrchestrator/Components/ReserveInventoryActivity.cs` — extends `WorkflowActivity<ReserveInventoryInput, ReservationResult>`; invokes `inventory-service` via `DaprClient.InvokeMethodAsync`
  - Create `src/WorkflowOrchestrator/Components/ProcessPaymentActivity.cs` — extends `WorkflowActivity<ProcessPaymentInput, PaymentResult>`; invokes `payment-service` via gRPC `DaprClient.InvokeMethodAsync`
  - Create `src/WorkflowOrchestrator/Components/SendNotificationActivity.cs` — extends `WorkflowActivity<SendNotificationInput, NotificationResult>`; invokes `notification-service`
  - Create `src/WorkflowOrchestrator/Components/ReleaseInventoryReservationActivity.cs` — compensation; invokes `inventory-service` `POST /inventory/release`
  - Create `src/WorkflowOrchestrator/Components/RefundPaymentActivity.cs` — compensation; invokes `payment-service` refund endpoint
  - All activities inject `IDaprClient` and `ILogger<T>`; wrap Dapr calls in `try/catch (DaprException)`
  - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 2.8, 12.6, 12.7_

- [x] 14. WorkflowOrchestrator — OrderSagaWorkflow, WorkflowService, endpoints, and Program.cs
  - Create `src/WorkflowOrchestrator/Components/OrderSagaWorkflow.cs` — extends `Workflow<OrderSagaInput, OrderSagaResult>`
  - Implement saga sequence: `ValidateOrder` → `ReserveInventory` → `ProcessPayment` → `SendNotification`
  - On `ReserveInventory` failure: return `OrderSagaResult(false, reason)` without calling payment
  - On `ProcessPayment` failure: call `ReleaseInventoryReservationActivity`; if `TransactionId` is non-null also call `RefundPaymentActivity`; return `OrderSagaResult(false, reason)`
  - On full success: return `OrderSagaResult(true, null)`
  - Create `src/WorkflowOrchestrator/Services/IWorkflowService.cs` and `WorkflowService.cs`
  - `StartOrderSagaAsync`: call `DaprClient.StartWorkflowAsync("dapr", nameof(OrderSagaWorkflow), instanceId, input, ct)`
  - `GetStatusAsync`: call `DaprClient.GetWorkflowAsync("dapr", instanceId, ct)` and map to `WorkflowStatusResponse`
  - Create `src/WorkflowOrchestrator/Endpoints/WorkflowEndpoints.cs`
  - `POST /workflow/orders` — start saga, return 202 with `instanceId`
  - `GET /workflow/orders/{instanceId}` — return current status
  - `GET /health`
  - Create `src/WorkflowOrchestrator/Program.cs` — `AddDaprClient()`, `AddHealthChecks()`, `AddDaprWorkflow(options => { options.RegisterWorkflow<OrderSagaWorkflow>(); options.RegisterActivity<ValidateOrderActivity>(); /* all 6 activities */ })`, register `IWorkflowService`, map endpoints
  - _Requirements: 5.1, 5.8, 5.9, 5.10, 5.11, 5.12, 5.13, 5.14, 12.5, 12.14_

  - [x]* 14.1 Write property test for successful saga sequence (Property 12)
    - **Property 12: Successful saga executes all four activities in order and reaches Completed**
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.12, 14.6**

  - [x]* 14.2 Write property test for payment failure compensation (Property 13)
    - **Property 13: Payment failure triggers compensation and reaches Failed status**
    - **Validates: Requirements 5.6, 5.7, 5.13, 14.7**

  - [x]* 14.3 Write property test for inventory failure short-circuit (Property 14)
    - **Property 14: Inventory failure skips payment and reaches Failed status**
    - **Validates: Requirements 5.8**

- [x] 15. Checkpoint — WorkflowOrchestrator unit tests
  - Create `tests/OrderService.Tests/OrderSagaWorkflowTests.cs` (or a dedicated `WorkflowOrchestrator.Tests` project if preferred)
  - Cover: happy path all four activities called in order → `Completed`, payment failure → `ReleaseInventoryReservation` called → `Failed`, payment failure with `TransactionId` → `RefundPayment` also called, inventory failure → payment never called → `Failed`
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 13.1, 13.4_


- [x] 16. Docker Compose — full local stack
  - Create `docker-compose.yml` at repo root
  - Define `dapr-network` bridge network
  - `redis` service: `redis:7-alpine`, port 6379, healthcheck `redis-cli ping`
  - `zipkin` service: `openzipkin/zipkin:latest`, port 9411
  - For each of the 5 microservices define an app container and a paired `{service}-dapr` sidecar container:
    - App container: build from `./src/{ServiceName}`, expose correct port (5001–5005), mount `./components:/components`, `depends_on: [redis]`, healthcheck `curl -f http://localhost:{port}/health` with 30s interval and 3 retries, connected to `dapr-network`
    - Sidecar container: `daprd:1.14` image, command with `-app-id`, `-app-port`, `-dapr-http-port`, `-components-path /components`, `-config /components/zipkin.yaml`, `network_mode: service:{app-container}`, mount `./components:/components`, `depends_on: [{app}, redis, zipkin]`
  - No hardcoded secrets — all secrets provided via the local file secret store mounted at `/components/secrets.json`
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 7.8, 7.9_

- [x] 17. Integration tests — Testcontainers setup and per-building-block tests
  - Create `tests/Integration.Tests/IntegrationTestBase.cs` — `IAsyncLifetime` base class that starts Redis and Dapr sidecar Testcontainers with a 60-second startup timeout; fails with descriptive message on timeout
  - Create `tests/Integration.Tests/ServiceInvocationTests.cs` — `OrderService_InvokesInventoryService_ReturnsValidResponse`: verify `ReserveInventoryResponse` received via Dapr service invocation
  - Create `tests/Integration.Tests/PubSubTests.cs` — `OrderPlaced_PublishedAndReceived_WithinFiveSeconds`: publish to `order-placed`, assert subscriber receives within 5 seconds
  - Create `tests/Integration.Tests/StateManagementTests.cs` — `Order_SavedAndRead_ReturnsEquivalentObject`: save an `Order` record, read it back, assert field equality (Property 9 round-trip)
  - Create `tests/Integration.Tests/WorkflowTests.cs` — `OrderSaga_HappyPath_ReachesCompleted` and `OrderSaga_PaymentFailure_ReachesFailedWithCompensation`
  - Create `tests/Integration.Tests/SecretsTests.cs` — `OrderService_ReadsSecret_FromSecretStore`: verify secret value matches expected
  - Create `tests/Integration.Tests/ConfigurationTests.cs` — `OrderService_ReadsConfig_FromConfigStore`: verify config values applied correctly
  - Each test class uses `IAsyncLifetime` for container lifecycle; no state leaks between runs
  - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 14.8_

- [x] 18. Final checkpoint — full solution build and all tests pass
  - Ensure the solution builds with `dotnet build SmartOrder.sln`
  - Ensure all unit and property-based tests pass
  - Ensure all integration tests pass (requires Docker)
  - Ensure all tests pass, ask the user if questions arise.


## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP build
- Tasks 1–2 (scaffolding + components) must be complete before any service implementation
- Tasks 3–7 (OrderService) must be complete before integration tests that depend on it
- Tasks 9–11 (Inventory, Payment, Notification) can be implemented in parallel after Task 1–2
- Task 13–14 (WorkflowOrchestrator) depends on all four other services being complete
- Task 16 (Docker Compose) can be written any time after Task 1 but requires all services to build before running
- Task 17 (Integration tests) requires Docker and all services to be running via Docker Compose
- All async methods must include `CancellationToken cancellationToken` and use `ConfigureAwait(false)` in service/infrastructure code
- All Dapr calls must use `IDaprClient` (not `DaprClient` directly) to enable NSubstitute mocking in unit tests
- Property-based tests use FsCheck.Xunit with `MaxTest = 100` and must include the comment: `// Feature: smart-order, Property {N}: {property_text}`
