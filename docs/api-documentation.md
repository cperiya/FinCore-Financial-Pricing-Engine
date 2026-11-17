# FinCore REST API

The API uses ASP.NET Core controllers and built-in OpenAPI. Portfolio endpoints
require `ConnectionStrings:FinCore` to be configured; the original stock
valuation and root discovery endpoints remain available without a database.
All portfolio data is USD-denominated and synthetic. No live market data or
currency conversion is performed.

## Endpoint summary

| Method and route | Success | Errors |
| --- | --- | --- |
| `POST /api/portfolios` | `201 Created`, `Location: /api/portfolios/{id}` | `400` invalid name/currency |
| `GET /api/portfolios` | `200 OK`, array of portfolios | — |
| `GET /api/portfolios/{id}` | `200 OK`, portfolio | `400` malformed GUID, `404` missing portfolio |
| `GET /api/portfolios/{id}/positions` | `200 OK`, array of positive positions (possibly empty) | `400` malformed GUID, `404` missing portfolio |
| `GET /api/portfolios/{id}/valuation?asOfUtc={utc}` | `200 OK`, complete USD valuation | `400` invalid cutoff, `404` missing portfolio, `409` missing eligible prices, `422` decimal overflow, `503` known transient SQL failure |
| `POST /api/instruments` | `200 OK`, registered equity | `400` invalid request, `409` duplicate normalized symbol |
| `POST /api/portfolios/{id}/trades` | `200 OK`, stored or replayed trade | `400` invalid request, `404` missing parent, `409` oversell/idempotency conflict, `503` transient transaction failure |
| `POST /api/instruments/{id}/prices` | `200 OK`, stored market price | `400` invalid request, `404` missing instrument |
| `GET /api/instruments/{id}/prices/latest?asOfUtc={utc}` | `200 OK`, latest eligible price | `400` malformed GUID or invalid/missing cutoff, `404` missing instrument or eligible price |
| `GET /api/pricing/stock` | `200 OK`, stock valuation | `400` invalid input or decimal overflow |
| `GET /` | `200 OK`, discovery document | — |

GUID route parameters intentionally have no `:guid` route constraint. A
malformed route GUID is handled by `[ApiController]` model binding and returned
as `400 ValidationProblemDetails`, not a route-level `404`.

## Portfolio requests

### Create a portfolio

`POST /api/portfolios` accepts a non-blank name up to 200 characters and a
required `baseCurrency` of USD (case-insensitive; normalized to `USD`).

```http
POST /api/portfolios
Content-Type: application/json

{
  "name": "Synthetic Growth Portfolio",
  "baseCurrency": "USD"
}
```

Response: `201 Created`, with a `Location` header that resolves to
`GET /api/portfolios/{id}`:

```json
{
  "id": "a23cf908-0812-4c5f-9f31-cf5f76d69e1f",
  "name": "Synthetic Growth Portfolio",
  "baseCurrency": "USD",
  "createdAt": "2026-11-16T20:08:45.645Z"
}
```

### List and retrieve portfolios

`GET /api/portfolios` returns an array ordered by creation time descending,
then ID ascending. `GET /api/portfolios/{id}` returns one portfolio or
`404 ProblemDetails`.

```http
GET /api/portfolios
Accept: application/json

GET /api/portfolios/a23cf908-0812-4c5f-9f31-cf5f76d69e1f
Accept: application/json
```

Both successful responses use the portfolio representation shown above; the
list response is a JSON array containing those objects.

### Retrieve derived positions

`GET /api/portfolios/{id}/positions` returns positions calculated by the
Application service from the complete trade history. Positions are not stored
in SQL. A valid portfolio with no open holdings returns `200 OK` and `[]`.

```json
[
  {
    "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
    "quantity": 10
  }
]
```

### Value a portfolio

`GET /api/portfolios/{id}/valuation` returns a complete USD market valuation.
An optional `asOfUtc` query value must use the zero UTC offset. When it is
omitted, the Application service captures `TimeProvider.GetUtcNow()` once and
uses that cutoff for both trades and market prices.

