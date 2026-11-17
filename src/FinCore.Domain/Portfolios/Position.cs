namespace FinCore.Domain.Portfolios;

/// <summary>
/// Represents a positive long holding derived from a portfolio's trades.
/// </summary>
public sealed class Position
{
    internal Position(Guid portfolioId, Guid instrumentId, decimal quantity)
    {
        PortfolioId = portfolioId;
        InstrumentId = instrumentId;
        Quantity = quantity;
    }

    /// <summary>
    /// Gets the identifier of the portfolio holding the instrument.
    /// </summary>
    public Guid PortfolioId { get; }

    /// <summary>
    /// Gets the identifier of the held instrument.
    /// </summary>
    public Guid InstrumentId { get; }

    /// <summary>
    /// Gets the positive net quantity; positions with zero holdings are omitted.
    /// </summary>
    public decimal Quantity { get; }
}
