using FinCore.Domain.Valuation;

namespace FinCore.UnitTests;

/// <summary>
/// Verifies stock position valuation rules.
/// </summary>
public sealed class StockValuationServiceTests
{
    private readonly StockValuationService _service = new();

    /// <summary>
    /// Verifies that a valid stock position is valued as price multiplied by quantity.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithValidInputs_ReturnsProduct()
    {
        decimal result = _service.CalculateMarketValue(12.50m, 4m);

        Assert.Equal(50m, result);
    }

    /// <summary>
    /// Verifies that a zero-share position is rejected.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithZeroQuantity_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CalculateMarketValue(12.50m, 0m));

        Assert.Equal("quantity", exception.ParamName);
    }

    /// <summary>
    /// Verifies that a zero unit price is rejected.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithZeroUnitPrice_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CalculateMarketValue(0m, 4m));

        Assert.Equal("unitPrice", exception.ParamName);
    }

    /// <summary>
    /// Verifies that a negative unit price is rejected.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithNegativeUnitPrice_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CalculateMarketValue(-1m, 4m));

        Assert.Equal("unitPrice", exception.ParamName);
    }

    /// <summary>
    /// Verifies that a negative quantity is rejected.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithNegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CalculateMarketValue(1m, -4m));

        Assert.Equal("quantity", exception.ParamName);
    }

    /// <summary>
    /// Verifies that fractional decimal inputs retain exact decimal precision.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WithFractionalInputs_PreservesDecimalPrecision()
    {
        decimal result = _service.CalculateMarketValue(12.3456m, 0.125m);

        Assert.Equal(1.5432000m, result);
    }

    /// <summary>
    /// Verifies that a product outside the decimal range reports overflow.
    /// </summary>
    [Fact]
    public void CalculateMarketValue_WhenProductExceedsDecimalRange_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(
            () => _service.CalculateMarketValue(decimal.MaxValue, 2m));
    }
}
