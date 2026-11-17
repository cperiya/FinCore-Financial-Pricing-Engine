namespace FinCore.Domain.Portfolios;

/// <summary>
/// Indicates that a sell trade would make a portfolio's instrument position negative.
/// </summary>
public sealed class InsufficientPositionException : InvalidOperationException
{
    internal InsufficientPositionException(
        Guid portfolioId,
        Guid instrumentId,
        Guid tradeId,
        decimal availableQuantity,
        decimal requestedQuantity)
        : base(
            $"Trade '{tradeId}' requests {requestedQuantity} units, but only " +
            $"{availableQuantity} units are available in portfolio '{portfolioId}'.")
    {
        PortfolioId = portfolioId;
        InstrumentId = instrumentId;
        TradeId = tradeId;
        AvailableQuantity = availableQuantity;
        RequestedQuantity = requestedQuantity;
    }

    /// <summary>
    /// Gets the identifier of the portfolio whose position would become negative.
    /// </summary>
    public Guid PortfolioId { get; }

    /// <summary>
    /// Gets the identifier of the instrument being sold.
    /// </summary>
    public Guid InstrumentId { get; }

    /// <summary>
    /// Gets the identifier of the sell trade that exceeds the available position.
    /// </summary>
    public Guid TradeId { get; }

    /// <summary>
    /// Gets the quantity held immediately before the rejected sell trade.
    /// </summary>
    public decimal AvailableQuantity { get; }

    /// <summary>
    /// Gets the quantity requested by the rejected sell trade.
    /// </summary>
    public decimal RequestedQuantity { get; }
}
