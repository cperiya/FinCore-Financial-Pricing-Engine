namespace FinCore.Domain.Portfolios;

/// <summary>
/// Identifies whether a trade adds to or reduces a long position.
/// </summary>
public enum TradeSide
{
    /// <summary>
    /// Adds the trade quantity to a position.
    /// </summary>
    Buy = 1,

    /// <summary>
    /// Subtracts the trade quantity from a position.
    /// </summary>
    Sell = 2
}
