namespace FinCore.Application.Pricing;

/// <summary>
/// Provides the application use case for valuing a stock position.
/// </summary>
public interface IStockPricingService
{
    /// <summary>
    /// Values a stock position using its unit price and quantity.
    /// </summary>
    /// <param name="symbol">The required stock symbol; it is trimmed and normalized to uppercase.</param>
    /// <param name="unitPrice">The positive price of one share in the caller's currency.</param>
    /// <param name="quantity">The positive number of shares.</param>
    /// <returns>The input values and calculated market value in the same currency.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="unitPrice"/> or <paramref name="quantity"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="symbol"/> is null, empty, or whitespace.</exception>
    /// <exception cref="OverflowException">
    /// Thrown when the market value cannot be represented by <see cref="decimal"/>.
    /// </exception>
    StockValuationResult ValueStock(string symbol, decimal unitPrice, decimal quantity);
}
