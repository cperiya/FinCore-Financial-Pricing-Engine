namespace FinCore.Domain.Portfolios;

/// <summary>
/// Represents a buy or sell transaction for an instrument in a portfolio.
/// </summary>
public sealed class Trade
{
    /// <summary>
    /// Initializes a trade with positive quantity and unit price and a UTC execution time.
    /// </summary>
    /// <param name="id">The non-empty trade identifier.</param>
    /// <param name="portfolioId">The non-empty portfolio identifier.</param>
    /// <param name="instrumentId">The non-empty instrument identifier.</param>
    /// <param name="side">Whether the trade buys or sells the instrument.</param>
    /// <param name="quantity">The positive quantity traded.</param>
    /// <param name="unitPrice">The positive USD price per unit.</param>
    /// <param name="executedAt">The UTC execution timestamp.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an identifier is empty, the side is unsupported, or quantity or unit price is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="executedAt"/> is not UTC.</exception>
    public Trade(
        Guid id,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset executedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Trade identifier must not be empty.");
        }

        if (portfolioId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(portfolioId),
                portfolioId,
                "Portfolio identifier must not be empty.");
        }

        if (instrumentId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentId),
                instrumentId,
                "Instrument identifier must not be empty.");
        }

        if (side is not TradeSide.Buy and not TradeSide.Sell)
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Trade side is not supported.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Trade quantity must be greater than zero.");
        }

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Trade unit price must be greater than zero.");
        }

        Id = id;
        PortfolioId = portfolioId;
        InstrumentId = instrumentId;
        Side = side;
        Quantity = quantity;
        UnitPrice = unitPrice;
        ExecutedAt = UtcTimestamp.RequireUtc(executedAt, nameof(executedAt));
    }

    /// <summary>
    /// Gets the trade identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the identifier of the portfolio containing this trade.
    /// </summary>
    public Guid PortfolioId { get; private set; }

    /// <summary>
    /// Gets the identifier of the traded instrument.
    /// </summary>
    public Guid InstrumentId { get; private set; }

    /// <summary>
    /// Gets whether this trade buys or sells the instrument.
    /// </summary>
    public TradeSide Side { get; private set; }

    /// <summary>
    /// Gets the positive quantity traded.
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// Gets the positive USD execution price per unit.
    /// </summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>
    /// Gets the UTC execution timestamp.
    /// </summary>
    public DateTimeOffset ExecutedAt { get; private set; }
}
