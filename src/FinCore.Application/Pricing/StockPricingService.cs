using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;

namespace FinCore.Application.Pricing;

/// <summary>
/// Implements the application use case for valuing a stock position.
/// </summary>
public sealed class StockPricingService : IStockPricingService
{
    private readonly StockValuationService _stockValuationService;

    /// <summary>
    /// Initializes the pricing use case with the domain valuation service.
    /// </summary>
    /// <param name="stockValuationService">The domain service that applies stock valuation rules.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="stockValuationService"/> is <see langword="null"/>.
    /// </exception>
    public StockPricingService(StockValuationService stockValuationService)
    {
        ArgumentNullException.ThrowIfNull(stockValuationService);
        _stockValuationService = stockValuationService;
    }

    /// <summary>
    /// Values a stock position and returns its normalized symbol, inputs, and market value.
    /// </summary>
    /// <param name="symbol">
    /// The required stock symbol; surrounding whitespace is removed and letters are uppercased.
    /// No instrument-registration length limit is applied.
    /// </param>
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
    public StockValuationResult ValueStock(string symbol, decimal unitPrice, decimal quantity)
    {
        string normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        decimal marketValue = _stockValuationService.CalculateMarketValue(unitPrice, quantity);
        return new StockValuationResult(normalizedSymbol, unitPrice, quantity, marketValue);
    }
}
