namespace FinCore.Domain.Portfolios;

/// <summary>
/// Derives long-only instrument positions from a sequence of portfolio trades.
/// </summary>
public sealed class PositionCalculationService
{
    /// <summary>
    /// Initializes a stateless position calculation service.
    /// </summary>
    public PositionCalculationService()
    {
    }

    /// <summary>
    /// Calculates positive net holdings for each portfolio and instrument.
    /// </summary>
    /// <param name="trades">
    /// Trades to aggregate; they are ordered by execution time, then buy-before-sell, then trade identifier.
    /// </param>
    /// <returns>Positive positions ordered by portfolio and instrument identifier; zero holdings are omitted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trades"/> is null.</exception>
    /// <exception cref="InsufficientPositionException">
    /// Thrown when a sell trade would make a chronological position negative.
    /// </exception>
    /// <exception cref="OverflowException">
    /// Thrown when adding quantities exceeds the range of <see cref="decimal"/>.
    /// </exception>
    public IReadOnlyList<Position> CalculatePositions(IEnumerable<Trade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        List<Position> positions = [];

        foreach (IGrouping<(Guid PortfolioId, Guid InstrumentId), Trade> group in trades.GroupBy(
                     trade => (trade.PortfolioId, trade.InstrumentId)))
        {
            decimal quantity = 0m;

            foreach (Trade trade in group
                         .OrderBy(trade => trade.ExecutedAt)
                         .ThenBy(trade => trade.Side)
                         .ThenBy(trade => trade.Id))
            {
                if (trade.Side == TradeSide.Buy)
                {
                    quantity += trade.Quantity;
                    continue;
                }

                if (trade.Quantity > quantity)
                {
                    throw new InsufficientPositionException(
                        trade.PortfolioId,
                        trade.InstrumentId,
                        trade.Id,
                        quantity,
                        trade.Quantity);
                }

                quantity -= trade.Quantity;
            }

            if (quantity > 0m)
            {
                positions.Add(new Position(group.Key.PortfolioId, group.Key.InstrumentId, quantity));
            }
        }

        return positions
            .OrderBy(position => position.PortfolioId)
            .ThenBy(position => position.InstrumentId)
            .ToArray();
    }
}

internal static class CurrencyRules
{
    internal static string NormalizeUsd(string currency, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency, parameterName);

        string normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency != "USD")
        {
            throw new ArgumentException("Only USD currency is supported.", parameterName);
        }

        return normalizedCurrency;
    }
}

internal static class UtcTimestamp
{
    internal static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }

        return value.ToUniversalTime();
    }
}
