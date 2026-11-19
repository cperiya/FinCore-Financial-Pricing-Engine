using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;

namespace FinCore.Benchmarks;

internal readonly record struct CacheScheduleSlot(int Sample, int Block, bool PortfolioA, bool JsonFirst)
{
    internal string ModeOrder => JsonFirst ? "json-first" : "scalar-first";
}

internal static class CacheFeasibilitySchedule
{
    internal const string Version = "counterbalanced-v1";

    internal static CacheScheduleSlot At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new CacheScheduleSlot(index + 1, index / 4 + 1, index % 2 == 0, index % 4 >= 2);
    }

    internal static CacheScheduleCounts Count(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int aScalar = 0, aJson = 0, bScalar = 0, bJson = 0;
        for (int i = 0; i < count; i++)
        {
            CacheScheduleSlot slot = At(i);
            if (slot.PortfolioA && !slot.JsonFirst) aScalar++;
            else if (slot.PortfolioA) aJson++;
            else if (!slot.JsonFirst) bScalar++;
            else bJson++;
        }
        return new CacheScheduleCounts(count, aScalar, aJson, bScalar, bJson);
    }
}

internal sealed record CacheScheduleCounts(
    int Samples, int PortfolioAScalarFirst, int PortfolioAJsonFirst,
    int PortfolioBScalarFirst, int PortfolioBJsonFirst);

internal static class CacheFeasibilityScheduleSelfTest
{
    internal static void Run()
    {
        CacheScheduleSlot[] first = Enumerable.Range(0, 100).Select(CacheFeasibilitySchedule.At).ToArray();
        CacheScheduleSlot[] second = Enumerable.Range(0, 100).Select(CacheFeasibilitySchedule.At).ToArray();
        if (!first.SequenceEqual(second) ||
            CacheFeasibilitySchedule.Count(100) != new CacheScheduleCounts(100, 25, 25, 25, 25) ||
            CacheFeasibilitySchedule.Count(10) != new CacheScheduleCounts(10, 3, 2, 3, 2) ||
            CacheFeasibilitySchedule.Count(6) != new CacheScheduleCounts(6, 2, 1, 2, 1))
        {
            throw new InvalidOperationException("The schedule is not deterministic or balanced.");
        }
        foreach (CacheScheduleSlot[] block in first.Chunk(4))
        {
            if (block.Select(slot => (slot.PortfolioA, slot.JsonFirst)).Distinct().Count() != 4 ||
                block.Any(slot => slot.Block != block[0].Block))
            {
                throw new InvalidOperationException("A matched block does not contain all portfolio/order pairs.");
            }
        }
        Console.WriteLine("Cache feasibility scheduling tests passed.");
    }
}

internal sealed class CacheFeasibilityObservation
{
    internal Guid[] Requested { get; private set; } = [];
    internal MarketPrice[] Selected { get; private set; } = [];
    internal int Calls { get; private set; }

    internal void Reset()
    {
        Requested = [];
        Selected = [];
        Calls = 0;
    }

    internal void Record(IReadOnlyCollection<Guid> requested, IReadOnlyList<MarketPrice> selected)
    {
        Requested = requested.ToArray();
        Selected = selected.ToArray();
        Calls++;
    }
}

