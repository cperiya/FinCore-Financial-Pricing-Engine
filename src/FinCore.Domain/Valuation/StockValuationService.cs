namespace FinCore.Domain.Valuation;

/// <summary>
/// Calculates the market value of a stock position using decimal arithmetic.
/// </summary>
public sealed class StockValuationService
{
    /// <summary>
    /// Initializes a stateless stock valuation service.
    /// </summary>
    public StockValuationService()
    {
    }

    /// <summary>
    /// Calculates a position's market value as unit price multiplied by quantity.
    /// </summary>
    /// <param name="unitPrice">The positive price of one share, in the caller's currency.</param>
    /// <param name="quantity">The positive number of shares; fractional quantities are supported.</param>
    /// <returns>The market value in the same currency as <paramref name="unitPrice"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="unitPrice"/> or <paramref name="quantity"/> is not positive.
    /// </exception>
    /// <exception cref="OverflowException">
    /// Thrown when the product cannot be represented by <see cref="decimal"/>.
    /// </exception>
    public decimal CalculateMarketValue(decimal unitPrice, decimal quantity)
    {
        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Quantity must be greater than zero.");
        }

        return unitPrice * quantity;
    }
}
