using System.Data.SqlTypes;
using System.Security.Cryptography;
using System.Text;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal sealed record CacheScenario(string Name, int Count, int OverlapPercent, bool CurrentCutoff, int WriteEvery);

internal sealed class CacheFeasibilityWorkload
{
    internal static readonly DateTimeOffset HistoricalCutoff = new(2025, 1, 2, 12, 0, 0, TimeSpan.Zero);
    internal static readonly CacheScenario[] Scenarios =
    [
        new("historical-full-overlap-no-writes", 5, 100, false, 0),
        new("current-half-overlap-sparse-writes", 50, 50, true, 25),
        new("historical-no-overlap-frequent-writes", 500, 0, false, 5),
        new("current-full-overlap-sparse-writes", 2_000, 100, true, 25)
    ];

    private readonly Dictionary<Guid, List<MarketPrice>> _history = [];
    private readonly Dictionary<Guid, decimal> _quantitiesA = [];
    private readonly Dictionary<Guid, decimal> _quantitiesB = [];
    private readonly List<Guid> _instrumentsA = [];
    private readonly DateTimeOffset _future;

    internal CacheFeasibilityWorkload(CacheScenario scenario, DateTimeOffset now)
    {
        Scenario = scenario;
        _future = now.AddDays(1);
        PortfolioA = Id(scenario, "portfolio", 1);
        PortfolioB = Id(scenario, "portfolio", 2);
    }

    internal CacheScenario Scenario { get; }
    internal Guid PortfolioA { get; }
    internal Guid PortfolioB { get; }

    internal async Task SeedAsync(string connectionString)
    {
        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(connectionString).Options;
        await using FinCoreDbContext db = new(options);
        db.Portfolios.Add(new Portfolio(PortfolioA, $"Feasibility A {Scenario.Count}", "USD", HistoricalCutoff.AddDays(-10)));
        db.Portfolios.Add(new Portfolio(PortfolioB, $"Feasibility B {Scenario.Count}", "USD", HistoricalCutoff.AddDays(-10)));
        int sharedCount = Scenario.Count * Scenario.OverlapPercent / 100;
        int poolSize = Scenario.Count * 2 - sharedCount;
        for (int i = 0; i < poolSize; i++)
        {
            Guid instrument = Id(Scenario, "instrument", i);
            db.Instruments.Add(new Instrument(instrument, $"CF{Scenario.Count:D4}{i:D6}",
                $"Synthetic feasibility equity {i}", "USD", InstrumentType.Equity));
            AddPrice(db, instrument, "old", i, 5m + i / 100m, HistoricalCutoff.AddHours(-2));
            AddPrice(db, instrument, "base", i, 10m + i / 100m, HistoricalCutoff.AddHours(-1));
            if (i % 11 == 0)
            {
                AddPrice(db, instrument, "tie", i, 11m + i / 100m, HistoricalCutoff.AddHours(-1));
            }
            AddPrice(db, instrument, "future", i, 1_000m + i / 100m, _future);
            if (i < Scenario.Count)
            {
                _instrumentsA.Add(instrument);
                AddTrades(db, PortfolioA, instrument, i, 100m, 20m, _quantitiesA);
            }

            if (i < sharedCount || i >= Scenario.Count)
            {
                AddTrades(db, PortfolioB, instrument, i, 65m, 25m, _quantitiesB);
            }
        }

        await db.SaveChangesAsync();
        if (_quantitiesA.Count != Scenario.Count || _quantitiesB.Count != Scenario.Count)
        {
            throw new InvalidOperationException("The generated portfolio sizes are invalid.");
        }
    }

    internal async Task<Guid> WriteAsync(ServiceProvider provider, int sample)
    {
        Guid instrument = _instrumentsA[(sample / Scenario.WriteEvery - 1) % _instrumentsA.Count];
        MarketPrice price = new(Id(Scenario, "write", sample), instrument,
            200m + sample / 100m, "USD", HistoricalCutoff.AddMinutes(-30));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        FinCoreDbContext db = scope.ServiceProvider.GetRequiredService<FinCoreDbContext>();
        await db.MarketPrices.AddAsync(price);
        await db.SaveChangesAsync();
        _history[instrument].Add(price);
        return instrument;
    }

    internal (MarketPrice Selected, DateTimeOffset? Next) Select(Guid instrument, DateTimeOffset cutoff)
    {
        List<MarketPrice> history = _history[instrument];
        MarketPrice selected = history.Where(price => price.AsOfUtc <= cutoff)
            .OrderByDescending(price => price.AsOfUtc)
            .ThenByDescending(price => new SqlGuid(price.Id))
            .First();
        DateTimeOffset? next = history.Where(price => price.AsOfUtc > selected.AsOfUtc)
            .Select(price => (DateTimeOffset?)price.AsOfUtc).Min();
        return (selected, next);
    }

    internal void Validate(PortfolioValuation valuation, Guid portfolioId, DateTimeOffset cutoff)
    {
        Dictionary<Guid, decimal> quantities = portfolioId == PortfolioA ? _quantitiesA : _quantitiesB;
        if (valuation.PortfolioId != portfolioId || valuation.AsOfUtc != cutoff ||
            valuation.Currency != "USD" || valuation.Positions.Count != quantities.Count)
        {
            throw new InvalidOperationException("Unexpected portfolio, cutoff, currency or position count.");
        }

        decimal total = 0m;
        HashSet<Guid> seen = [];
        foreach (PortfolioValuationPosition position in valuation.Positions)
        {
            if (!seen.Add(position.InstrumentId) ||
                !quantities.TryGetValue(position.InstrumentId, out decimal quantity))
            {
                throw new InvalidOperationException("The valuation contains an unexpected instrument.");
            }

            MarketPrice expected = Select(position.InstrumentId, cutoff).Selected;
            decimal value = checked(quantity * expected.Price);
            if (position.Quantity != quantity || position.MarketPrice != expected.Price ||
                position.PriceAsOfUtc != expected.AsOfUtc || position.MarketValue != value)
            {
                throw new InvalidOperationException("The valuation does not match the SQL-order reference.");
            }

            total = checked(total + value);
        }

        if (valuation.TotalMarketValue != total)
        {
            throw new InvalidOperationException("The valuation total differs from the reference.");
        }
    }

    private void AddPrice(FinCoreDbContext db, Guid instrument, string kind, int index, decimal amount, DateTimeOffset time)
    {
        MarketPrice price = new(Id(Scenario, kind, index), instrument, amount, "USD", time);
        db.MarketPrices.Add(price);
        if (!_history.TryGetValue(instrument, out List<MarketPrice>? prices))
        {
            prices = [];
            _history.Add(instrument, prices);
        }
        prices.Add(price);
    }

    private void AddTrades(FinCoreDbContext db, Guid portfolio, Guid instrument, int index,
        decimal buy, decimal sell, Dictionary<Guid, decimal> quantities)
    {
        string prefix = portfolio == PortfolioA ? "trade-a" : "trade-b";
        db.Trades.Add(new Trade(Id(Scenario, prefix + "-buy", index), portfolio, instrument,
            TradeSide.Buy, buy, 10m, HistoricalCutoff.AddDays(-2)));
        db.Trades.Add(new Trade(Id(Scenario, prefix + "-sell", index), portfolio, instrument,
            TradeSide.Sell, sell, 10m, HistoricalCutoff.AddDays(-1)));
        quantities.Add(instrument, buy - sell);
    }

    private static Guid Id(CacheScenario scenario, string kind, int index)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scenario.Name}:{kind}:{index}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
