using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Benchmarks;

internal static class SyntheticPortfolioWorkload
{
    public static readonly int[] InstrumentCounts = [5, 50, 500, 2_000];

    public static SyntheticWorkload Create(int instrumentCount)
    {
        if (!InstrumentCounts.Contains(instrumentCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentCount),
                instrumentCount,
                "Only the documented synthetic portfolio sizes are supported.");
        }

        Guid portfolioId = CreateId(instrumentCount, entityKind: 1, index: 0);
        DateTimeOffset asOfUtc = new(2025, 1, 2, 12, 0, 0, TimeSpan.Zero);

        List<SyntheticInstrument> instruments = new(instrumentCount);
        decimal expectedTotal = 0m;
        for (int index = 1; index <= instrumentCount; index++)
        {
            Guid instrumentId = CreateId(instrumentCount, entityKind: 2, index);
            string symbol = $"SYN{instrumentCount:D4}{index:D6}";
            decimal eligiblePrice = 10m + index / 100m;
            expectedTotal = checked(expectedTotal + checked(80m * eligiblePrice));
            instruments.Add(new SyntheticInstrument(
                instrumentId,
                symbol,
                eligiblePrice,
                CreateId(instrumentCount, entityKind: 5, index)));
        }

        return new SyntheticWorkload(
            instrumentCount,
            portfolioId,
            asOfUtc,
            expectedTotal,
            instruments);
    }

    public static async Task SeedAsync(string connectionString, SyntheticWorkload workload)
    {
        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name))
            .Options;

        await using FinCoreDbContext dbContext = new(options);
        DateTimeOffset createdAt = new(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);
        dbContext.Portfolios.Add(
            new Portfolio(
                workload.PortfolioId,
                $"Synthetic {workload.InstrumentCount}",
                "USD",
                createdAt));

        List<Instrument> instruments = new(workload.Instruments.Count);
        List<Trade> trades = new(workload.Instruments.Count * 2);
        List<MarketPrice> prices = new(workload.Instruments.Count * 3);

        for (int index = 1; index <= workload.Instruments.Count; index++)
        {
            SyntheticInstrument expected = workload.Instruments[index - 1];
            instruments.Add(new Instrument(
                expected.InstrumentId,
                expected.Symbol,
                $"Synthetic equity {index:D6}",
                "USD",
                InstrumentType.Equity));

            trades.Add(new Trade(
                CreateId(workload.InstrumentCount, entityKind: 3, index),
                workload.PortfolioId,
                expected.InstrumentId,
                TradeSide.Buy,
                100m,
                expected.EligiblePrice,
                new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero)));
            trades.Add(new Trade(
                CreateId(workload.InstrumentCount, entityKind: 4, index),
                workload.PortfolioId,
                expected.InstrumentId,
                TradeSide.Sell,
                20m,
                expected.EligiblePrice,
                new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero)));

            prices.Add(new MarketPrice(
                CreateId(workload.InstrumentCount, entityKind: 6, index),
                expected.InstrumentId,
                expected.EligiblePrice / 2m,
                "USD",
                new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero)));
            prices.Add(new MarketPrice(
                expected.EligiblePriceId,
                expected.InstrumentId,
                expected.EligiblePrice,
                "USD",
                new DateTimeOffset(2025, 1, 2, 11, 0, 0, TimeSpan.Zero)));
            prices.Add(new MarketPrice(
                CreateId(workload.InstrumentCount, entityKind: 7, index),
                expected.InstrumentId,
                expected.EligiblePrice + 1_000m,
                "USD",
                new DateTimeOffset(2025, 1, 2, 12, 1, 0, TimeSpan.Zero)));
        }

        dbContext.Instruments.AddRange(instruments);
        dbContext.Trades.AddRange(trades);
        dbContext.MarketPrices.AddRange(prices);
        await dbContext.SaveChangesAsync();
    }

    public static void ValidateValuation(
        SyntheticWorkload workload,
        FinCore.Application.Portfolios.PortfolioValuation valuation)
    {
        if (valuation.PortfolioId != workload.PortfolioId ||
            valuation.Currency != "USD" ||
            valuation.AsOfUtc != workload.AsOfUtc ||
            valuation.Positions.Count != workload.Instruments.Count ||
            valuation.TotalMarketValue != workload.ExpectedTotal)
        {
            throw new InvalidOperationException(
                $"Synthetic {workload.InstrumentCount}-instrument valuation did not match its expected result.");
        }

        Dictionary<Guid, SyntheticInstrument> expectedById =
            workload.Instruments.ToDictionary(instrument => instrument.InstrumentId);
        foreach (FinCore.Application.Portfolios.PortfolioValuationPosition position in valuation.Positions)
        {
            if (!expectedById.TryGetValue(position.InstrumentId, out SyntheticInstrument? expected) ||
                position.Symbol != expected.Symbol ||
                position.Quantity != 80m ||
                position.MarketPrice != expected.EligiblePrice ||
                position.PriceAsOfUtc != new DateTimeOffset(2025, 1, 2, 11, 0, 0, TimeSpan.Zero) ||
                position.MarketValue != checked(80m * expected.EligiblePrice))
            {
                throw new InvalidOperationException(
                    $"Synthetic {workload.InstrumentCount}-instrument valuation selected unexpected holdings or prices.");
            }
        }
    }

    private static Guid CreateId(int workloadSize, int entityKind, int index)
    {
        long encoded = ((long)workloadSize << 32) | ((long)entityKind << 24) | (uint)index;
        return Guid.Parse($"00000000-0000-0000-0000-{encoded:X12}");
    }
}

internal sealed record SyntheticWorkload(
    int InstrumentCount,
    Guid PortfolioId,
    DateTimeOffset AsOfUtc,
    decimal ExpectedTotal,
    IReadOnlyList<SyntheticInstrument> Instruments);

internal sealed record SyntheticInstrument(
    Guid InstrumentId,
    string Symbol,
    decimal EligiblePrice,
    Guid EligiblePriceId);