Trades are included only when `ExecutedAt <= asOfUtc`; Domain ordering remains
execution time, BUY before SELL, then `Trade.Id`. For each positive position,
the API selects the eligible price with the greatest `AsOfUtc` not later than
the cutoff, breaking equal-time ties by greatest `MarketPrice.Id`. Values use
decimal arithmetic without rounding and reuse the Domain position and stock
valuation services. Zero positions are omitted. An empty portfolio returns an
empty `positions` array and `totalMarketValue: 0`.

The endpoint does not enforce a stale-price threshold because no business
freshness limit is defined. The selected price timestamp is always included.
Prices later than the cutoff are excluded.

```http
GET /api/portfolios/a23cf908-0812-4c5f-9f31-cf5f76d69e1f/valuation?asOfUtc=2026-11-16T20%3A30%3A00Z
Accept: application/json
```

Successful response (`200 OK`):

```json
{
  "portfolioId": "a23cf908-0812-4c5f-9f31-cf5f76d69e1f",
  "currency": "USD",
  "asOfUtc": "2026-11-16T20:30:00Z",
  "positions": [
    {
      "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
      "symbol": "MSFT",
      "quantity": 80,
      "marketPrice": 450,
      "priceAsOfUtc": "2026-11-16T20:00:00Z",
      "marketValue": 36000
    }
  ],
  "totalMarketValue": 36000
}
```

If one or more positions lack an eligible price, the entire valuation returns
`409 Conflict`; it never returns a partial total:

```json
{
  "title": "Market prices unavailable",
  "status": 409,
  "detail": "One or more open positions have no eligible market price; no partial valuation was returned.",
  "missingInstruments": [
    {
      "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
      "symbol": "MSFT"
    }
  ]
}
```

## Instrument and price requests

### Register a USD equity

`POST /api/instruments` requires a symbol, a non-blank name of at most 200
characters, and `currency: "USD"`. The endpoint is equity-only; no
client-selectable instrument type is accepted. Symbols are trimmed and
uppercased by Domain/Application rules before persistence. Duplicate
normalized symbols return `409 ProblemDetails`.

```http
POST /api/instruments
Content-Type: application/json

{
  "symbol": "msft",
  "name": "Synthetic Example Equity",
  "currency": "USD"
}
```

Response: `200 OK`:

```json
{
  "id": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
  "symbol": "MSFT",
  "name": "Synthetic Example Equity",
  "currency": "USD",
  "instrumentType": "Equity"
}
```

### Record a simulated market price

`POST /api/instruments/{id}/prices` requires a positive `price` in the
persistable `decimal(28,10)` range, `currency: "USD"`, and an `asOfUtc`
timestamp with a zero UTC offset.

```http
POST /api/instruments/bc74902a-ff1e-462e-9fdd-317204fe33d9/prices
Content-Type: application/json

{
  "price": 25.75,
  "currency": "USD",
  "asOfUtc": "2026-11-16T20:00:00Z"
}
```

Response: `200 OK`:

```json
{
  "id": "78cfcc50-4033-45bb-ae11-c8be80a517a4",
  "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
  "price": 25.75,
  "currency": "USD",
  "asOfUtc": "2026-11-16T20:00:00Z"
}
```

### Retrieve the latest eligible price

`GET /api/instruments/{id}/prices/latest` requires `asOfUtc` as an explicit
UTC-offset-zero query value. It returns the latest recorded price whose
timestamp is no later than the cutoff. A missing instrument or no eligible
price returns `404 ProblemDetails`.

```http
GET /api/instruments/bc74902a-ff1e-462e-9fdd-317204fe33d9/prices/latest?asOfUtc=2026-11-16T20%3A30%3A00Z
```

Response: `200 OK`, using the market-price response shape shown for price
recording, with the selected eligible record.

## Trade requests, idempotency, and retry behavior

`POST /api/portfolios/{id}/trades` requires a non-empty, caller-generated
`tradeId` GUID. That stable ID is the idempotency key and persisted trade
primary key. `instrumentId`, `side`, positive `quantity`, positive USD
`unitPrice`, and UTC-offset-zero `executedAt` are required. The side is a
string enum, `"Buy"` or `"Sell"`; numeric enum values are rejected.

Financial values must fit SQL Server `decimal(28,10)` without rounding:
positive, no greater than `999999999999999999.9999999999`, and unchanged by
rounding to ten decimal places.