internal sealed class ObservedMarketPriceRepository(
    IMarketPriceRepository inner,
    CacheFeasibilityObservation observation) : IMarketPriceRepository
{
    public Task AddAsync(MarketPrice price, CancellationToken cancellationToken) =>
        inner.AddAsync(price, cancellationToken);

    public Task<MarketPrice?> GetLatestByInstrumentIdAsync(Guid id, CancellationToken cancellationToken) =>
        inner.GetLatestByInstrumentIdAsync(id, cancellationToken);

    public Task<MarketPrice?> GetLatestAtOrBeforeAsync(Guid id, DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        inner.GetLatestAtOrBeforeAsync(id, cutoff, cancellationToken);

    public async Task<IReadOnlyList<MarketPrice>> GetLatestAtOrBeforeAsync(
        IReadOnlyCollection<Guid> ids, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketPrice> result = await inner.GetLatestAtOrBeforeAsync(ids, cutoff, cancellationToken);
        observation.Record(ids, result);
        return result;
    }
}

internal sealed class CacheFeasibilityOracle
{
    private readonly Dictionary<(Guid Instrument, DateTimeOffset Cutoff), Guid> _exact = [];
    private readonly Dictionary<Guid, (Guid Price, DateTimeOffset From, DateTimeOffset? Until)> _intervals = [];
    private readonly Dictionary<Guid, Guid> _lastSelected = [];

    internal long Requested { get; private set; }
    internal long Distinct { get; private set; }
    internal long RepeatedSelection { get; private set; }
    internal long ExactHits { get; private set; }
    internal long IntervalHits { get; private set; }
    internal long ExactFullBatches { get; private set; }
    internal long ExactPartialBatches { get; private set; }
    internal long IntervalFullBatches { get; private set; }
    internal long IntervalPartialBatches { get; private set; }
    internal long Batches { get; private set; }
    internal int Writes { get; private set; }

    internal void Invalidate(Guid instrument)
    {
        foreach ((Guid Instrument, DateTimeOffset Cutoff) key in _exact.Keys
                     .Where(key => key.Instrument == instrument).ToArray())
        {
            _exact.Remove(key);
        }
        _intervals.Remove(instrument);
        Writes++;
    }

    internal void Observe(
        DateTimeOffset cutoff,
        IReadOnlyList<Guid> requested,
        IReadOnlyList<MarketPrice> selected,
        Func<Guid, DateTimeOffset, (MarketPrice Selected, DateTimeOffset? Next)> reference)
    {
        Requested += requested.Count;
        Guid[] ids = requested.Distinct().ToArray();
        Distinct += ids.Length;
        Dictionary<Guid, MarketPrice> prices = selected.ToDictionary(price => price.InstrumentId);
        if (prices.Count != ids.Length)
        {
            throw new InvalidOperationException("The selected price set is incomplete.");
        }

        foreach (Guid[] batch in ids.Chunk(2_000))
        {
            int exactHits = 0;
            int intervalHits = 0;
            Batches++;
            foreach (Guid id in batch)
            {
                (MarketPrice expected, DateTimeOffset? next) = reference(id, cutoff);
                if (!prices.TryGetValue(id, out MarketPrice? actual) ||
                    actual.Id != expected.Id || actual.Price != expected.Price || actual.AsOfUtc != expected.AsOfUtc)
                {
                    throw new InvalidOperationException("The actual SQL price identity differs from the reference.");
                }

                if (_lastSelected.TryGetValue(id, out Guid previous) && previous == actual.Id)
                {
                    RepeatedSelection++;
                }
                _lastSelected[id] = actual.Id;

                if (_exact.TryGetValue((id, cutoff), out Guid exactPrice))
                {
                    if (exactPrice != actual.Id)
                    {
                        throw new InvalidOperationException("Stale exact-key oracle entry.");
                    }
                    exactHits++;
                }
                else
                {
                    _exact.Add((id, cutoff), actual.Id);
                }

                if (_intervals.TryGetValue(id, out var interval) &&
                    cutoff >= interval.From && (!interval.Until.HasValue || cutoff < interval.Until.Value))
                {
                    if (interval.Price != actual.Id)
                    {
                        throw new InvalidOperationException("Stale interval oracle entry.");
                    }
                    intervalHits++;
                }
                else
                {
                    _intervals[id] = (actual.Id, actual.AsOfUtc, next);
                }
            }

            ExactHits += exactHits;
            IntervalHits += intervalHits;
            if (exactHits == batch.Length) ExactFullBatches++;
            else if (exactHits > 0) ExactPartialBatches++;
            if (intervalHits == batch.Length) IntervalFullBatches++;
            else if (intervalHits > 0) IntervalPartialBatches++;
        }
    }
}

internal static class CacheFeasibilitySelfTest
{
    internal static void Run()
    {
        DateTimeOffset start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        MarketPrice first = new(Guid.NewGuid(), a, 1m, "USD", start);
        MarketPrice second = new(Guid.NewGuid(), b, 2m, "USD", start);
        MarketPrice future = new(Guid.NewGuid(), a, 3m, "USD", start.AddHours(1));
        Dictionary<Guid, MarketPrice> current = new() { [a] = first, [b] = second };
        (MarketPrice, DateTimeOffset?) Reference(Guid id, DateTimeOffset _) =>
            (current[id], id == a && current[id] == first ? future.AsOfUtc : null);

        CacheFeasibilityOracle oracle = new();
        Guid[] ids = [a, b];
        oracle.Observe(start, ids, [first, second], Reference);
        oracle.Observe(start, ids, [first, second], Reference);
        oracle.Observe(start.AddMinutes(1), ids, [first, second], Reference);
        if (oracle.ExactHits != 2 || oracle.IntervalHits != 4 ||
            oracle.ExactFullBatches != 1 || oracle.IntervalFullBatches != 2)
        {
            throw new InvalidOperationException("Reuse and full-batch oracle test failed.");
        }

        oracle.Invalidate(a);
        current[a] = future;
        oracle.Observe(start.AddHours(1), ids, [future, second], Reference);
        if (oracle.IntervalPartialBatches != 1 || oracle.ExactHits != 2 || oracle.Writes != 1)
        {
            throw new InvalidOperationException("Invalidation, interval boundary or partial-batch test failed.");
        }

        bool rejected = false;
        try
        {
            oracle.Observe(start.AddHours(1), ids, [first, second], Reference);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        if (!rejected) throw new InvalidOperationException("The oracle accepted an incorrect selected price.");

        CacheFeasibilityOracle backdatedOracle = new();
        backdatedOracle.Observe(start, [a], [first], (_, _) => (first, null));
        backdatedOracle.Invalidate(a);
        MarketPrice replacement = new(Guid.NewGuid(), a, 4m, "USD", start);
        backdatedOracle.Observe(start, [a], [replacement], (_, _) => (replacement, null));
        if (backdatedOracle.ExactHits != 0 || backdatedOracle.IntervalHits != 0)
        {
            throw new InvalidOperationException("A write did not invalidate a previously cached cutoff.");
        }

        CacheFeasibilityOracle boundaryOracle = new();
        boundaryOracle.Observe(start, [a], [first], (_, _) => (first, future.AsOfUtc));
        boundaryOracle.Observe(future.AsOfUtc, [a], [future], (_, _) => (future, null));
        if (boundaryOracle.IntervalHits != 0)
        {
            throw new InvalidOperationException("The exclusive next-price interval boundary was not respected.");
        }

        MarketPrice[] manyPrices = Enumerable.Range(0, 2_001)
            .Select(_ => new MarketPrice(Guid.NewGuid(), Guid.NewGuid(), 1m, "USD", start))
            .ToArray();
        Guid[] manyIds = manyPrices.Select(price => price.InstrumentId).ToArray();
        Dictionary<Guid, MarketPrice> manyById = manyPrices.ToDictionary(price => price.InstrumentId);
        CacheFeasibilityOracle batchOracle = new();
        (MarketPrice, DateTimeOffset?) ManyReference(Guid id, DateTimeOffset _) => (manyById[id], null);
        batchOracle.Observe(start, manyIds, manyPrices, ManyReference);
        batchOracle.Observe(start, manyIds, manyPrices, ManyReference);
        batchOracle.Invalidate(manyIds[0]);
        batchOracle.Observe(start, manyIds, manyPrices, ManyReference);
        if (batchOracle.Batches != 6 || batchOracle.ExactFullBatches != 3 ||
            batchOracle.ExactPartialBatches != 1 || batchOracle.IntervalFullBatches != 3 ||
            batchOracle.IntervalPartialBatches != 1)
        {
            throw new InvalidOperationException("The 2,000-ID batch boundary was not respected.");
        }
        CacheFeasibilityOracle nextRun = new();
        nextRun.Observe(start, [a], [first], (_, _) => (first, null));
        if (nextRun.ExactHits != 0 || nextRun.IntervalHits != 0 || nextRun.Writes != 0)
        {
            throw new InvalidOperationException("A new oracle retained state from an earlier run.");
        }
        Console.WriteLine("Cache feasibility oracle tests passed.");
    }
}
