# Class and method reference

This reference covers the public FinCore types added for stock valuation and
root API discovery.
Framework-provided ASP.NET Core methods are described in the
[architecture guide](architecture.md); they are not FinCore business methods.

## Domain

### `StockValuationService`

File: [`src/FinCore.Domain/Valuation/StockValuationService.cs`](../src/FinCore.Domain/Valuation/StockValuationService.cs)

Stateless domain service for validating and calculating a stock position's
market value. It has no project dependencies.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `StockValuationService()` | None | A new service instance | Initializes the stateless service. |
| `CalculateMarketValue(decimal unitPrice, decimal quantity)` | Positive unit price and share quantity, both `decimal` | `decimal` market value | Rejects values less than or equal to zero with `ArgumentOutOfRangeException`; returns `unitPrice * quantity`; may throw `OverflowException` if the product exceeds decimal range. Fractional quantities are allowed. |

The result uses the same caller-known currency as `unitPrice`; the service has
no currency code, FX logic, or rounding rule.

## Application

### `IStockPricingService`

File: [`src/FinCore.Application/Pricing/IStockPricingService.cs`](../src/FinCore.Application/Pricing/IStockPricingService.cs)

Defines the stock pricing use case consumed by the API.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `ValueStock(string symbol, decimal unitPrice, decimal quantity)` | Non-empty symbol, positive unit price, and positive quantity | `StockValuationResult` | Trims and uppercases the symbol, delegates valuation to Domain, and returns the symbol, inputs, and calculated result. Throws `ArgumentException` for a blank symbol; propagates `ArgumentOutOfRangeException` for non-positive numbers and `OverflowException` for an unrepresentable result. |

### `StockPricingService`

File: [`src/FinCore.Application/Pricing/StockPricingService.cs`](../src/FinCore.Application/Pricing/StockPricingService.cs)

Implements the application use case using the domain valuation service.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `StockPricingService(StockValuationService stockValuationService)` | Required domain service | A new application service | Throws `ArgumentNullException` if the dependency is null. |
| `ValueStock(string symbol, decimal unitPrice, decimal quantity)` | Symbol, positive unit price, and positive quantity | `StockValuationResult` | Trims and uppercases the symbol, calls `CalculateMarketValue`, and packages the normalized symbol, inputs, and result. Throws `ArgumentException` for a blank symbol; propagates the domain validation and overflow exceptions. |

### `StockValuationResult`

File: [`src/FinCore.Application/Pricing/StockValuationResult.cs`](../src/FinCore.Application/Pricing/StockValuationResult.cs)

Immutable result returned by the application use case.

| Member | Purpose |
| --- | --- |
| `StockValuationResult(string symbol, decimal unitPrice, decimal quantity, decimal marketValue)` | Initializes the normalized symbol and values from the use-case calculation. |
| `Symbol` | Normalized uppercase stock symbol. |
| `UnitPrice` | Input price of one share. |
| `Quantity` | Input share quantity. |
| `MarketValue` | Calculated product in the same currency as `UnitPrice`. |

## API

### `ApiHomeResponse`

File: [`src/FinCore.Api/Models/ApiHomeResponse.cs`](../src/FinCore.Api/Models/ApiHomeResponse.cs)

Immutable response model used by `GET /`.

| Member | Purpose |
| --- | --- |
| `ApiHomeResponse(string applicationName, string description, IReadOnlyDictionary<string, string> links)` | Initializes the application name, description, and environment-appropriate relative resource links. |
| `ApplicationName` | The application name (`FinCore`). |
| `Description` | A short description of the API's implemented purpose. |
| `Links` | Relative links keyed by resource name; the OpenAPI entry is present only in Development. |

### `HomeController`

File: [`src/FinCore.Api/Controllers/HomeController.cs`](../src/FinCore.Api/Controllers/HomeController.cs)

Controller that serves API discovery information at `/`.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `HomeController(IWebHostEnvironment environment)` | Current ASP.NET Core hosting environment | A new controller | Throws `ArgumentNullException` if the environment is null. |
| `Get()` | None | `ActionResult<ApiHomeResponse>` | Returns `200 OK` with application metadata, the stock valuation URI template, and an OpenAPI link only when the environment is Development. |

### `StockValuationRequest`

File: [`src/FinCore.Api/Models/StockValuationRequest.cs`](../src/FinCore.Api/Models/StockValuationRequest.cs)

Query-bound request record. `[BindRequired]` ensures both nullable query
values are supplied; ASP.NET Core model binding rejects malformed decimals.

| Member | Purpose |
| --- | --- |
| `Symbol` | Required nullable string query value; missing, empty, or whitespace-only values are invalid. Valid input is trimmed and uppercased by the Application service. |
| `UnitPrice` | Required nullable `decimal` query value; must be greater than zero. |
| `Quantity` | Required nullable `decimal` query value; must be greater than zero. |
| `Validate(ValidationContext validationContext)` | Returns validation errors for blank symbols and non-positive numeric values. ASP.NET Core's `[ApiController]` behavior converts invalid model state into `400 Bad Request`. |

