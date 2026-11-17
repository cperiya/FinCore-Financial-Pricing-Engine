using System.ComponentModel.DataAnnotations;
using FinCore.Domain.Portfolios;

namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// Request to record a portfolio trade using a stable client-supplied trade identifier.
/// </summary>
public sealed record RecordTradeRequest : IValidatableObject
{
    /// <summary>
    /// Gets the caller-supplied trade identifier and idempotency key.
    /// </summary>
    [Required]
    public Guid? TradeId { get; init; }

    /// <summary>
    /// Gets the instrument identifier.
    /// </summary>
    [Required]
    public Guid? InstrumentId { get; init; }

    /// <summary>
    /// Gets the trade side, serialized as <c>Buy</c> or <c>Sell</c>.
    /// </summary>
    [Required]
    public TradeSide? Side { get; init; }

    /// <summary>
    /// Gets the positive quantity to trade.
    /// </summary>
    [Required]
    public decimal? Quantity { get; init; }

    /// <summary>
    /// Gets the positive USD unit price.
    /// </summary>
    [Required]
    public decimal? UnitPrice { get; init; }

    /// <summary>
    /// Gets the UTC execution time.
    /// </summary>
    [Required]
    public DateTimeOffset? ExecutedAt { get; init; }

    /// <summary>
    /// Validates required identifiers, enum, financial values, and execution time.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Validation errors for unsupported values.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TradeId.HasValue && TradeId.Value == Guid.Empty)
        {
            yield return new ValidationResult("A non-empty tradeId is required.", [nameof(TradeId)]);
        }

        if (InstrumentId.HasValue && InstrumentId.Value == Guid.Empty)
        {
            yield return new ValidationResult("A non-empty instrumentId is required.", [nameof(InstrumentId)]);
        }

        if (Side.HasValue && !Enum.IsDefined(Side.Value))
        {
            yield return new ValidationResult("A supported trade side is required.", [nameof(Side)]);
        }

        if (Quantity.HasValue && !ApiRequestValidation.IsPositiveFinancialValue(Quantity))
        {
            yield return new ValidationResult(
                "Quantity must be positive and representable as decimal(28,10).",
                [nameof(Quantity)]);
        }

        if (UnitPrice.HasValue && !ApiRequestValidation.IsPositiveFinancialValue(UnitPrice))
        {
            yield return new ValidationResult(
                "Unit price must be positive and representable as decimal(28,10).",
                [nameof(UnitPrice)]);
        }

        if (ExecutedAt.HasValue && !ApiRequestValidation.IsUtc(ExecutedAt))
        {
            yield return new ValidationResult("executedAt must use the UTC offset.", [nameof(ExecutedAt)]);
        }
    }
}