```http
POST /api/portfolios/a23cf908-0812-4c5f-9f31-cf5f76d69e1f/trades
Content-Type: application/json

{
  "tradeId": "33333333-3333-4333-8333-333333333333",
  "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
  "side": "Buy",
  "quantity": 10,
  "unitPrice": 25.50,
  "executedAt": "2026-11-16T20:00:00Z"
}
```

Response: `200 OK`:

```json
{
  "id": "33333333-3333-4333-8333-333333333333",
  "portfolioId": "a23cf908-0812-4c5f-9f31-cf5f76d69e1f",
  "instrumentId": "bc74902a-ff1e-462e-9fdd-317204fe33d9",
  "side": "Buy",
  "quantity": 10,
  "unitPrice": 25.50,
  "executedAt": "2026-11-16T20:00:00Z"
}
```

The service validates the full chronological trade history, including
backdated trades, using execution time, BUY before SELL, then `Trade.Id`.
Overselling and reuse of a trade ID with a different canonical payload return
`409 ProblemDetails`. An identical retry returns the original trade with
`200 OK` and does not insert a second row. Both new trades and exact replays
use `200 OK` because this API surface has no trade GET-by-ID route and the
existing Application result does not distinguish insert from replay.

Trade writes retain the Application/Infrastructure SQL Server `SERIALIZABLE`
transaction and transaction-owned `sp_getapplock` protocol. Lock/command
timeouts, lock acquisition failures, SQL deadlocks, and recognized SQL TCP
connection timeouts return `503 ProblemDetails` with `Retry-After: 2`; they
are not conflicts. For a trade retry after a transient or ambiguous result,
the client must reuse the same `tradeId` and identical payload. The database
primary key and Application idempotency check prevent duplicate insertion.
The API does not automatically retry database operations.

## Validation and error responses

`[ApiController]` converts malformed JSON, malformed GUIDs, and DTO validation
errors into `400 ValidationProblemDetails`. Currency and timestamp validation
is explicit; the API does not convert currency or timestamps. Expected
business outcomes use Problem Details:

| Status | Meaning |
| --- | --- |
| `400` | Missing/invalid fields, unsupported enum or currency, nonpositive/out-of-range value, non-UTC timestamp, malformed GUID |
| `404` | Requested portfolio/instrument does not exist, or the individual latest-price endpoint has no eligible price |
| `409` | Duplicate normalized symbol, insufficient holdings, conflicting trade ID, or any missing eligible valuation prices |
| `422` | A checked position market value or portfolio total exceeds the supported `decimal` range |
| `503` | Known SQL application-lock/command timeout, lock acquisition failure, deadlock, or recognized SQL TCP wait timeout; response detail is endpoint-neutral |
| `500` | Unexpected server failure with a generic client detail; internal exception and database text are not returned |

For example, an unsupported currency returns `400 ValidationProblemDetails`
with a field error:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "BaseCurrency": ["Only USD portfolios are supported."]
  }
}
```

A conflicting trade identifier returns a safe `409 ProblemDetails` response:

```json
{
  "title": "Trade idempotency conflict",
  "status": 409,
  "detail": "The trade identifier was used with a different request.",
  "instance": "/api/portfolios/a23cf908-0812-4c5f-9f31-cf5f76d69e1f/trades"
}
```

## Existing stock valuation endpoint

### `GET /api/pricing/stock`

Calculates `MarketValue = UnitPrice * Quantity` for a required symbol. Query
parameters are `symbol` (non-blank), `unitPrice` (> 0), and `quantity` (> 0).
Fractional quantities are supported. Symbol normalization is uppercase and
has no instrument-registration length limit; registered instruments remain
limited to 20 characters.
Currency remains caller-known; this endpoint performs no currency validation,
FX conversion, or currency-specific rounding.

```http
GET /api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4
```

Response: `200 OK`:

```json
{
  "symbol": "MSFT",
  "unitPrice": 12.50,
  "quantity": 4,
  "marketValue": 50.00
}
```

Missing/malformed inputs, nonpositive values, and decimal overflow return
`400` with validation details, preserving the original Milestone 1 behavior.

## Root discovery endpoint

`GET /` returns application information and a stock valuation URI template.
The `openApi` link appears only in Development, where `/openapi/v1.json` is
mapped.
