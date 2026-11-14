# FinCore – Financial Pricing & Portfolio Risk Engine

FinCore is a .NET 10 modular-monolith starter for a financial pricing and
portfolio risk engine. The first implemented feature values one stock
position. It does **not** load live market data, identify a currency, convert
between currencies, or persist positions.

## Implemented: stock position valuation

The API calculates:

```text
MarketValue = UnitPrice * Quantity
```

Both inputs use `decimal`. Unit price and quantity must be greater than zero;
negative and zero values are rejected. Fractional share quantities are
supported. A non-empty symbol is required and normalized to uppercase. If the
product exceeds the range of `decimal`, the API returns a validation error.

Currency is caller-known: `UnitPrice` and `MarketValue` are assumed to use the
same currency. The request and response do not carry a currency code, and no
foreign-exchange conversion is performed. Callers must not combine values
denominated in different currencies.

### Try the endpoint

Start the API from the repository root:

```powershell
dotnet run --project .\src\FinCore.Api\FinCore.Api.csproj
```

The HTTP launch profile listens at `http://localhost:5157`. Example request:

```http
GET http://localhost:5157/api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4
```

Successful response (`200 OK`):

```json
{
  "symbol": "MSFT",
  "unitPrice": 12.50,
  "quantity": 4,
  "marketValue": 50.00
}
```

Missing or blank symbols, missing or malformed numeric values, and zero or
negative numeric values produce `400 Bad Request` with validation details.
The OpenAPI document is available in Development at
`http://localhost:5157/openapi/v1.json`.

The root URL `http://localhost:5157/` returns a JSON discovery document with
the application name, description, and a templated stock valuation link. The
OpenAPI link is included only in Development, where the OpenAPI endpoint is
mapped.

The root URL returns JSON application information and links. The stock
valuation request flow is: HTTP query binding and validation in
`FinCore.Api` → symbol normalization and use case in
`FinCore.Application` → arithmetic and positive-value invariants in
`FinCore.Domain` → application result → HTTP response. See the
[API documentation](docs/api-documentation.md) and
[class and method reference](docs/class-method-reference.md) for the full
contract and source walkthrough.

## Projects and dependency direction

| Project | Responsibility |
| --- | --- |
| `FinCore.Api` | HTTP endpoints, request validation, response mapping, and application composition. |
| `FinCore.Application` | `IStockPricingService` use case and its result model. |
| `FinCore.Domain` | Stock market-value calculation and positive-value rules. |
| `FinCore.Infrastructure` | Reserved for future persistence and integrations; no adapters yet. |
| `FinCore.UnitTests` | xUnit domain tests and in-memory API integration tests. |

Dependencies point inward: API → Application → Domain; API → Infrastructure
for future adapter composition; Infrastructure → Application and Domain.
UnitTests references API for its HTTP integration test and Application and
Domain for valuation tests; it does not reference Infrastructure.

## Build and test

Run from the repository root in PowerShell:

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Documentation

- [Architecture guide](docs/architecture.md) — project boundaries, design
  rationale, and end-to-end request flow.
- [API documentation](docs/api-documentation.md) — endpoint contract,
  validation, response, and currency assumptions.
- [Class and method reference](docs/class-method-reference.md) — public
  FinCore types and methods, including parameters and return values.
- [Learning journal](docs/learning-journal.md) — completed milestones,
  learning topics, and validation commands.
- [.github/copilot-instructions.md](.github/copilot-instructions.md) —
  repository-specific contribution expectations.

## Not implemented yet

Portfolio aggregation, market-data integration, persistence, SQL Server,
Entity Framework Core, Redis, Docker, Azure, and CI/CD remain out of scope.
Any future financial examples or fixtures must use synthetic data.