### `StockValuationResponse`

File: [`src/FinCore.Api/Models/StockValuationResponse.cs`](../src/FinCore.Api/Models/StockValuationResponse.cs)

Immutable HTTP response model. All monetary values use the same caller-known
currency; there is no currency identifier in the payload.

| Member | Purpose |
| --- | --- |
| `StockValuationResponse(string symbol, decimal unitPrice, decimal quantity, decimal marketValue)` | Initializes the normalized symbol and response fields from the application result. |
| `Symbol` | Normalized uppercase stock symbol. |
| `UnitPrice` | Price of one share. |
| `Quantity` | Number of shares. |
| `MarketValue` | Product of unit price and quantity. |

### `PricingController`

File: [`src/FinCore.Api/Controllers/PricingController.cs`](../src/FinCore.Api/Controllers/PricingController.cs)

HTTP adapter for pricing operations.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `PricingController(IStockPricingService stockPricingService)` | Application use-case interface | A new controller | Throws `ArgumentNullException` if the dependency is null. |
| `GetStock(StockValuationRequest request)` | Query-bound symbol, unit price, and quantity | `ActionResult<StockValuationResponse>` | Returns `200 OK` with the normalized symbol and valuation. Model validation handles missing/blank symbol, missing/malformed/non-positive inputs as `400`; decimal overflow is mapped to a validation problem `400`. |

## Tests

### `StockPricingEndpointTests`

File: [`tests/FinCore.UnitTests/StockPricingEndpointTests.cs`](../tests/FinCore.UnitTests/StockPricingEndpointTests.cs)

In-memory HTTP integration tests for the stock valuation endpoint. Each test
returns `Task`, accepts no parameters, and verifies the API response.

| Method | Behavior checked |
| --- | --- |
| `GetStock_WithoutSymbol_ReturnsBadRequest()` | Missing symbol is rejected with HTTP 400. |
| `GetStock_WithBlankSymbol_ReturnsBadRequest()` | Whitespace-only symbol is rejected with HTTP 400. |
| `GetStock_WithNegativePrice_ReturnsBadRequest()` | Negative unit price is rejected with HTTP 400. |
| `GetStock_WithZeroQuantity_ReturnsBadRequest()` | Zero quantity is rejected with HTTP 400. |
| `GetStock_WithValidInput_ReturnsNormalizedSymbolAndMarketValue()` | Valid input returns uppercase symbol and the calculated market value. |

### `ApiHomeEndpointTests`

File: [`tests/FinCore.UnitTests/ApiHomeEndpointTests.cs`](../tests/FinCore.UnitTests/ApiHomeEndpointTests.cs)

In-memory HTTP integration tests for the root route. Each test returns
`Task`, accepts no parameters, and asserts the response status and JSON links.

| Method | Behavior checked |
| --- | --- |
| `GetRoot_InDevelopment_ReturnsApplicationAndAvailableLinks()` | The root returns the application name, description, stock link, and Development OpenAPI link. |
| `GetRoot_InProduction_OmitsUnavailableOpenApiLink()` | The root remains available but does not advertise an unmapped OpenAPI document in Production. |

### `StockValuationServiceTests`

File: [`tests/FinCore.UnitTests/StockValuationServiceTests.cs`](../tests/FinCore.UnitTests/StockValuationServiceTests.cs)

The public xUnit test methods return `void`, take no parameters, and use
assertions to exercise one valuation behavior each.

| Method | Behavior checked |
| --- | --- |
| `CalculateMarketValue_WithValidInputs_ReturnsProduct()` | Positive price and quantity produce their product. |
| `CalculateMarketValue_WithZeroQuantity_ThrowsArgumentOutOfRangeException()` | Zero quantity is rejected and identifies `quantity`. |
| `CalculateMarketValue_WithZeroUnitPrice_ThrowsArgumentOutOfRangeException()` | Zero unit price is rejected and identifies `unitPrice`. |
| `CalculateMarketValue_WithNegativeUnitPrice_ThrowsArgumentOutOfRangeException()` | Negative unit price is rejected and identifies `unitPrice`. |
| `CalculateMarketValue_WithNegativeQuantity_ThrowsArgumentOutOfRangeException()` | Negative quantity is rejected and identifies `quantity`. |
| `CalculateMarketValue_WithFractionalInputs_PreservesDecimalPrecision()` | Decimal multiplication preserves the expected fractional result. |
| `CalculateMarketValue_WhenProductExceedsDecimalRange_ThrowsOverflowException()` | An unrepresentable product reports overflow. |
