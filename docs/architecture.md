# Architecture and request flow

## Current implementation

FinCore is a modular monolith: one API host with separate projects for HTTP,
application orchestration, domain rules, and future integrations. The
implemented behavior is a single stock-position calculation:

```text
MarketValue = UnitPrice * Quantity
```

The calculation uses `decimal`, accepts fractional quantities, requires a
non-empty symbol and positive numeric inputs, and does not apply rounding or
currency conversion. The price and result are assumed to share the same
caller-known currency.

## Projects and dependency direction

| Project | Current responsibility | Project references |
| --- | --- | --- |
| `FinCore.Api` | HTTP controller, request validation, response mapping, dependency-injection composition, and Development OpenAPI document | Application, Infrastructure |
| `FinCore.Application` | `IStockPricingService`, its implementation, and use-case result | Domain |
| `FinCore.Domain` | `StockValuationService`, which validates and calculates market value | None |
| `FinCore.Infrastructure` | Reserved for persistence and integrations; no adapter is implemented | Application, Domain |
| `FinCore.UnitTests` | xUnit domain tests and in-memory API integration tests | API, Application, Domain |

```text
FinCore.Api ───────────────> FinCore.Application ──> FinCore.Domain
     │                              ↑
     └──> FinCore.Infrastructure ───┘
                      └───────────────────────────> FinCore.Domain

FinCore.UnitTests ────────> FinCore.Application
       └─────────────────> FinCore.Domain
       └─────────────────> FinCore.Api (root endpoint integration test)
```

Arrows are compile-time project references. They do not imply that
Infrastructure is currently called at runtime.

### Design rationale

- **Keep the Domain dependency-free.** The valuation invariant and arithmetic
  do not need ASP.NET, a database, or an external service. That keeps the rule
  directly testable and protects it from transport or storage changes.
- **Put orchestration in Application.** `StockPricingService` is the use case
  boundary. It calls the domain rule and returns an application result rather
  than making the API calculate the value itself.
- **Keep HTTP concerns in API.** The controller binds query parameters,
  returns HTTP status codes and DTOs, and maps overflow to a validation
  response. The domain does not know about HTTP.
- **Use an application interface.** `IStockPricingService` allows the API to
  depend on the use-case contract and leaves the application service
  replaceable in tests and future hosts.
- **Do not add an interface for the simple domain calculation.** The
  stateless domain service has no integration or alternate implementation
  seam at this point; keeping it concrete avoids an abstraction without a
  present use.
- **Use `decimal` for money-like values.** Decimal arithmetic avoids binary
  floating-point representation artifacts for these inputs. The API does not
  impose currency-specific scale or rounding; it returns the exact
  representable decimal product, or reports overflow.
- **Keep one deployable application.** Project separation makes the code
  navigable and testable without introducing microservice deployment or
  network boundaries.

The API references Infrastructure for future composition, but no database,
cache, or integration is registered. Infrastructure is not involved in this
feature.

## Stock valuation request flow

1. A client sends `GET /api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4`.
2. ASP.NET Core routes the request to `PricingController.GetStock`.
3. `[ApiController]` binds the query into `StockValuationRequest`. Required
   fields and its `IValidatableObject` implementation reject missing or blank
   symbols and numeric values that are missing, malformed, zero, or negative
   with `400 Bad Request` validation details.
4. The controller calls `IStockPricingService.ValueStock` with the validated
   symbol and numeric inputs.
5. `StockPricingService` calls
   `StockValuationService.CalculateMarketValue`. It trims and uppercases the
   symbol, while the domain method independently enforces positive numeric
   inputs and calculates `unitPrice * quantity`.
6. The application service packages the normalized symbol, inputs, and
   calculated value in `StockValuationResult`.
7. The controller maps the application result to `StockValuationResponse` and
   returns `200 OK`. If decimal multiplication overflows, it returns a
   validation-problem response with status `400`.

Request and response models are API types; the application result is a separate
application type. Neither HTTP types nor currency conversion logic leak into
Domain.

## Root discovery endpoint

`HomeController.Get` handles `GET /` and returns the application name, a short
description, and relative links. It always includes the stock valuation URI
template, including the required `symbol` query parameter. It includes
`/openapi/v1.json` only in Development, matching
`Program.cs`, where `MapOpenApi` is registered only for that environment.
`ApiHomeEndpointTests` uses `WebApplicationFactory<Program>` to send actual
in-memory HTTP requests in Development and Production and assert which links
are present.

### What `MapGet` does

