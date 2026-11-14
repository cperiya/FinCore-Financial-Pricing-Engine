namespace FinCore.Api.Models;

/// <summary>
/// Represents the calculated market value of a stock position.
/// All monetary values use the same caller-known currency; no currency conversion is applied.
/// </summary>
public sealed record StockValuationResponse
{
    /// <summary>
    /// Gets the normalized uppercase stock symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the price of one share in the caller's currency.
    /// </summary>
    public decimal UnitPrice { get; }

    /// <summary>
    /// Gets the number of shares in the position.
    /// </summary>
    public decimal Quantity { get; }

    /// <summary>
    /// Gets the position market value in the same currency as <see cref="UnitPrice"/>.
    /// </summary>
    public decimal MarketValue { get; }

    /// <summary>
    /// Initializes a response containing the symbol, position inputs, and calculated market value.
    /// </summary>
    /// <param name="symbol">The normalized uppercase stock symbol.</param>
    /// <param name="unitPrice">The price of one share in the caller's currency.</param>
    /// <param name="quantity">The number of shares in the position.</param>
    /// <param name="marketValue">The calculated position value in the same currency.</param>
    public StockValuationResponse(string symbol, decimal unitPrice, decimal quantity, decimal marketValue)
    {
        Symbol = symbol;
        UnitPrice = unitPrice;
        Quantity = quantity;
        MarketValue = marketValue;
    }
}
