# Learning journal

## Milestone 1 – Solution and architecture scaffold

**Implemented:** created and registered the .NET 10 solution structure with an
ASP.NET Core API host, Application, Domain, and Infrastructure projects, plus
an xUnit test project. Added references to establish the intended clean
dependency direction, a .NET `.gitignore`, and baseline project documentation.
The API host registers controller support and configures its HTTP middleware
pipeline, but no controllers or routes are present. Expanded the README and
architecture guide to explain the current project responsibilities, startup
flow, generated files, and design rationale without describing future
functionality as implemented.

**Engineering decisions:** use a modular monolith to keep deployment simple
while separating HTTP, use cases, domain rules, and integrations. Keep Domain
independent; make the API the future composition root. Application and
Infrastructure point inward to Domain; tests reference Application and Domain
without coupling to Infrastructure. Remove the template WeatherForecast
controller and model so they are not mistaken for financial functionality.
Keep the generated `.http` scratch file unchanged and explicitly document
that its WeatherForecast request is stale.

**Not implemented:** financial pricing or portfolio-risk logic, database
access, caching, Docker, Azure resources, or CI/CD. No real financial data is
used. The xUnit project currently has no test cases.

**What to learn next:** trace the `Program.cs` host setup from builder
creation, through service registration and middleware/endpoint configuration,
to `Run`. Understand how project references differ from runtime calls, why
business rules should not depend on ASP.NET or persistence, and how the
solution and project files drive restore/build/test. When behavior is
introduced, add a focused test that exercises the behavior rather than
mistaking successful compilation for a business-rule test.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

Restore and build validate package resolution and compilation. Test invokes
the configured xUnit runner; currently it reports no available tests because
no test cases have been implemented.

## Milestone 2 – Stock position market valuation

**Implemented:** added `StockValuationService` in Domain to calculate
`MarketValue = UnitPrice * Quantity` with `decimal`. Added non-negative input
validation while allowing zero values and fractional share quantities. Added
the Application pricing use case and result, and exposed them through
`GET /api/pricing/stock`. The API returns a validation problem for missing,
malformed, or negative query values and for a product outside the `decimal`
range. Added Development-only built-in OpenAPI output, XML documentation
generation, endpoint examples, and domain unit tests.

**Currency assumption:** the caller knows the currency. Unit price and market
value share that currency; the request/response have no currency code and do
not convert or round to a currency-specific scale.

**Engineering decisions:** keep validation and multiplication in the
dependency-free Domain service so callers cannot bypass the business rule.
Use an Application interface as the API's dependency, and keep HTTP request
and response models separate from the Application result. Use built-in
ASP.NET Core OpenAPI without adding Swagger UI or an external OpenAPI tool.

**What to learn:** follow the request from query-string model binding and
`[ApiController]` validation, through dependency injection and the
Application use case, to the Domain calculation and back through response
mapping. Compare decimal multiplication with binary floating-point, inspect
how overflow is surfaced as a client error, and review how XML comments and
endpoint metadata appear in the generated Development OpenAPI document.

**Not implemented:** currency identification or FX, currency-specific
rounding, market data, portfolio aggregation, persistence, SQL Server, Entity
Framework Core, Redis, Docker, Azure, and CI/CD. Unit tests cover the domain
calculation; they do not exercise HTTP routing or OpenAPI generation.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Milestone 3 – Root API discovery endpoint

**Implemented:** added `GET /`, which returns the FinCore application name,
description, a templated link to stock valuation, and a link to the OpenAPI
document only in Development. Added an in-memory HTTP integration test for
Development and Production behavior, exposed the top-level `Program` type to
the test host, documented the endpoint, and added a root request to the
`.http` examples.

**Engineering decisions:** the API already uses attribute-routed controllers
and maps them with `MapControllers`. The root is another controller action
rather than a `MapGet` minimal API route, avoiding a second endpoint style.
The OpenAPI link follows the same Development-only rule as
`app.MapOpenApi()`, so the response does not advertise an endpoint unavailable
in Production.

**What to learn:** `MapGet` maps a GET route pattern directly to a handler
delegate in the minimal-API style; it is not the same as `MapControllers`,
which maps attribute-routed controller actions. ASP.NET Core does not create a
homepage automatically: applications serve only configured routes, and
there is no root route unless one is mapped. Study
`WebApplicationFactory<Program>` as a way to verify HTTP behavior without
starting a real network listener.

**Not implemented:** a browser-based homepage or UI. The root response is a
JSON API discovery document; stock valuation and Development OpenAPI remain
the only advertised resources.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Milestone 4 – Required stock symbol and positive valuation inputs

**Implemented:** the stock valuation contract now requires a non-blank
`symbol`, `unitPrice > 0`, and `quantity > 0`. Missing/invalid values return
HTTP 400. The Application use case trims and uppercases valid symbols, and
the successful response includes that normalized symbol. Domain validation
now also rejects zero numeric values, so the positive-input rule is preserved
when the domain service is called outside HTTP. Updated the root discovery
link, endpoint examples, XML comments, and API/class-reference documentation.

**Engineering decisions:** keep the symbol as application/API data because the
valuation formula does not use it, while normalize it at the application
boundary so all callers receive consistent results. Enforce positive numeric
inputs in Domain as well as validating the HTTP request, preventing alternate
callers from bypassing the financial rule. Test the HTTP contract using
`WebApplicationFactory<Program>` rather than only testing controller methods.

**What to learn:** compare HTTP model validation with domain invariants:
request-model validation provides useful 400 responses, while domain
validation keeps core rules reliable for every caller. Review why a symbol is
trimmed and uppercased using invariant casing, and trace the symbol from query
binding through the application result to the response DTO.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```
