# Stock valuation API

## `GET /`

Returns JSON discovery information for the application:

```json
{
  "applicationName": "FinCore",
  "description": "Financial pricing API for stock position market valuation.",
  "links": {
    "stockValuation": "/api/pricing/stock?symbol={symbol}&unitPrice={unitPrice}&quantity={quantity}",
    "openApi": "/openapi/v1.json"
  }
}
```

The `stockValuation` link is a URI template; replace `{unitPrice}` and
`{quantity}` with decimal values. The `openApi` link is included only when
running in Development because the OpenAPI document is not mapped in other
environments.

The API uses controller-based routing, so this action is implemented as a
controller action with an absolute `/` route. `MapGet` is an ASP.NET Core
minimal-API method: it maps a route pattern and handler delegate directly in
the application startup code. A Web API does not automatically have a
homepage because ASP.NET Core maps only the routes configured by the
application; it does not generate a website or default root document. This
project already uses controllers, so the root stays in that style rather than
mixing in a minimal-API endpoint.

## `GET /api/pricing/stock`

Calculates a stock position's market value for a required stock symbol:

```text
MarketValue = UnitPrice * Quantity
```

### Query parameters

| Name | Type | Required | Validation |
| --- | --- | --- | --- |
| `symbol` | string | Yes | Must contain a non-whitespace character. Valid symbols are trimmed and normalized to uppercase. |
| `unitPrice` | decimal | Yes | Must be greater than zero. |
| `quantity` | decimal | Yes | Must be greater than zero. Fractional shares are accepted. |

Missing or blank symbols, missing or malformed numeric inputs, and numeric
inputs that are zero or negative return `400 Bad Request` with validation
details. A calculation that exceeds the representable `decimal` range also
returns `400 Bad Request`.

### Currency and precision

The API does not accept or return a currency code. `unitPrice` and
`marketValue` are assumed to use the same caller-known currency. The caller is
responsible for ensuring the inputs are denominated in the intended
currency. The API performs no FX conversion, currency validation, or
currency-specific rounding.

`decimal` is used for both inputs and the result. Fractional quantities are
supported. No rounding step is applied; the returned value is the decimal
product when representable.

### Successful request

```http
GET /api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4
Accept: application/json
```

Response: `200 OK`

```json
{
  "symbol": "MSFT",
  "unitPrice": 12.50,
  "quantity": 4,
  "marketValue": 50.00
}
```

The response symbol is trimmed and uppercased. For example, input `msft`
produces `"symbol": "MSFT"`.

### Invalid requests

Blank or missing symbols, zero or negative numeric values, missing query
parameters, and malformed decimal inputs are rejected with `400 Bad Request`.
Responses use ASP.NET Core validation problem details. Examples:

```http
GET /api/pricing/stock?unitPrice=12.50&quantity=4
GET /api/pricing/stock?symbol=%20%20&unitPrice=12.50&quantity=4
GET /api/pricing/stock?symbol=MSFT&unitPrice=-12.50&quantity=4
GET /api/pricing/stock?symbol=MSFT&unitPrice=12.50&quantity=0
```

An input pair whose product overflows `decimal` also returns a validation
problem indicating that `marketValue` is outside the supported range.

### OpenAPI

In the Development environment, the generated OpenAPI document is served at
`/openapi/v1.json`. The API uses ASP.NET Core's built-in OpenAPI support.
No Swagger UI is configured.
