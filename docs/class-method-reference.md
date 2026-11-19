# Class and method reference

This reference covers public FinCore types for portfolio domain rules,
Application contracts, EF Core persistence, stock valuation, and API discovery.
It also summarizes the internal benchmark harness and diagnostic helpers.
Framework-provided ASP.NET Core methods are described in the
[architecture guide](architecture.md); they are not FinCore business methods.

## Domain

### `Portfolio`

File: [`src/FinCore.Domain/Portfolios/Portfolio.cs`](../src/FinCore.Domain/Portfolios/Portfolio.cs)

Represents a USD-denominated portfolio. Construction rejects an empty ID,
blank name, unsupported currency, or non-UTC creation timestamp. The name is
trimmed, limited to 200 characters, and the currency is normalized to
uppercase.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `Portfolio(Guid id, string name, string baseCurrency, DateTimeOffset createdAt)` | Non-empty ID, non-blank name up to 200 characters, USD currency, UTC timestamp | A new portfolio | Validates the USD-only and UTC invariants; trims the name and normalizes currency. Throws `ArgumentOutOfRangeException` when the name exceeds 200 characters. |
| `MaximumNameLength` | None | `int` constant (`200`) | Maximum accepted portfolio name length, matching the database column. |
| `Id` | None | `Guid` | Portfolio identifier. |
| `Name` | None | `string` | Trimmed portfolio name. |
| `BaseCurrency` | None | `string` | Normalized `USD`. |
| `CreatedAt` | None | `DateTimeOffset` | UTC creation timestamp. |

### `Instrument`

File: [`src/FinCore.Domain/Portfolios/Instrument.cs`](../src/FinCore.Domain/Portfolios/Instrument.cs)

Represents an equity instrument denominated in USD. Symbols are trimmed and
uppercased invariantly, limited to 20 characters, and unique in the current
equity database model. Names are trimmed and limited to 200 characters.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `Instrument(Guid id, string symbol, string name, string currency, InstrumentType instrumentType)` | Non-empty ID, non-blank symbol up to 20 characters and name up to 200 characters, USD currency, Equity type | A new instrument | Normalizes symbol, name, and currency; rejects unsupported currencies, instrument types, and excessive lengths. |
| `NormalizeSymbol(string symbol)` | Non-blank symbol up to 20 characters after normalization | `string` | Trims and uppercases invariantly; throws `ArgumentException` for blank input and `ArgumentOutOfRangeException` for excessive length. |
| `MaximumSymbolLength`, `MaximumNameLength` | None | `int` constants (`20`, `200`) | Supported symbol and name lengths, matching SQL column sizes. |
| `Id`, `Symbol`, `Name`, `Currency`, `InstrumentType` | None | Corresponding property type | Expose the validated instrument identity and data. |

### `InstrumentType` and `TradeSide`

Files: [`InstrumentType.cs`](../src/FinCore.Domain/Portfolios/InstrumentType.cs) and
[`TradeSide.cs`](../src/FinCore.Domain/Portfolios/TradeSide.cs).

`InstrumentType.Equity` is the only supported instrument category.
`TradeSide.Buy` adds shares and `TradeSide.Sell` subtracts shares.

### `StockSymbolNormalizer`

File: [`src/FinCore.Domain/Portfolios/StockSymbolNormalizer.cs`](../src/FinCore.Domain/Portfolios/StockSymbolNormalizer.cs)

`Normalize(string symbol)` trims and uppercases a non-blank symbol using invariant casing, without imposing the 20-character SQL storage limit for registered instruments. It throws `ArgumentException` for a blank symbol. `Instrument.NormalizeSymbol` reuses this helper and then enforces the registered-instrument length limit; the Milestone 1 stock-pricing use case uses the helper without that limit.

### `Trade`

File: [`src/FinCore.Domain/Portfolios/Trade.cs`](../src/FinCore.Domain/Portfolios/Trade.cs)

Represents a buy or sell at a positive USD unit price and positive quantity.
Construction requires non-empty trade, portfolio, and instrument IDs and a
UTC execution timestamp. Holdings sufficiency is checked by
`PositionCalculationService`, not by an individual trade. SQL storage
precision is checked by `FinCoreDbContext` before persistence.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `Trade(Guid id, Guid portfolioId, Guid instrumentId, TradeSide side, decimal quantity, decimal unitPrice, DateTimeOffset executedAt)` | IDs, side, positive quantity and price, UTC time | A new trade | Rejects empty IDs, undefined side, non-positive values, and non-UTC time. |
| `Id`, `PortfolioId`, `InstrumentId`, `Side`, `Quantity`, `UnitPrice`, `ExecutedAt` | None | Corresponding property type | Expose the validated trade data. |

### `MarketPrice`

File: [`src/FinCore.Domain/Portfolios/MarketPrice.cs`](../src/FinCore.Domain/Portfolios/MarketPrice.cs)

Represents a positive simulated USD price for an instrument at a UTC
timestamp. SQL storage precision is checked by `FinCoreDbContext` before
persistence.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `MarketPrice(Guid id, Guid instrumentId, decimal price, string currency, DateTimeOffset asOfUtc)` | Non-empty IDs, positive price, USD currency, UTC time | A new market price | Validates price, currency, identifiers, and UTC timestamp. |
| `Id`, `InstrumentId`, `Price`, `Currency`, `AsOfUtc` | None | Corresponding property type | Expose the validated market-price data. |

### `Position`, `InsufficientPositionException`, and `PositionCalculationService`

Files: [`Position.cs`](../src/FinCore.Domain/Portfolios/Position.cs),
[`InsufficientPositionException.cs`](../src/FinCore.Domain/Portfolios/InsufficientPositionException.cs), and
[`PositionCalculationService.cs`](../src/FinCore.Domain/Portfolios/PositionCalculationService.cs).

`Position` is a positive net holding for one portfolio/instrument pair. Zero
holdings are omitted. `InsufficientPositionException` identifies the sell
that would take a holding below zero and provides available and requested
quantities.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `PositionCalculationService()` | None | A new service | Initializes a stateless position calculator. |
| `CalculatePositions(IEnumerable<Trade> trades)` | Trade sequence | `IReadOnlyList<Position>` | Groups by portfolio/instrument and calculates buys minus sells ordered by execution time, side (buy before sell), then trade ID. Omits zero holdings and throws `InsufficientPositionException` if a chronological position becomes negative. |
| `Position.PortfolioId`, `Position.InstrumentId`, `Position.Quantity` | None | `Guid`, `Guid`, `decimal` | Identify a positive derived holding. |
| `InsufficientPositionException.PortfolioId`, `.InstrumentId`, `.TradeId`, `.AvailableQuantity`, `.RequestedQuantity` | None | Corresponding property type | Identify the portfolio, instrument, rejected trade, and quantities involved. |

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

## Application portfolio persistence contracts

These contracts use only Domain types and .NET async primitives. They do not
reference EF Core; Infrastructure provides their implementations.

### `IPortfolioRepository`

