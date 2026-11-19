using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Benchmarks;

internal static class TranslationValidationWorkload
{
    public static readonly int[] InstrumentCounts = [1, 5, 50, 500, 2_000, 2_001];

    private static readonly DateTimeOffset ValuationCutoff =
        new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const int TradesPerInstrument = 12;
    private const decimal BuyQuantity = 10.0000000001m;
    private const decimal SellQuantity = 4.1234567890m;
    private static readonly decimal ExpectedPositionQuantity =
        checked(6m * BuyQuantity - 6m * SellQuantity);

    public static TranslationValidationData Create(int instrumentCount)
    {
        if (!InstrumentCounts.Contains(instrumentCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentCount),
                instrumentCount,
                "Only the isolated translation-validation workload sizes are supported.");
        }

        Guid portfolioId = CreateId(instrumentCount, entityKind: 1, index: 0);
        List<TranslationValidationInstrument> instruments = new(instrumentCount);
        decimal expectedTotal = 0m;

        for (int index = 1; index <= instrumentCount; index++)
        {
            Guid instrumentId = CreateId(instrumentCount, entityKind: 2, index);
            string symbol = $"VAL{instrumentCount:D4}{index:D6}";
            decimal latestPrice = 10.1234567890m + index / 10_000m;
            expectedTotal = checked(
                expectedTotal + checked(ExpectedPositionQuantity * latestPrice));
            instruments.Add(new TranslationValidationInstrument(
                instrumentId,
                symbol,
                latestPrice,
                ValuationCutoff.AddHours(-1)));
        }

        return new TranslationValidationData(
            instrumentCount,
            portfolioId,
            ValuationCutoff,
            ExpectedPositionQuantity,
            expectedTotal,
            instruments);
    }

    public static async Task SeedAsync(
        string connectionString,
        TranslationValidationData workload,
        CancellationToken cancellationToken = default)
    {
        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name))
            .Options;

        await using FinCoreDbContext dbContext = new(options);
        dbContext.Portfolios.Add(new Portfolio(
            workload.PortfolioId,
            $"Translation validation {workload.InstrumentCount}",
            "USD",
            workload.AsOfUtc.AddDays(-30)));

        List<Instrument> instruments = new(workload.Instruments.Count);
        List<Trade> trades = new(workload.Instruments.Count * TradesPerInstrument);
        List<MarketPrice> prices = new(workload.Instruments.Count * 2);

        for (int index = 1; index <= workload.Instruments.Count; index++)
        {
            TranslationValidationInstrument expected = workload.Instruments[index - 1];
            instruments.Add(new Instrument(
                expected.InstrumentId,
                expected.Symbol,
                $"Validation equity {index:D6}",
                "USD",
                InstrumentType.Equity));

            for (int tradeIndex = 0; tradeIndex < TradesPerInstrument; tradeIndex++)
            {
                bool isBuy = tradeIndex < 6;
                trades.Add(new Trade(
                    CreateId(
                        workload.InstrumentCount,
                        entityKind: 3,
                        index * TradesPerInstrument + tradeIndex),
                    workload.PortfolioId,
                    expected.InstrumentId,
                    isBuy ? TradeSide.Buy : TradeSide.Sell,
                    isBuy ? BuyQuantity : SellQuantity,
                    11.1234567890m,
                    workload.AsOfUtc.AddDays(-1).AddMinutes(tradeIndex + 1)));
            }

            DateTimeOffset latestPriceTime = workload.AsOfUtc.AddHours(-1);
            if (index % 3 == 0)
            {
                prices.Add(new MarketPrice(
                    CreateId(workload.InstrumentCount, entityKind: 5, index),
                    expected.InstrumentId,
                    expected.LatestPrice + 3m,
                    "USD",
                    workload.AsOfUtc.AddDays(-2)));
            }

            if (index % 11 == 0)
            {
                prices.Add(new MarketPrice(
                    CreateId(workload.InstrumentCount, entityKind: 6, index * 2),
                    expected.InstrumentId,
                    expected.LatestPrice + 1m,
                    "USD",
                    latestPriceTime));
                prices.Add(new MarketPrice(
                    CreateId(workload.InstrumentCount, entityKind: 6, index * 2 + 1),
                    expected.InstrumentId,
                    expected.LatestPrice,
                    "USD",
                    latestPriceTime));
            }
            else
            {
                prices.Add(new MarketPrice(
                    CreateId(workload.InstrumentCount, entityKind: 7, index),
                    expected.InstrumentId,
                    expected.LatestPrice,
                    "USD",
                    latestPriceTime));
            }

            if (index % 5 == 0)
            {
                prices.Add(new MarketPrice(
                    CreateId(workload.InstrumentCount, entityKind: 8, index),
                    expected.InstrumentId,
                    expected.LatestPrice + 1_000m,
                    "USD",
                    workload.AsOfUtc.AddMinutes(1)));
            }
        }

        dbContext.Instruments.AddRange(instruments);
        dbContext.Trades.AddRange(trades);
        dbContext.MarketPrices.AddRange(prices);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static void ValidateValuation(
        TranslationValidationData workload,
        FinCore.Application.Portfolios.PortfolioValuation valuation)
    {
        if (valuation.PortfolioId != workload.PortfolioId ||
            valuation.Currency != "USD" ||
            valuation.AsOfUtc != workload.AsOfUtc ||
            valuation.Positions.Count != workload.Instruments.Count ||
            valuation.TotalMarketValue != workload.ExpectedTotal)
        {
            throw new InvalidOperationException(
                $"The {workload.InstrumentCount}-instrument valuation did not match its exact expected result.");
        }

        Dictionary<Guid, TranslationValidationInstrument> expectedById =
            workload.Instruments.ToDictionary(instrument => instrument.InstrumentId);
        foreach (FinCore.Application.Portfolios.PortfolioValuationPosition position in valuation.Positions)
        {
            if (!expectedById.TryGetValue(position.InstrumentId, out TranslationValidationInstrument? expected) ||
                position.Symbol != expected.Symbol ||
                position.Quantity != workload.ExpectedPositionQuantity ||
                position.MarketPrice != expected.LatestPrice ||
                position.PriceAsOfUtc != expected.PriceAsOfUtc ||
                position.MarketValue != checked(workload.ExpectedPositionQuantity * expected.LatestPrice))
            {
                throw new InvalidOperationException(
                    $"The {workload.InstrumentCount}-instrument valuation selected unexpected holdings or prices.");
            }
        }
    }

    private static Guid CreateId(int workloadSize, int entityKind, int index)
    {
        long encoded = ((long)workloadSize << 32) | ((long)entityKind << 24) | (uint)index;
        return Guid.Parse($"00000000-0000-0000-0000-{encoded:X12}");
    }
}

internal sealed record TranslationValidationData(
    int InstrumentCount,
    Guid PortfolioId,
    DateTimeOffset AsOfUtc,
    decimal ExpectedPositionQuantity,
    decimal ExpectedTotal,
    IReadOnlyList<TranslationValidationInstrument> Instruments);

internal sealed record TranslationValidationInstrument(
    Guid InstrumentId,
    string Symbol,
    decimal LatestPrice,
    DateTimeOffset PriceAsOfUtc);