`MapGet(pattern, handler)` is an ASP.NET Core minimal-API method. It directly
maps an HTTP GET route pattern to a delegate, for example a lambda that
returns a JSON value. It is not used for this root route: `Program.cs` already
registers controllers and calls `MapControllers`, and the pricing endpoint is
also a controller. Keeping the root in `HomeController` maintains one endpoint
style rather than mixing controller and minimal-API routing.

A Web API does not automatically have a homepage. ASP.NET Core serves only
routes the application maps (plus any separately configured static files or
default pages); a root route is not generated just because the server is
running. Before `GET /` was added, no mapped route matched the root, so
ASP.NET Core correctly returned `404 Not Found`.

### Currency and precision assumptions

No currency code is accepted or returned. The caller must interpret `unitPrice`
and `marketValue` in one shared currency. The service does not check the
currency, select a default currency, convert currencies, or round to a
currency's minor unit. Decimal multiplication is exact for products
representable by `decimal`; an out-of-range product raises `OverflowException`
in Domain and is translated by the API to a client validation response.

## Dependency injection and API documentation

`Program.cs` registers `StockValuationService` as a singleton because it is
stateless, and `IStockPricingService` as a scoped application service. The API
uses ASP.NET Core's built-in OpenAPI support and maps `/openapi/v1.json` only
in the Development environment.

The API project, Application project, Domain project, and unit-test project
generate XML documentation files during build. Public types, members, and
methods added for these features include XML comments; see the
[class and method reference](class-method-reference.md).

## Important files

| File | Purpose |
| --- | --- |
| [`src/FinCore.Api/Program.cs`](../src/FinCore.Api/Program.cs) | Registers controllers, OpenAPI, and application/domain services; configures middleware, routes, and host startup. |
| [`src/FinCore.Api/Controllers/PricingController.cs`](../src/FinCore.Api/Controllers/PricingController.cs) | Defines `GET /api/pricing/stock`, invokes the application service, and maps the result to HTTP. |
| [`src/FinCore.Api/Controllers/HomeController.cs`](../src/FinCore.Api/Controllers/HomeController.cs) | Defines the root discovery endpoint and exposes environment-appropriate links. |
| [`src/FinCore.Api/Models/ApiHomeResponse.cs`](../src/FinCore.Api/Models/ApiHomeResponse.cs) | Defines the JSON response for the root discovery endpoint. |
| [`src/FinCore.Api/Models/StockValuationRequest.cs`](../src/FinCore.Api/Models/StockValuationRequest.cs) | Defines required symbol and positive numeric query inputs, with model validation. |
| [`src/FinCore.Api/Models/StockValuationResponse.cs`](../src/FinCore.Api/Models/StockValuationResponse.cs) | Defines the JSON response contract. |
| [`src/FinCore.Application/Pricing/IStockPricingService.cs`](../src/FinCore.Application/Pricing/IStockPricingService.cs) | Application use-case contract used by the API. |
| [`src/FinCore.Application/Pricing/StockPricingService.cs`](../src/FinCore.Application/Pricing/StockPricingService.cs) | Calls the Domain calculation and creates the application result. |
| [`src/FinCore.Application/Pricing/StockValuationResult.cs`](../src/FinCore.Application/Pricing/StockValuationResult.cs) | Carries normalized symbol, unit price, quantity, and calculated market value across the application boundary. |
| [`src/FinCore.Domain/Valuation/StockValuationService.cs`](../src/FinCore.Domain/Valuation/StockValuationService.cs) | Enforces positive numeric inputs and performs decimal multiplication. |
| [`tests/FinCore.UnitTests/StockValuationServiceTests.cs`](../tests/FinCore.UnitTests/StockValuationServiceTests.cs) | Covers valid inputs, zero/negative rejection, precision, and overflow. |
| [`tests/FinCore.UnitTests/ApiHomeEndpointTests.cs`](../tests/FinCore.UnitTests/ApiHomeEndpointTests.cs) | Verifies the root route and available links through in-memory HTTP requests. |
| [`src/FinCore.Api/FinCore.Api.http`](../src/FinCore.Api/FinCore.Api.http) | Provides sample successful and invalid endpoint requests. |
| [`docs/api-documentation.md`](./api-documentation.md) | Documents the public HTTP contract and currency assumptions. |
| [`docs/class-method-reference.md`](./class-method-reference.md) | Explains public FinCore types, members, parameters, and returns. |

## Validation commands

Run from the repository root in PowerShell:

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

The tests cover the domain arithmetic and its validation behavior. They do not
exercise the HTTP server or OpenAPI document.

## Not implemented

Portfolio aggregation, quote retrieval, currency identification or
conversion, currency-specific rounding, persistence, SQL Server, Entity
Framework Core, Redis, Docker, Azure, and CI/CD are not part of this feature.
Financial examples must use synthetic data.
