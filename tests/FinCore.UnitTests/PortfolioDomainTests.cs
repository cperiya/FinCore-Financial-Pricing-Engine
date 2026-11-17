using FinCore.Domain.Portfolios;

namespace FinCore.UnitTests;

/// <summary>
/// Verifies portfolio-domain validation and long-only position calculations.
/// </summary>
public sealed class PortfolioDomainTests
{
    private static readonly Guid PortfolioId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid InstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset UtcNow = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that a portfolio trims its name and normalizes its USD currency.
    /// </summary>
    [Fact]
    public void Portfolio_WithValidValues_NormalizesNameAndCurrency()
    {
        Portfolio portfolio = new(PortfolioId, "  Growth  ", " usd ", UtcNow);

        Assert.Equal("Growth", portfolio.Name);
        Assert.Equal("USD", portfolio.BaseCurrency);
        Assert.Equal(UtcNow, portfolio.CreatedAt);
    }

    /// <summary>
    /// Verifies that non-USD portfolios are rejected.
    /// </summary>
    [Fact]
    public void Portfolio_WithNonUsdCurrency_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Portfolio(PortfolioId, "Growth", "EUR", UtcNow));
    }

    /// <summary>
    /// Verifies that an empty portfolio identifier is rejected.
    /// </summary>
    [Fact]
    public void Portfolio_WithEmptyId_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Portfolio(Guid.Empty, "Growth", "USD", UtcNow));
    }

    /// <summary>
    /// Verifies that a portfolio name at the supported maximum length is accepted.
    /// </summary>
    [Fact]
    public void Portfolio_WithMaximumLengthName_AcceptsBoundary()
    {
        string name = new('P', Portfolio.MaximumNameLength);

        Portfolio portfolio = new(PortfolioId, name, "USD", UtcNow);

        Assert.Equal(Portfolio.MaximumNameLength, portfolio.Name.Length);
    }

    /// <summary>
    /// Verifies that portfolio names longer than the storage limit are rejected.
    /// </summary>
    [Fact]
    public void Portfolio_WithNameLongerThanMaximum_ThrowsArgumentOutOfRangeException()
    {
        string name = new('P', Portfolio.MaximumNameLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Portfolio(PortfolioId, name, "USD", UtcNow));
    }

    /// <summary>
    /// Verifies that portfolio timestamps with a non-UTC offset are rejected.
    /// </summary>
    [Fact]
    public void Portfolio_WithNonUtcTimestamp_ThrowsArgumentException()
    {
        DateTimeOffset localOffset = new(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Throws<ArgumentException>(() => new Portfolio(PortfolioId, "Growth", "USD", localOffset));
    }

    /// <summary>
    /// Verifies that instrument symbols are trimmed and uppercased invariantly.
    /// </summary>
    [Fact]
    public void Instrument_WithValidValues_NormalizesSymbol()
    {
        Instrument instrument = new(
            InstrumentId,
            "  msFt ",
            "Microsoft",
            "usd",
            InstrumentType.Equity);

        Assert.Equal("MSFT", instrument.Symbol);
        Assert.Equal("Microsoft", instrument.Name);
        Assert.Equal("USD", instrument.Currency);
    }

    /// <summary>
    /// Verifies instrument symbols and names at their supported maximum lengths are accepted.
    /// </summary>
    [Fact]
    public void Instrument_WithMaximumLengthSymbolAndName_AcceptsBoundary()
    {
        string symbol = new('a', Instrument.MaximumSymbolLength);
        string name = new('N', Instrument.MaximumNameLength);

        Instrument instrument = new(
            InstrumentId,
            symbol,
            name,
            "USD",
            InstrumentType.Equity);

        Assert.Equal(new string('A', Instrument.MaximumSymbolLength), instrument.Symbol);
        Assert.Equal(Instrument.MaximumNameLength, instrument.Name.Length);
    }

    /// <summary>
    /// Verifies instrument symbols longer than the indexed column are rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithSymbolLongerThanMaximum_ThrowsArgumentOutOfRangeException()
    {
        string symbol = new('A', Instrument.MaximumSymbolLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Instrument(InstrumentId, symbol, "Example", "USD", InstrumentType.Equity));
    }

    /// <summary>
    /// Verifies instrument names longer than the storage limit are rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithNameLongerThanMaximum_ThrowsArgumentOutOfRangeException()
    {
        string name = new('N', Instrument.MaximumNameLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Instrument(InstrumentId, "ABC", name, "USD", InstrumentType.Equity));
    }

    /// <summary>
    /// Verifies that blank instrument symbols are rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithBlankSymbol_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new Instrument(InstrumentId, " ", "Example", "USD", InstrumentType.Equity));
    }

    /// <summary>
    /// Verifies that non-USD instruments are rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithNonUsdCurrency_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new Instrument(InstrumentId, "ABC", "Example", "EUR", InstrumentType.Equity));
    }

    /// <summary>
    /// Verifies that an empty instrument identifier is rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithEmptyId_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Instrument(Guid.Empty, "ABC", "Example", "USD", InstrumentType.Equity));
    }

    /// <summary>
    /// Verifies that instrument types outside the supported equity type are rejected.
    /// </summary>
    [Fact]
    public void Instrument_WithUnsupportedType_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Instrument(InstrumentId, "ABC", "Example", "USD", (InstrumentType)2));
    }

    /// <summary>
    /// Verifies that a trade retains its side and positive decimal values.
    /// </summary>
    [Fact]
    public void Trade_WithValidBuy_StoresTradeValues()
    {
        Trade trade = CreateTrade(TradeSide.Buy, 2.5m, 12.34m, UtcNow);

        Assert.Equal(TradeSide.Buy, trade.Side);
        Assert.Equal(2.5m, trade.Quantity);
        Assert.Equal(12.34m, trade.UnitPrice);
        Assert.Equal(UtcNow, trade.ExecutedAt);
    }

    /// <summary>
    /// Verifies that zero or negative trade quantities are rejected.
    /// </summary>
    [Fact]
    public void Trade_WithNonPositiveQuantity_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException zeroException = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTrade(TradeSide.Buy, 0m, 1m, UtcNow));
        ArgumentOutOfRangeException negativeException = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTrade(TradeSide.Buy, -1m, 1m, UtcNow));

        Assert.Equal("quantity", zeroException.ParamName);
        Assert.Equal("quantity", negativeException.ParamName);
    }

    /// <summary>
    /// Verifies that zero or negative trade prices are rejected.
    /// </summary>
    [Fact]
    public void Trade_WithNonPositiveUnitPrice_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException zeroException = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTrade(TradeSide.Buy, 1m, 0m, UtcNow));
        ArgumentOutOfRangeException negativeException = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTrade(TradeSide.Buy, 1m, -1m, UtcNow));

        Assert.Equal("unitPrice", zeroException.ParamName);
        Assert.Equal("unitPrice", negativeException.ParamName);
    }

    /// <summary>
    /// Verifies that undefined trade sides are rejected.
    /// </summary>
    [Fact]
    public void Trade_WithUnsupportedSide_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTrade((TradeSide)0, 1m, 1m, UtcNow));
    }

    /// <summary>
    /// Verifies that empty trade or parent identifiers are rejected.
    /// </summary>
    [Fact]
    public void Trade_WithEmptyIdentifier_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Trade(Guid.Empty, PortfolioId, InstrumentId, TradeSide.Buy, 1m, 1m, UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Trade(Guid.NewGuid(), Guid.Empty, InstrumentId, TradeSide.Buy, 1m, 1m, UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Trade(Guid.NewGuid(), PortfolioId, Guid.Empty, TradeSide.Buy, 1m, 1m, UtcNow));
    }

    /// <summary>
    /// Verifies that trade timestamps with a non-UTC offset are rejected.
    /// </summary>
    [Fact]
    public void Trade_WithNonUtcTimestamp_ThrowsArgumentException()
    {
        DateTimeOffset localOffset = new(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Throws<ArgumentException>(
            () => CreateTrade(TradeSide.Buy, 1m, 1m, localOffset));
    }

    /// <summary>
    /// Verifies that market prices require a positive USD amount and UTC timestamp.
    /// </summary>
    [Fact]
    public void MarketPrice_WithValidValues_StoresNormalizedCurrency()
    {
        MarketPrice marketPrice = new(Guid.NewGuid(), InstrumentId, 10.25m, "usd", UtcNow);

        Assert.Equal(10.25m, marketPrice.Price);
        Assert.Equal("USD", marketPrice.Currency);
        Assert.Equal(UtcNow, marketPrice.AsOfUtc);
    }

    /// <summary>
    /// Verifies that non-positive market prices are rejected.
    /// </summary>
    [Fact]
    public void MarketPrice_WithNonPositivePrice_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MarketPrice(Guid.NewGuid(), InstrumentId, 0m, "USD", UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MarketPrice(Guid.NewGuid(), InstrumentId, -1m, "USD", UtcNow));
    }

    /// <summary>
    /// Verifies that empty market-price and instrument identifiers are rejected.
    /// </summary>
    [Fact]
    public void MarketPrice_WithEmptyIdentifier_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MarketPrice(Guid.Empty, InstrumentId, 1m, "USD", UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MarketPrice(Guid.NewGuid(), Guid.Empty, 1m, "USD", UtcNow));
    }

    /// <summary>
    /// Verifies that non-USD market prices are rejected.
    /// </summary>
    [Fact]
    public void MarketPrice_WithNonUsdCurrency_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new MarketPrice(Guid.NewGuid(), InstrumentId, 10m, "EUR", UtcNow));
    }

    /// <summary>
    /// Verifies that market-price timestamps with a non-UTC offset are rejected.
    /// </summary>
    [Fact]
    public void MarketPrice_WithNonUtcTimestamp_ThrowsArgumentException()
    {
        DateTimeOffset localOffset = new(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5));

        Assert.Throws<ArgumentException>(
            () => new MarketPrice(Guid.NewGuid(), InstrumentId, 10m, "USD", localOffset));
    }

    /// <summary>
    /// Verifies that buys add to holdings and sells subtract from them.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithBuyAndSell_ReturnsNetHolding()
    {
        PositionCalculationService service = new();
        Trade[] trades =
        [
            CreateTrade(TradeSide.Sell, 3m, 10m, UtcNow.AddHours(1)),
            CreateTrade(TradeSide.Buy, 8m, 10m, UtcNow)
        ];

        IReadOnlyList<Position> positions = service.CalculatePositions(trades);

        Position position = Assert.Single(positions);
        Assert.Equal(PortfolioId, position.PortfolioId);
        Assert.Equal(InstrumentId, position.InstrumentId);
        Assert.Equal(5m, position.Quantity);
    }

    /// <summary>
    /// Verifies that equal-time buys are applied before sells regardless of input order.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithEqualTimestamps_AppliesBuysBeforeSells()
    {
        PositionCalculationService service = new();
        Trade buy = CreateTrade(TradeSide.Buy, 3m, 10m, UtcNow);
        Trade sell = CreateTrade(TradeSide.Sell, 2m, 10m, UtcNow);

        Position[] sellFirst = service.CalculatePositions([sell, buy]).ToArray();
        Position[] buyFirst = service.CalculatePositions([buy, sell]).ToArray();

        Position sellFirstPosition = Assert.Single(sellFirst);
        Position buyFirstPosition = Assert.Single(buyFirst);
        Assert.Equal(1m, sellFirstPosition.Quantity);
        Assert.Equal(sellFirstPosition.Quantity, buyFirstPosition.Quantity);
    }

    /// <summary>
    /// Verifies that equal-time sells use trade ID as a stable final tie-breaker.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithEqualTimeOversells_RejectsSameTradeRegardlessOfInputOrder()
    {
        PositionCalculationService service = new();
        Trade buy = CreateTrade(TradeSide.Buy, 1m, 10m, UtcNow);
        Trade firstSell = new(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            PortfolioId,
            InstrumentId,
            TradeSide.Sell,
            1m,
            10m,
            UtcNow);
        Trade secondSell = new(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            PortfolioId,
            InstrumentId,
            TradeSide.Sell,
            1m,
            10m,
            UtcNow);

        InsufficientPositionException firstResult = Assert.Throws<InsufficientPositionException>(
            () => service.CalculatePositions([buy, secondSell, firstSell]));
        InsufficientPositionException reversedResult = Assert.Throws<InsufficientPositionException>(
            () => service.CalculatePositions([firstSell, buy, secondSell]));

        Assert.Equal(secondSell.Id, firstResult.TradeId);
        Assert.Equal(firstResult.TradeId, reversedResult.TradeId);
    }

    /// <summary>
    /// Verifies that holdings are grouped independently by portfolio and instrument.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithMultiplePortfolioInstrumentPairs_GroupsIndependently()
    {
        PositionCalculationService service = new();
        Guid secondPortfolioId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        Guid secondInstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Trade[] trades =
        [
            CreateTrade(TradeSide.Buy, 5m, 10m, UtcNow),
            CreateTrade(TradeSide.Buy, 2m, 10m, UtcNow, secondPortfolioId),
            CreateTrade(TradeSide.Buy, 3m, 10m, UtcNow, instrumentId: secondInstrumentId)
        ];

        IReadOnlyList<Position> positions = service.CalculatePositions(trades);

        Assert.Equal(3, positions.Count);
        Assert.Contains(
            positions,
            position => position.PortfolioId == PortfolioId &&
                        position.InstrumentId == InstrumentId &&
                        position.Quantity == 5m);
        Assert.Contains(
            positions,
            position => position.PortfolioId == secondPortfolioId &&
                        position.InstrumentId == InstrumentId &&
                        position.Quantity == 2m);
        Assert.Contains(
            positions,
            position => position.PortfolioId == PortfolioId &&
                        position.InstrumentId == secondInstrumentId &&
                        position.Quantity == 3m);
    }

    /// <summary>
    /// Verifies that a position closed by an equal sell is omitted from the results.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithZeroHoldings_ReturnsNoPosition()
    {
        PositionCalculationService service = new();
        Trade[] trades =
        [
            CreateTrade(TradeSide.Buy, 4m, 10m, UtcNow),
            CreateTrade(TradeSide.Sell, 4m, 12m, UtcNow.AddHours(1))
        ];

        Assert.Empty(service.CalculatePositions(trades));
    }

    /// <summary>
    /// Verifies that selling more than the chronological holding raises a domain exception.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithInsufficientPosition_ThrowsExceptionWithAvailableQuantity()
    {
        PositionCalculationService service = new();
        Trade sell = CreateTrade(TradeSide.Sell, 3m, 10m, UtcNow.AddHours(1));
        Trade[] trades =
        [
            sell,
            CreateTrade(TradeSide.Buy, 2m, 10m, UtcNow),
            CreateTrade(TradeSide.Buy, 5m, 10m, UtcNow.AddHours(2))
        ];

        InsufficientPositionException exception = Assert.Throws<InsufficientPositionException>(
            () => service.CalculatePositions(trades));

        Assert.Equal(PortfolioId, exception.PortfolioId);
        Assert.Equal(InstrumentId, exception.InstrumentId);
        Assert.Equal(sell.Id, exception.TradeId);
        Assert.Equal(2m, exception.AvailableQuantity);
        Assert.Equal(3m, exception.RequestedQuantity);
    }

    /// <summary>
    /// Verifies that a null trade sequence is rejected.
    /// </summary>
    [Fact]
    public void CalculatePositions_WithNullTrades_ThrowsArgumentNullException()
    {
        PositionCalculationService service = new();

        Assert.Throws<ArgumentNullException>(() => service.CalculatePositions(null!));
    }

    /// <summary>
    /// Verifies that a quantity sum outside the decimal range reports overflow.
    /// </summary>
    [Fact]
    public void CalculatePositions_WhenQuantitySumExceedsDecimalRange_ThrowsOverflowException()
    {
        PositionCalculationService service = new();
        Trade[] trades =
        [
            CreateTrade(TradeSide.Buy, decimal.MaxValue, 1m, UtcNow),
            CreateTrade(TradeSide.Buy, 1m, 1m, UtcNow.AddSeconds(1))
        ];

        Assert.Throws<OverflowException>(() => service.CalculatePositions(trades));
    }

    private static Trade CreateTrade(
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset executedAt,
        Guid? portfolioId = null,
        Guid? instrumentId = null)
    {
        return new Trade(
            Guid.NewGuid(),
            portfolioId ?? PortfolioId,
            instrumentId ?? InstrumentId,
            side,
            quantity,
            unitPrice,
            executedAt);
    }
}