File: [`src/FinCore.Application/Portfolios/IPortfolioRepository.cs`](../src/FinCore.Application/Portfolios/IPortfolioRepository.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `AddAsync(Portfolio portfolio, CancellationToken cancellationToken)` | A validated portfolio and cancellation token | `Task` | Tracks a new portfolio in the current unit of work; it does not commit it. Throws `ArgumentNullException` for a null portfolio. |
| `GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken)` | Portfolio ID and cancellation token | `Task<Portfolio?>` | Returns the portfolio or `null` when it does not exist. |
| `GetAllAsync(CancellationToken cancellationToken)` | Cancellation token | `Task<IReadOnlyList<Portfolio>>` | Returns portfolios ordered by `CreatedAt` descending and `Id` ascending. |

### `IInstrumentRepository`

File: [`src/FinCore.Application/Portfolios/IInstrumentRepository.cs`](../src/FinCore.Application/Portfolios/IInstrumentRepository.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `AddAsync(Instrument instrument, CancellationToken cancellationToken)` | A validated instrument and cancellation token | `Task` | Tracks a new instrument without committing; null input raises `ArgumentNullException`. |
| `GetByIdAsync(Guid instrumentId, CancellationToken cancellationToken)` | Instrument ID and cancellation token | `Task<Instrument?>` | Returns the instrument or `null` when absent. |
| `GetByIdsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)` | Instrument IDs and cancellation token | `Task<IReadOnlyList<Instrument>>` | Returns matching instruments using bounded batches; absent IDs are omitted. |
| `GetBySymbolAsync(string symbol, CancellationToken cancellationToken)` | Symbol and cancellation token | `Task<Instrument?>` | Normalizes the symbol and returns the instrument or `null`. |

### `ITradeRepository`

File: [`src/FinCore.Application/Portfolios/ITradeRepository.cs`](../src/FinCore.Application/Portfolios/ITradeRepository.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `AddAsync(Trade trade, CancellationToken cancellationToken)` | A validated trade and cancellation token | `Task` | Tracks a new trade without committing; null input raises `ArgumentNullException`. |
| `GetByIdAsync(Guid tradeId, CancellationToken cancellationToken)` | Stable trade ID/idempotency key and cancellation token | `Task<Trade?>` | Returns the trade with that primary key or `null`. |
| `GetByPortfolioIdAsync(Guid portfolioId, CancellationToken cancellationToken)` | Portfolio ID and cancellation token | `Task<IReadOnlyList<Trade>>` | Returns trades ordered by execution time, side, and trade ID. |
| `GetByPortfolioIdAtOrBeforeAsync(Guid portfolioId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)` | Portfolio ID, inclusive UTC cutoff, and cancellation token | `Task<IReadOnlyList<Trade>>` | Returns only `ExecutedAt <= asOfUtc` trades in deterministic Domain order. |
| `GetByPortfolioAndInstrumentAsync(Guid portfolioId, Guid instrumentId, CancellationToken cancellationToken)` | Portfolio and instrument IDs plus cancellation token | `Task<IReadOnlyList<Trade>>` | Returns that position's trades in deterministic position-calculation order. |

### `IMarketPriceRepository`

File: [`src/FinCore.Application/Portfolios/IMarketPriceRepository.cs`](../src/FinCore.Application/Portfolios/IMarketPriceRepository.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `AddAsync(MarketPrice marketPrice, CancellationToken cancellationToken)` | A validated market price and cancellation token | `Task` | Tracks a market price without committing; null input raises `ArgumentNullException`. |
| `GetLatestByInstrumentIdAsync(Guid instrumentId, CancellationToken cancellationToken)` | Instrument ID and cancellation token | `Task<MarketPrice?>` | Returns the latest `AsOfUtc` price, breaking equal-time ties by descending ID; returns `null` when no price exists. |
| `GetLatestAtOrBeforeAsync(Guid instrumentId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)` | Instrument ID, UTC cutoff, and cancellation token | `Task<MarketPrice?>` | Returns the latest eligible price where `AsOfUtc <= cutoff`, breaking ties by descending ID; returns `null` when none exists. |
| `GetLatestAtOrBeforeAsync(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset asOfUtc, CancellationToken cancellationToken)` | Instrument IDs, UTC cutoff, and cancellation token | `Task<IReadOnlyList<MarketPrice>>` | Returns one latest eligible price per priced instrument using bounded batches; missing prices are omitted. |

### `IUnitOfWork`

File: [`src/FinCore.Application/Persistence/IUnitOfWork.cs`](../src/FinCore.Application/Persistence/IUnitOfWork.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `SaveChangesAsync(CancellationToken cancellationToken)` | Cancellation token | `Task<int>` | Commits all currently tracked changes and returns the number of state entries written. |
| `ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)` | An application operation and cancellation token | `Task<TResult>` | Runs the operation in a serializable transaction, saves tracked changes, and commits; failures trigger rollback and are surfaced. Recognized SQL TCP wait timeouts (error 258, severity 20+) raise `TransientPersistenceException`, including failures while starting the transaction. |
| `ExecuteReadOnlyInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)` | Read-only operation and cancellation token | `Task<TResult>` | Runs the operation in a serializable transaction and commits without calling `SaveChangesAsync`; recognized connectivity timeouts are translated consistently at transaction startup and during reads. |
| `ExecuteWithExclusiveLocksAsync<TResult>(IReadOnlyCollection<string> lockResources, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)` | Logical SQL lock resources, an operation, and cancellation token | `Task<TResult>` | Starts a serializable transaction, acquires distinct application locks in ordinal order, runs the operation, saves, and commits. Lock and SQL command timeouts raise `TimeoutException` unless canceled; deadlocks and recognized TCP wait timeouts are surfaced as retryable failures. |

### `IPortfolioValuationDiagnostics` and `IPortfolioValuationDiagnosticStage`

File: [`IPortfolioValuationDiagnostics.cs`](../src/FinCore.Application/Portfolios/IPortfolioValuationDiagnostics.cs)

The optional Application-layer sink provides stage instrumentation without an
EF Core dependency. Normal API composition does not register a sink; the
valuation service uses a no-op stage when none is supplied.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `BeginStage(string stageName, int inputCount)` | Stage name and logical input count | `IPortfolioValuationDiagnosticStage` | Begins an optional measurement scope. |
| `Complete(int outputCount)` | Logical output count | `void` | Completes the scope with its output count. |
| `Dispose()` | None | `void` | Disposes the scope through `IDisposable`. The benchmark collector completes an unfinished scope with zero outputs. |

## Infrastructure

### `PortfolioCollectionParameterOptions`

File: [`PortfolioCollectionParameterOptions.cs`](../src/FinCore.Infrastructure/Persistence/PortfolioCollectionParameterOptions.cs)

`UseJsonParameterForIds` is a `bool` property that defaults to `false`.
Setting it to `true` through DI options selects query-local EF Core JSON
collection parameters for the two batched valuation repository queries. The
normal API registration does not bind or enable this option from configuration.

### `FinCoreDbContext`

File: [`src/FinCore.Infrastructure/Persistence/FinCoreDbContext.cs`](../src/FinCore.Infrastructure/Persistence/FinCoreDbContext.cs)

EF Core context for portfolio persistence. Its constructor accepts
`DbContextOptions<FinCoreDbContext>`. The `Portfolios`, `Instruments`,
`Trades`, and `MarketPrices` properties expose the corresponding entity sets.
`OnModelCreating(ModelBuilder)` applies the four explicit entity
configurations used by runtime queries and migrations. The context does not
persist derived `Position` instances. Its `SaveChanges` and
`SaveChangesAsync` overloads check added or modified trade and market-price
decimals against `decimal(28,10)` before writing; an out-of-range or
over-scale value raises `InvalidOperationException` rather than being rounded
by SQL Server.

### Entity configurations

Files: [`src/FinCore.Infrastructure/Persistence/Configurations/`](../src/FinCore.Infrastructure/Persistence/Configurations/)

| Configuration | Important mapping behavior |
| --- | --- |
| `PortfolioConfiguration` | Maps the primary key, required `nvarchar(200)` name, `char(3)` USD currency, `datetimeoffset(7)` creation time, USD check, and zero-offset UTC check. |
| `InstrumentConfiguration` | Maps the primary key, `nvarchar(20)` symbol, `nvarchar(200)` name, `char(3)` currency, and integer instrument type; checks USD and the supported Equity value; creates unique `UX_Instruments_Symbol`. |
| `TradeConfiguration` | Maps enum side, positive `decimal(28,10)` quantity and price, UTC execution time, required portfolio/instrument FKs with restricted deletes, side/value checks, and `IX_Trades_Portfolio_Instrument_Execution` on `(PortfolioId ASC, InstrumentId ASC, ExecutedAt ASC, Side ASC, Id ASC)`. |
| `MarketPriceConfiguration` | Maps positive `decimal(28,10)` price, `char(3)` currency, UTC observation time, required instrument FK with restricted deletes, and `IX_MarketPrices_Instrument_Latest` on `(InstrumentId ASC, AsOfUtc DESC, Id DESC)`. |

`FinCoreDbContext` rejects trade and market-price values that exceed the SQL
decimal range or scale before SQL Server can round them. Domain validation
limits portfolio and instrument names to 200 characters and symbols to 20
characters; `Instrument` trims and invariant-uppercases symbols before
persistence. The unique `UX_Instruments_Symbol` index enforces one record per
normalized symbol.

### SQL repositories

Files: [`src/FinCore.Infrastructure/Persistence/Repositories/`](../src/FinCore.Infrastructure/Persistence/Repositories/)

`PortfolioRepository` and `InstrumentRepository` implement `AddAsync` and
`GetByIdAsync`; the portfolio repository also implements stable listing and
the instrument repository looks up normalized symbols and supports batched
ID retrieval. `TradeRepository` implements `AddAsync`, lookup by trade ID,
the complete and cutoff-aware portfolio queries, and position-history lookup,
ordering by `ExecutedAt`, `Side`, and `Id`.
`MarketPriceRepository` implements `AddAsync` and
`GetLatestByInstrumentIdAsync` and as-of eligible lookup, ordering by
`AsOfUtc DESC, Id DESC`.
Valuation queries also filter trades by an inclusive execution cutoff and
retrieve instrument and latest eligible price batches. ID lists are split
into batches of at most 2,000 to remain below SQL Server's parameter limit;
latest prices are grouped by instrument and ordered by observation time then
ID descending. Repositories receive the scoped `FinCoreDbContext`; add methods track changes
but do not call `SaveChangesAsync`, so a use case can commit related changes
as one unit.

The internal
[`InstrumentRepository`](../src/FinCore.Infrastructure/Persistence/Repositories/InstrumentRepository.cs)
and
[`MarketPriceRepository`](../src/FinCore.Infrastructure/Persistence/Repositories/MarketPriceRepository.cs)
constructors accept the scoped context and
`IOptions<PortfolioCollectionParameterOptions>`. `GetByIdsAsync` and the
collection overload of `GetLatestAtOrBeforeAsync`, respectively, deduplicate
IDs and execute no-tracking batches sequentially. With the default option they
use `batch.Contains(...)`; the opt-in branch uses
`EF.Parameter(batch).Contains(...)`. The price query still requires a
zero-offset cutoff, includes `AsOfUtc <= cutoff`, and chooses the greatest
timestamp then greatest ID. The single-instrument methods do not use this
collection option.

### `FinCoreUnitOfWork`

File: [`src/FinCore.Infrastructure/Persistence/FinCoreUnitOfWork.cs`](../src/FinCore.Infrastructure/Persistence/FinCoreUnitOfWork.cs)

Implements `IUnitOfWork` using the same scoped context as its repositories.
`SaveChangesAsync` commits tracked changes. `ExecuteInTransactionAsync`
starts a SQL Server serializable transaction, runs the callback, saves, and
commits. `ExecuteReadOnlyInTransactionAsync` runs sequential reads under the
same isolation level but commits without saving changes.
`ExecuteWithExclusiveLocksAsync` also acquires transaction-owned
exclusive `sp_getapplock` locks on the same connection before running the
callback. It validates resource names, removes duplicates, and sorts them
ordinally; lock timeout becomes `TimeoutException`, negative lock results
become `TransactionLockAcquisitionException`, and SQL deadlock error 1205
becomes `TransactionDeadlockException`. Cancellation is passed to commands
and persistence, with rollback attempted using a non-cancelable token.
SQL command timeouts become `TimeoutException` unless the request token was
canceled, in which case cancellation is preserved.
SQL TCP wait timeout error 258 is translated to
`TransientPersistenceException` only for severity 20 or higher, both while
opening the transaction and during transaction work. Authentication,
configuration, and SQL syntax failures are not classified as transient.
Transaction disposal provides a final rollback safeguard; rollback failures
are reported together with the original error.

The trade use case supplies `FinCore:Trade:{tradeId:N}` and
`FinCore:Position:{portfolioId:N}:{instrumentId:N}`. Both the application
locks and `SERIALIZABLE` isolation protect the full trade-history read and
insert. All repositories and the unit of work use the same scoped context.

### `InfrastructureServiceCollectionExtensions`

File: [`src/FinCore.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`](../src/FinCore.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs)

`AddFinCoreInfrastructure(IServiceCollection services, string connectionString)`
registers `FinCoreDbContext`, all four repositories, and `IUnitOfWork` as
scoped services. It returns the supplied service collection. It throws
`ArgumentNullException` for a null service collection and `ArgumentException`
for a blank connection string. The API obtains the optional connection
setting from `ConnectionStrings:FinCore`/`ConnectionStrings__FinCore`; no
credential is stored in source or `appsettings.json`.

### `FinCoreDbContextFactory`

File: [`src/FinCore.Infrastructure/Persistence/FinCoreDbContextFactory.cs`](../src/FinCore.Infrastructure/Persistence/FinCoreDbContextFactory.cs)

`CreateDbContext(string[] args)` is the EF Core design-time entry point. It
configures the SQL Server provider from `ConnectionStrings__FinCore` and
returns a `FinCoreDbContext`. It throws `InvalidOperationException` if the
environment variable is missing or blank; the value is not logged.

### `InitialPortfolioPersistence`

File: [`src/FinCore.Infrastructure/Persistence/Migrations/`](../src/FinCore.Infrastructure/Persistence/Migrations/)

The `20261115200739_InitialPortfolioPersistence` migration creates the four
tables, checks, relationships, and indexes described in the
[database design](database-design.md). It was applied and verified on a local
development database; a fresh installation must apply it explicitly using the
[README instructions](../README.md#initialize-the-local-development-database).
The `FinCoreIntegrationTests` database was not targeted.

| Method | Behavior |
| --- | --- |
| `Up(MigrationBuilder migrationBuilder)` | Creates the portfolio, instrument, trade, and market-price tables with their constraints, foreign keys, and indexes. |
| `Down(MigrationBuilder migrationBuilder)` | Drops the four application tables in dependency-safe order. It is the reverse migration operation and has not been run against the local database. |

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
| `ValueStock(string symbol, decimal unitPrice, decimal quantity)` | Non-blank symbol of any length, positive unit price, and positive quantity | `StockValuationResult` | Trims and uppercases the symbol without the instrument-storage length limit, calls `CalculateMarketValue`, and packages the normalized symbol, inputs, and result. Throws `ArgumentException` for a blank symbol; propagates the domain validation and overflow exceptions. |

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

### Portfolio and trading Application services

These services use Domain entities and Application repository interfaces.
They do not reference EF Core. The API registers them with Infrastructure
when the persistence connection is configured. Portfolio HTTP controllers
are described in the API section below.

#### `IPortfolioApplicationService` and `PortfolioApplicationService`

Files: [`IPortfolioApplicationService.cs`](../src/FinCore.Application/Portfolios/IPortfolioApplicationService.cs) and
[`PortfolioApplicationService.cs`](../src/FinCore.Application/Portfolios/PortfolioApplicationService.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `PortfolioApplicationService(IPortfolioRepository, ITradeRepository, IUnitOfWork, PositionCalculationService, TimeProvider)` | Required persistence, calculation, and clock dependencies | A new service | Throws `ArgumentNullException` when a dependency is null. |
| `CreatePortfolioAsync(string name, CancellationToken cancellationToken)` | Portfolio name and cancellation token | `Task<Portfolio>` | Generates an ID, assigns USD and the injected `TimeProvider` UTC time, adds, and saves the portfolio. |
| `GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken)` | Portfolio ID and token | `Task<Portfolio?>` | Returns the portfolio or `null` when absent. |
| `ListPortfoliosAsync(CancellationToken cancellationToken)` | Cancellation token | `Task<IReadOnlyList<Portfolio>>` | Returns stable repository ordering: creation time descending, then ID ascending. |
| `GetPositionsAsync(Guid portfolioId, CancellationToken cancellationToken)` | Portfolio ID and token | `Task<IReadOnlyList<Position>>` | Requires an existing portfolio, loads all its trades, and derives positions with the Domain calculator; throws `PortfolioNotFoundException` when absent. |

#### `IInstrumentApplicationService` and `InstrumentApplicationService`

Files: [`IInstrumentApplicationService.cs`](../src/FinCore.Application/Portfolios/IInstrumentApplicationService.cs) and
[`InstrumentApplicationService.cs`](../src/FinCore.Application/Portfolios/InstrumentApplicationService.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `InstrumentApplicationService(IInstrumentRepository, IUnitOfWork)` | Instrument repository and transaction boundary | A new service | Throws `ArgumentNullException` when a dependency is null. |
| `RegisterEquityAsync(string symbol, string name, CancellationToken cancellationToken)` | Equity symbol, name, and token | `Task<Instrument>` | Normalizes the symbol, takes its transaction-owned symbol lock, checks for an existing row, and adds a USD Equity instrument. A duplicate raises `InstrumentSymbolAlreadyRegisteredException`; the existing unique index remains the final database safeguard. |

#### `ITradeApplicationService` and `TradeApplicationService`

Files: [`ITradeApplicationService.cs`](../src/FinCore.Application/Portfolios/ITradeApplicationService.cs) and
[`TradeApplicationService.cs`](../src/FinCore.Application/Portfolios/TradeApplicationService.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `TradeApplicationService(IPortfolioRepository, IInstrumentRepository, ITradeRepository, IUnitOfWork, PositionCalculationService)` | Parent and trade repositories, transaction boundary, and Domain calculator | A new service | Throws `ArgumentNullException` when a dependency is null. |
| `RecordTradeAsync(Guid tradeId, Guid portfolioId, Guid instrumentId, TradeSide side, decimal quantity, decimal unitPrice, DateTimeOffset executedAt, CancellationToken cancellationToken)` | Caller-supplied stable trade ID/idempotency key, parent IDs, side, positive decimal values, UTC execution time, and token | `Task<Trade>` | Takes trade-ID and position locks in the serializable unit of work; returns an exact stored replay or rejects a conflicting ID; verifies parents; validates the full affected history in Domain ordering; adds only if no chronological oversell occurs. |
| `HasSameCanonicalPayload(Trade stored, Trade requested)` | Stored and candidate Domain trades | `bool` (private) | Compares identifiers, side, decimal values, and UTC timestamp; decimal scale differences compare equal. |

Backdated trades are accepted only if recalculating the entire history with
the candidate remains long-only. Equal timestamps use `ExecutedAt`, BUY
before SELL, then `Trade.Id`. The primary key on `Trades.Id` enforces
idempotency uniqueness; no additional migration is required.

#### `IMarketPriceApplicationService` and `MarketPriceApplicationService`

Files: [`IMarketPriceApplicationService.cs`](../src/FinCore.Application/Portfolios/IMarketPriceApplicationService.cs) and
[`MarketPriceApplicationService.cs`](../src/FinCore.Application/Portfolios/MarketPriceApplicationService.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `MarketPriceApplicationService(IInstrumentRepository, IMarketPriceRepository, IUnitOfWork)` | Instrument and price repositories plus unit of work | A new service | Throws `ArgumentNullException` when a dependency is null. |
| `RecordPriceAsync(Guid instrumentId, decimal price, DateTimeOffset asOfUtc, CancellationToken cancellationToken)` | Instrument ID, positive USD price, UTC observation time, and token | `Task<MarketPrice>` | Requires an existing instrument, constructs a simulated USD price, and saves it. |
| `GetLatestEligiblePriceAsync(Guid instrumentId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)` | Instrument ID, UTC cutoff, and token | `Task<MarketPrice?>` | Requires an existing instrument and returns the greatest `AsOfUtc` not later than the cutoff, then descending ID; returns `null` when no price is eligible. |

#### `IPortfolioValuationApplicationService` and `PortfolioValuationApplicationService`

Files: [`IPortfolioValuationApplicationService.cs`](../src/FinCore.Application/Portfolios/IPortfolioValuationApplicationService.cs),
[`PortfolioValuationApplicationService.cs`](../src/FinCore.Application/Portfolios/PortfolioValuationApplicationService.cs), and
[`PortfolioValuation.cs`](../src/FinCore.Application/Portfolios/PortfolioValuation.cs)

The service accepts an optional `IPortfolioValuationDiagnostics` constructor
argument, defaulting to `null`. It captures one effective cutoff, runs sequential no-tracking reads
in a read-only serializable transaction, filters historical trades, derives
positions through `PositionCalculationService`, fetches instruments and
eligible prices in batches, then delegates position multiplication to
`StockValuationService`. It omits zero holdings, returns zero for an empty
portfolio, and does not round. Stale prices are permitted and their timestamps
are returned; future prices are excluded.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `GetPortfolioValuationAsync(Guid portfolioId, DateTimeOffset? asOfUtc, CancellationToken cancellationToken)` | Portfolio ID, optional zero-offset UTC cutoff, and token | `Task<PortfolioValuation>` | Uses one cutoff for trades and prices; throws `PortfolioNotFoundException`, `MissingMarketPricesException` for all missing held-instrument prices, or `PortfolioValuationOverflowException` on checked decimal overflow. |
| `PortfolioValuation` | Portfolio ID, USD currency, effective cutoff, valued positions, and total | Application result record | Carries the complete result; `Positions` is empty and total is zero when no holdings are open. |
| `PortfolioValuationPosition` | Instrument ID and symbol, quantity, selected price/timestamp, and value | Application result record | Carries one positive position's unrounded USD valuation. |
| `MissingMarketPriceInstrument` | Instrument ID and symbol | Value record | Identifies a held instrument without a price at the cutoff. |
| `MissingMarketPricesException` | Portfolio ID, cutoff, and missing-instrument list | Exception | Signals an incomplete valuation; the HTTP layer maps it to `409` with no partial total. |
| `PortfolioValuationOverflowException` | Portfolio ID, cutoff, and inner overflow | Exception | Signals an unrepresentable result; the HTTP layer maps it to `422`. |

#### Application transaction exceptions

Files:
[`PortfolioApplicationExceptions.cs`](../src/FinCore.Application/Portfolios/PortfolioApplicationExceptions.cs),
[`TransactionLockAcquisitionException.cs`](../src/FinCore.Application/Persistence/TransactionLockAcquisitionException.cs),
[`TransactionDeadlockException.cs`](../src/FinCore.Application/Persistence/TransactionDeadlockException.cs),
and [`TransientPersistenceException.cs`](../src/FinCore.Application/Persistence/TransientPersistenceException.cs).

`PortfolioNotFoundException` and `InstrumentNotFoundException` expose the
missing IDs. `InstrumentSymbolAlreadyRegisteredException` exposes the
normalized conflicting symbol. `TradeIdempotencyConflictException` exposes
the reused trade ID. `TransactionLockAcquisitionException` exposes the
resource name and negative SQL application-lock result code.
`TransactionDeadlockException` wraps SQL Server error 1205 after rollback;
callers may retry with the same trade ID. Lock timeouts use
`TimeoutException`, recognized SQL TCP connection timeouts use
`TransientPersistenceException`, and cancellation uses
`OperationCanceledException`.
Their public constructors take the missing ID, conflicting symbol, trade ID,
SQL resource/result code, or inner SQL deadlock exception as applicable.
`TransientPersistenceException(Exception innerException)` carries a
recognized transient provider failure without exposing it in HTTP responses.

### `SqlExceptionTranslator`

File: [`src/FinCore.Infrastructure/Persistence/SqlExceptionTranslator.cs`](../src/FinCore.Infrastructure/Persistence/SqlExceptionTranslator.cs)

The internal `Translate(int errorNumber, byte errorClass, Exception providerException, CancellationToken cancellationToken)` helper maps SQL deadlock 1205 to `TransactionDeadlockException`, command timeout -2 to `TimeoutException` (or cancellation when requested), and TCP wait timeout 258 to `TransientPersistenceException` only at severity 20 or higher. Other SQL errors, including authentication and syntax failures, are returned unchanged. `FinCoreUnitOfWork` applies this translation to nested provider exceptions during transaction startup and transaction work.
`MissingMarketPricesException` carries the portfolio ID, valuation cutoff,
and every missing instrument ID/symbol. `PortfolioValuationOverflowException`
wraps decimal overflow with the portfolio ID and cutoff; neither exposes
internal SQL details to the HTTP response.

## Benchmark harness

The separate benchmark project is not part of API composition. The following
helpers are internal unless identified as public; see the
[performance guide](performance-benchmarking.md) for executable commands,
measurements, and safety requirements.

| Component | Implemented behavior |
| --- | --- |
| [`Program`](../benchmarks/FinCore.Benchmarks/Program.cs) | Dispatches baseline, measurement, translation, feasibility, cleanup, and self-test modes. |
| [`PortfolioValuationBenchmarks`](../benchmarks/FinCore.Benchmarks/PortfolioValuationBenchmarks.cs) | Public BenchmarkDotNet class: `GlobalSetupAsync()` creates and seeds an owned database and validates a result; `ValuePortfolioAsync()` measures a valuation; `GlobalCleanupAsync()` disposes services and safely cleans the database. Failed setup also attempts cleanup. |
| [`PortfolioValuationTranslationBenchmarks`](../benchmarks/FinCore.Benchmarks/PortfolioValuationTranslationBenchmarks.cs) and [`PortfolioValuationTranslationValidationBenchmarks`](../benchmarks/FinCore.Benchmarks/PortfolioValuationTranslationValidationBenchmarks.cs) | Public BenchmarkDotNet classes with `InstrumentCount` and `UseJsonParameter` parameters. Their `GlobalSetupAsync()` verifies the parent-owned database and valuation; `ValuePortfolioAsync()` measures it; `GlobalCleanupAsync()` disposes the worker provider. |
| [`BenchmarkDatabaseFixture`](../benchmarks/FinCore.Benchmarks/BenchmarkDatabaseFixture.cs) | `InitializeAsync()` validates the local server, creates an exact generated database, records ownership, and applies the existing migration. `DisposeAsync()` and `CleanupOwnedDatabaseAsync(string databaseName)` refuse unsafe deletion; interrupted recovery requires the matching manifest and locks. |
| [`BenchmarkServiceProviderFactory`](../benchmarks/FinCore.Benchmarks/BenchmarkServiceProviderFactory.cs) | `Create(...)` composes a benchmark provider with optional diagnostics, interceptor, JSON mode, and price observer; `ValueAsync(...)` uses a fresh scope for one synthetic workload valuation. |
| [`ValuationDiagnosticsCollector`](../benchmarks/FinCore.Benchmarks/ValuationDiagnostics.cs) and [`SqlCommandDiagnosticsInterceptor`](../benchmarks/FinCore.Benchmarks/SqlCommandDiagnosticsInterceptor.cs) | The collector implements `BeginStage`, records stage duration and approximate process-wide allocation deltas, and provides `Reset()`. The interceptor records async reader counts and durations through reader availability, not materialization. |
| [`BaselineBenchmarkRunner`](../benchmarks/FinCore.Benchmarks/BaselineBenchmarkRunner.cs) and [`BenchmarkRunValidation`](../benchmarks/FinCore.Benchmarks/BenchmarkRunValidation.cs) | `Run(Type benchmarkType, string[] args)` validates the expected BenchmarkDotNet cases and returns a nonzero status on failure. `Validate(...)` and `ValidateBaseline(...)` check expected cases, launches, and required measurements. |
| [`EndToEndMeasurementRunner`](../benchmarks/FinCore.Benchmarks/EndToEndMeasurementRunner.cs), [`CollectionParameterExperimentRunner`](../benchmarks/FinCore.Benchmarks/CollectionParameterExperimentRunner.cs), and [`CollectionParameterValidationRunner`](../benchmarks/FinCore.Benchmarks/CollectionParameterValidationRunner.cs) | Execute sequential service measurements and controlled translation comparisons; the validation runner also checks richer workloads and captures plans. |
| [`CacheFeasibilityRunner`](../benchmarks/FinCore.Benchmarks/CacheFeasibilityRunner.cs), [`CacheFeasibilityWorkload`](../benchmarks/FinCore.Benchmarks/CacheFeasibilityWorkload.cs), and [feasibility helpers](../benchmarks/FinCore.Benchmarks/CacheFeasibilityBenchmarks.cs) | Run matched scalar/JSON pairs, validate actual price selections, and estimate exact-cutoff and interval reuse offline. The deterministic four-pair schedule counterbalances portfolio and mode order. The oracle is not a cache and does not measure Redis. |
| [`BenchmarkValidationSelfTest`](../benchmarks/FinCore.Benchmarks/BenchmarkValidationSelfTest.cs) and [feasibility self-tests](../benchmarks/FinCore.Benchmarks/CacheFeasibilityBenchmarks.cs) | Exercise benchmark failure detection, oracle behavior, and schedule balance; SQL-specific fixture failure tests require the local benchmark connection. |

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

### `PortfoliosController`

File: [`src/FinCore.Api/Controllers/PortfoliosController.cs`](../src/FinCore.Api/Controllers/PortfoliosController.cs)

Thin HTTP adapter for portfolio and trade Application use cases. It maps
domain/application values to API DTOs and leaves portfolio, position,
chronological trade, and concurrency rules in their existing layers.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `PortfoliosController(IPortfolioApplicationService, ITradeApplicationService, IPortfolioValuationApplicationService)` | Portfolio, trade, and valuation use-case interfaces | A new controller | Rejects null service dependencies. |
| `Create(CreatePortfolioRequest, CancellationToken)` | Portfolio name and USD base currency | `ActionResult<PortfolioResponse>` | Calls `CreatePortfolioAsync`; returns `201 Created` with a `Location` for `GetById`. |
| `List(CancellationToken)` | Cancellation token | `ActionResult<IReadOnlyList<PortfolioResponse>>` | Returns the Application service's stable portfolio ordering as `200 OK`. |
| `GetById(Guid id, CancellationToken)` | Portfolio identifier and token | `ActionResult<PortfolioResponse>` | Returns `200 OK` or `404 ProblemDetails`. |
| `GetPositions(Guid id, CancellationToken)` | Portfolio identifier and token | `ActionResult<IReadOnlyList<PositionResponse>>` | Calls the Domain-backed Application position use case; returns `200 OK` with an empty list for no holdings, or `404` for a missing portfolio. |
| `GetValuation(Guid id, DateTimeOffset? asOfUtc, CancellationToken)` | Portfolio ID, optional zero-offset UTC cutoff, and token | `ActionResult<PortfolioValuationResponse>` | Returns a complete valuation; invalid cutoff is `400`, missing portfolio `404`, missing prices `409`, overflow `422`, and known transient SQL errors `503`. |
| `RecordTrade(Guid portfolioId, RecordTradeRequest, CancellationToken)` | Portfolio ID, stable trade request, and token | `ActionResult<TradeResponse>` | Calls `RecordTradeAsync`; returns `200 OK` for new writes and exact replays, with expected conflicts mapped centrally. |

### `InstrumentsController`

File: [`src/FinCore.Api/Controllers/InstrumentsController.cs`](../src/FinCore.Api/Controllers/InstrumentsController.cs)

HTTP adapter for USD equity registration and simulated market prices.

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `InstrumentsController(IInstrumentApplicationService, IMarketPriceApplicationService)` | Instrument and price use-case interfaces | A new controller | Rejects null service dependencies. |
| `Register(RegisterInstrumentRequest, CancellationToken)` | Symbol, name, USD currency, and token | `ActionResult<InstrumentResponse>` | Calls the equity registration use case, which normalizes the symbol; returns `200 OK` or duplicate-symbol `409`. |
| `RecordPrice(Guid id, RecordMarketPriceRequest, CancellationToken)` | Instrument ID, positive USD price, UTC observation time, and token | `ActionResult<MarketPriceResponse>` | Calls the market-price use case; returns `200 OK` or `404` for a missing instrument. |
| `GetLatestPrice(Guid id, DateTimeOffset? asOfUtc, CancellationToken)` | Instrument ID, required UTC cutoff, and token | `ActionResult<MarketPriceResponse>` | Returns the latest eligible price or `404` if no eligible price exists; missing/non-UTC cutoff returns `400 ValidationProblemDetails`. |

### Portfolio and instrument API DTOs

Files: [`src/FinCore.Api/Models/Portfolios/`](../src/FinCore.Api/Models/Portfolios/) and
[`src/FinCore.Api/Models/Instruments/`](../src/FinCore.Api/Models/Instruments/)

All API DTOs are immutable sealed records and do not expose EF Core entities.

| DTO | Fields and behavior |
| --- | --- |
| `CreatePortfolioRequest` | `Name` and `BaseCurrency`; `Validate(ValidationContext)` requires a non-blank name within the Domain limit and USD currency. |
| `PortfolioResponse` | `Id`, normalized `Name`, `BaseCurrency`, and UTC `CreatedAt`. |
| `PositionResponse` | `InstrumentId` and positive trade-derived `Quantity`; zero holdings are omitted by the Domain calculation. |
| `PortfolioValuationResponse` | `PortfolioId`, USD `Currency`, effective `AsOfUtc`, position response list, and unrounded `TotalMarketValue`. |
| `PortfolioValuationPositionResponse` | Instrument ID, normalized symbol, positive quantity, selected USD price and timestamp, and unrounded market value. |
| `RecordTradeRequest` | `TradeId`, `InstrumentId`, `Side`, `Quantity`, `UnitPrice`, and `ExecutedAt`; `Validate(ValidationContext)` requires nonempty IDs, a defined trade side, positive SQL-representable financial values, and zero-offset UTC time. `TradeId` is the established idempotency key. |
| `TradeResponse` | The persisted trade ID, portfolio/instrument IDs, string-serialized `Side`, quantity, USD unit price, and UTC execution time. |
| `RegisterInstrumentRequest` | `Symbol`, `Name`, and `Currency`; `Validate(ValidationContext)` checks the normalized symbol limit, non-blank name, and USD-only currency. The route supports equities only. |
| `InstrumentResponse` | `Id`, normalized uppercase `Symbol`, `Name`, `Currency`, and string-serialized `InstrumentType`. |
| `RecordMarketPriceRequest` | `Price`, `Currency`, and `AsOfUtc`; `Validate(ValidationContext)` requires a positive `decimal(28,10)` value, USD, and zero-offset UTC time. |
| `MarketPriceResponse` | Price and instrument IDs, USD `Price`, `Currency`, and UTC `AsOfUtc`. |

`ApiRequestValidation` centralizes API-bound checks for trimmed text, USD
currency, positive `decimal(28,10)` representability without rounding, and
zero-offset UTC timestamps. The existing Domain constructors and Application
services remain authoritative when constructing and processing entities.

### `ApiExceptionHandler`

File: [`src/FinCore.Api/Middleware/ApiExceptionHandler.cs`](../src/FinCore.Api/Middleware/ApiExceptionHandler.cs)

| Member | Inputs | Return | Behavior |
| --- | --- | --- | --- |
| `TryHandleAsync(HttpContext, Exception, CancellationToken)` | Current HTTP context, thrown exception, and response cancellation token | `ValueTask<bool>` | Emits safe RFC Problem Details: validation `400`, missing portfolio/instrument `404`, business conflicts and missing valuation prices `409` (with `missingInstruments` IDs/symbols), valuation overflow `422`, known lock/SQL command timeout, lock acquisition failure, deadlock, or recognized transient persistence failure `503` with endpoint-neutral detail and `Retry-After`, and sanitized `500` for unexpected failures. It does not expose database messages or stack traces. Client-aborted cancellation does not produce an error body. |

ASP.NET Core `[ApiController]` model validation handles malformed JSON,
malformed GUIDs, and DTO validation failures as `400
ValidationProblemDetails`. Trade-side enums are JSON strings, and integer
enum values are rejected.

## Tests

### `PortfolioDomainTests`

File: [`tests/FinCore.UnitTests/PortfolioDomainTests.cs`](../tests/FinCore.UnitTests/PortfolioDomainTests.cs)

Unit tests for portfolio entity validation, symbol normalization, and
long-only position calculation.

| Method | Behavior checked |
| --- | --- |
| `Portfolio_WithValidValues_NormalizesNameAndCurrency()` | Name and USD currency are normalized. |
| `Portfolio_WithNonUsdCurrency_ThrowsArgumentException()` | Non-USD portfolios are rejected. |
| `Portfolio_WithEmptyId_ThrowsArgumentOutOfRangeException()` | Empty portfolio IDs are rejected. |
| `Portfolio_WithMaximumLengthName_AcceptsBoundary()` | A portfolio name of exactly 200 characters is accepted. |
| `Portfolio_WithNameLongerThanMaximum_ThrowsArgumentOutOfRangeException()` | Names longer than 200 characters are rejected. |
| `Portfolio_WithNonUtcTimestamp_ThrowsArgumentException()` | Non-UTC creation times are rejected. |
| `Instrument_WithValidValues_NormalizesSymbol()` | Symbols are trimmed and uppercased. |
| `Instrument_WithMaximumLengthSymbolAndName_AcceptsBoundary()` | A 20-character symbol and 200-character name are accepted and the symbol is uppercase. |
| `Instrument_WithSymbolLongerThanMaximum_ThrowsArgumentOutOfRangeException()` | Symbols longer than 20 characters are rejected. |
| `Instrument_WithNameLongerThanMaximum_ThrowsArgumentOutOfRangeException()` | Names longer than 200 characters are rejected. |
| `Instrument_WithBlankSymbol_ThrowsArgumentException()` | Blank symbols are rejected. |
| `Instrument_WithNonUsdCurrency_ThrowsArgumentException()` | Non-USD instruments are rejected. |
| `Instrument_WithEmptyId_ThrowsArgumentOutOfRangeException()` | Empty instrument IDs are rejected. |
| `Instrument_WithUnsupportedType_ThrowsArgumentOutOfRangeException()` | Non-equity instrument types are rejected. |
| `Trade_WithValidBuy_StoresTradeValues()` | Valid decimal trade values are retained. |
| `Trade_WithNonPositiveQuantity_ThrowsArgumentOutOfRangeException()` | Zero and negative trade quantities are rejected. |
| `Trade_WithNonPositiveUnitPrice_ThrowsArgumentOutOfRangeException()` | Zero and negative trade prices are rejected. |
| `Trade_WithUnsupportedSide_ThrowsArgumentOutOfRangeException()` | Undefined trade sides are rejected. |
| `Trade_WithEmptyIdentifier_ThrowsArgumentOutOfRangeException()` | Empty trade, portfolio, and instrument IDs are rejected. |
| `Trade_WithNonUtcTimestamp_ThrowsArgumentException()` | Non-UTC execution times are rejected. |
| `MarketPrice_WithValidValues_StoresNormalizedCurrency()` | Valid market prices normalize currency to USD. |
| `MarketPrice_WithNonPositivePrice_ThrowsArgumentOutOfRangeException()` | Zero and negative market prices are rejected. |
| `MarketPrice_WithEmptyIdentifier_ThrowsArgumentOutOfRangeException()` | Empty market-price and instrument IDs are rejected. |
| `MarketPrice_WithNonUsdCurrency_ThrowsArgumentException()` | Non-USD market prices are rejected. |
| `MarketPrice_WithNonUtcTimestamp_ThrowsArgumentException()` | Non-UTC observation times are rejected. |
| `CalculatePositions_WithBuyAndSell_ReturnsNetHolding()` | Buys add and sells subtract quantities in execution-time order. |
| `CalculatePositions_WithEqualTimestamps_AppliesBuysBeforeSells()` | Equal-time trades have a deterministic buy-before-sell order regardless of input order. |
| `CalculatePositions_WithEqualTimeOversells_RejectsSameTradeRegardlessOfInputOrder()` | Trade ID provides a stable tie-break for same-time sells, including which oversell is rejected. |
| `CalculatePositions_WithMultiplePortfolioInstrumentPairs_GroupsIndependently()` | Positions are calculated independently for each portfolio/instrument pair. |
| `CalculatePositions_WithZeroHoldings_ReturnsNoPosition()` | Fully closed positions are omitted. |
| `CalculatePositions_WithInsufficientPosition_ThrowsExceptionWithAvailableQuantity()` | Overselling raises a domain exception with the available balance, even if a later buy would increase holdings. |
| `CalculatePositions_WithNullTrades_ThrowsArgumentNullException()` | A null trade sequence is rejected. |
| `CalculatePositions_WhenQuantitySumExceedsDecimalRange_ThrowsOverflowException()` | Decimal quantity overflow is surfaced rather than hidden. |

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

### `PortfolioApiEndpointTests`

File: [`tests/FinCore.UnitTests/PortfolioApiEndpointTests.cs`](../tests/FinCore.UnitTests/PortfolioApiEndpointTests.cs)

In-memory HTTP tests use fake Application interfaces without a database.
They cover portfolio creation and Location, portfolio/position retrieval,
invalid currency and financial precision, malformed GUIDs, invalid enum and
non-UTC timestamps, not-found and business conflict responses, sanitized
unexpected failures, and retryable transient errors.

### `PortfolioApiIntegrationTests`

File: [`tests/FinCore.IntegrationTests/PortfolioApiIntegrationTests.cs`](../tests/FinCore.IntegrationTests/PortfolioApiIntegrationTests.cs)

HTTP requests use the existing run-owned SQL Server fixture. The tests verify
the configured DbContext targets the current generated database, exercise the
portfolio/instrument/trade/position/price workflow, replay and conflicting
trade IDs, overselling, concurrent sells, and end-to-end portfolio valuation
through the actual API pipeline.

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

### `SqlServerPersistenceTests`

File: [`tests/FinCore.IntegrationTests/SqlServerPersistenceTests.cs`](../tests/FinCore.IntegrationTests/SqlServerPersistenceTests.cs)

Opt-in SQL Server tests run only when
`FINCORE_SQLSERVER_TEST_CONNECTION_STRING` is configured. They use
`SqlServerTestDatabaseFixture`, which validates the local SQL Server 2022
Developer instance and requires `127.0.0.1,1433` for the documented Windows
Docker setup; it rejects `localhost,1433` because Windows may resolve it to
unpublished IPv6 loopback. It creates a run-owned database, applies
migrations, and drops only that database after validating its generated name
and ownership marker. The supplied `FinCoreIntegrationTests` catalog is not
opened.

`SqlServerTestDatabaseFixtureTests.InitializeAsync_WithLocalhostEndpoint_RejectsConnectionBeforeConnecting`
uses a synthetic connection string and verifies `localhost,1433` is rejected
before the fixture attempts a database connection.

| Method | Behavior checked |
| --- | --- |
| `Repositories_RoundTripPortfolioInstrumentTradeAndMarketPrice()` | Writes the four entity types and reads them using a new scoped context, including decimal precision and normalized symbol. |
| `MarketPriceRepository_ReturnsLatestPriceOrNullWhenMissing()` | Chooses the latest timestamped price and returns null for an instrument without price history. |
| `DbContext_WhenFinancialValueExceedsColumnPrecision_RejectsItBeforeSave()` | Rejects trade and market-price decimals that would be rounded by `decimal(28,10)` columns. |
| `UnitOfWork_WhenOperationFails_RollsBackPersistedChanges()` | Confirms an exception before transaction commit rolls back a change already saved inside the serializable transaction. |
| `Migration_AppliesExpectedHistoryAndSchemaObjects()` | Verifies the migration history, FK delete rules, check constraints, and ordered/unique indexes. |
| `InstrumentRepository_RejectsDuplicateNormalizedSymbols()` | Verifies the database unique index rejects symbols that Domain normalization makes equal. |
| `DatabaseCheckConstraints_RejectInvalidInstrumentCurrencyAndMarketPrice()` | Bypasses Domain constructors and confirms SQL Server rejects an unsupported currency and a non-positive market price. |

### `PortfolioApplicationServiceTests`

File: [`tests/FinCore.UnitTests/PortfolioApplicationServiceTests.cs`](../tests/FinCore.UnitTests/PortfolioApplicationServiceTests.cs)

In-memory Application tests use repository-contract fakes and a fixed UTC
clock; they do not depend on EF Core or SQL Server.

| Method | Behavior checked |
| --- | --- |
| `PortfolioService_CreatesUsdPortfolioAtCurrentUtcTime()` | Creates a trimmed USD portfolio using the injected UTC time. |
| `InstrumentService_NormalizesSymbolsAndRejectsDuplicate()` | Normalizes symbols and rejects an existing normalized symbol. |
| `TradeService_BuyThenSell_ReturnsRemainingPosition()` | Records a buy and sell and derives the remaining quantity. |
| `TradeService_BackdatedTradesValidateCompleteChronology()` | Accepts a valid backdated buy and rejects a backdated sell that oversells at its chronological point. |
| `TradeService_EqualExecutionTimestampsUseDomainOrdering()` | Applies BUY before SELL at an identical execution timestamp. |
| `TradeService_IdempotencyReturnsExactReplayAndRejectsConflict()` | Returns an identical replay and rejects a different payload with the same trade ID. |
| `MarketPriceService_ReturnsLatestEligiblePriceOrNull()` | Returns only prices at or before the UTC cutoff and returns null when none qualifies. |
| `ValuationService_EmptyPortfolioUsesSingleCurrentUtcCutoff()` | Captures the injected clock once and returns no positions with a zero total. |
| `ValuationService_Buy100Sell20_ValuesRemaining80Exactly()` | Values the remaining 80 shares at USD 450 to USD 36,000. |
| `ValuationService_MultipleInstruments_OmitsClosedPositions()` | Values multiple open instruments exactly and omits the fully closed holding. |
| `ValuationService_HistoricalCutoff_UsesOnlyEligibleTradesAndPrices()` | Excludes post-cutoff trades and prices. |
| `ValuationService_EqualPriceTimestamps_UsesGreatestPriceId()` | Applies deterministic ID tie-breaking for prices at the same timestamp. |
| `ValuationService_MultipleMissingPrices_ReportsEveryAffectedInstrument()` | Reports every missing held-instrument price and returns no partial result. |
| `ValuationService_DecimalOverflow_ThrowsExplicitValuationException()` | Surfaces checked decimal overflow as an explicit Application error. |
| `ValuationService_TotalSumOverflow_ThrowsExplicitValuationException()` | Rejects an unrepresentable checked sum across otherwise representable positions. |
| `ValuationService_CanceledToken_PropagatesCancellation()` | Propagates cancellation through the transaction and repository path. |

`PortfolioApiEndpointTests` additionally verifies valuation success, invalid
cutoffs, missing portfolios, detailed missing-price conflicts, empty results,
and sanitized `422` overflow handling.

### `PortfolioValuationIntegrationTests`

File: [`tests/FinCore.IntegrationTests/PortfolioValuationIntegrationTests.cs`](../tests/FinCore.IntegrationTests/PortfolioValuationIntegrationTests.cs)

Opt-in SQL Server tests use the same run-owned migrated database fixture.
They verify historical trade and price cutoffs, SQL latest-price tie-breaking,
and that a valuation overlapping an atomic trade-and-price commit returns a
serializable state rather than a mixed quantity/price result.

### `TradeApplicationIntegrationTests`

File: [`tests/FinCore.IntegrationTests/TradeApplicationIntegrationTests.cs`](../tests/FinCore.IntegrationTests/TradeApplicationIntegrationTests.cs)

These opt-in SQL Server tests use the same run-owned, migrated database fixture
as `SqlServerPersistenceTests`. Concurrent service calls use independent
dependency-injection scopes and therefore separate scoped DbContexts.

| Method | Behavior checked |
| --- | --- |
| `TradeService_ConcurrentSellsSerializeAndPreventOverselling()` | Two concurrent sells of 7 against 10 allow exactly one commit and leave 3 shares. |
| `TradeService_BackdatedTradesValidateCompleteChronologicalHistory()` | Persists a valid backdated buy and confirms an invalid backdated sell is not inserted. |
| `TradeService_EqualExecutionTimestampsUseDeterministicDomainOrdering()` | Persists equal-time buy/sell trades and checks the Domain result. |
| `TradeService_IdempotentReplayReturnsStoredTradeAndConflictsAreRejected()` | Replays a canonical duplicate without another row and rejects a conflicting payload. |
| `TradeService_SaveFailureRollsBackAndAllowsRetryWithSameId()` | Rolls back a precision failure, retries with the same ID, and verifies a committed replay. |
| `TradeService_CancellationWhileWaitingForLockDoesNotInsertTrade()` | Cancels a blocked lock request and confirms no trade was inserted. |
| `UnitOfWork_WhenApplicationLockTimesOut_ThrowsTimeoutException()` | Holds a resource lock until the configured 15-second lock timeout is surfaced. |
