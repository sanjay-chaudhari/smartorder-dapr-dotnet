---
inclusion: always
---

# .NET 10 Coding Standards

## Target Framework
- All projects target `net10.0`
- Use ASP.NET Core Minimal APIs exclusively — no MVC controllers, no `[ApiController]`
- Enable nullable reference types: `<Nullable>enable</Nullable>`
- Enable implicit usings: `<ImplicitUsings>enable</ImplicitUsings>`

## DTOs and Models
- Use C# record types for all request and response models
- Example: `public record CreateOrderRequest(string ProductId, int Quantity, decimal Price);`
- Validate with `DataAnnotations` for simple rules; use `FluentValidation` for complex rules
- Never use mutable classes for DTOs

## Dependency Injection
- Register all services in `Program.cs` using `IServiceCollection` extension methods
- One extension method per feature area: `services.AddOrderServices()`, `services.AddDaprClient()`
- Use constructor injection exclusively — no service locator pattern, no `IServiceProvider` in business logic

## Async and Cancellation
- All I/O operations must be `async` and return `Task` or `Task<T>`
- Every async method signature must include a `CancellationToken cancellationToken` parameter
- Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on `Task` objects
- Use `ConfigureAwait(false)` in all library and infrastructure code

## Logging
- Inject `ILogger<T>` via constructor — never use static loggers
- Use structured logging with named properties: `{OrderId}`, `{ServiceName}`, `{OperationName}`, `{TraceId}`
- Log levels:
  - `Debug` — Dapr SDK internals, low-level details
  - `Information` — business events (order created, payment processed)
  - `Warning` — retries, degraded state, non-critical failures
  - `Error` — exceptions, failed operations requiring attention

## Error Handling
- Catch `DaprException` on all Dapr API calls and log with full context
- Return `Results.Problem()` from Minimal API endpoints on unhandled errors
- Never swallow exceptions silently

## Project Structure Per Service

Each service follows this folder layout:

    src/{ServiceName}/
    ├── Program.cs               # DI registration, middleware, endpoint mapping
    ├── Endpoints/               # Minimal API endpoint definitions
    ├── Services/                # Business logic classes
    ├── Models/                  # Record types for requests, responses, domain models
    ├── Components/              # Dapr activity and workflow classes (if applicable)
    └── {ServiceName}.csproj

## Testing Standards
- Framework: xUnit with FluentAssertions
- Mock `IDaprClient` using NSubstitute in all unit tests
- Integration tests use Testcontainers to spin up Redis and Dapr sidecar
- Test class naming: `{ClassName}Tests`
- Test method naming: `{MethodName}_When{Condition}_Should{ExpectedResult}`
- Code coverage target: 80% for all business logic classes in `/Services/`

## Health Checks
- Every service exposes a `/health` endpoint
- Register with `builder.Services.AddHealthChecks()` and `app.MapHealthChecks("/health")`
- Docker Compose `healthcheck` directive points to `/health` for every service
