using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.Models.Instruments;

/// <summary>
/// Request to record a simulated USD market price.
/// </summary>
public sealed record RecordMarketPriceRequest : IValidatableObject
{
    /// <summary>
    /// Gets the positive USD market price.
    /// </summary>
    [Required]
    public decimal? Price { get; init; }

    /// <summary>
    /// Gets the requested price currency; only USD is supported.
    /// </summary>
    [Required]
    public string? Currency { get; init; }

    /// <summary>
    /// Gets the UTC time at which the price applies.
    /// </summary>
    [Required]
    public DateTimeOffset? AsOfUtc { get; init; }

    /// <summary>
    /// Validates price, currency, and UTC observation time.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Validation errors for unsupported values.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Price.HasValue && !ApiRequestValidation.IsPositiveFinancialValue(Price))
        {
            yield return new ValidationResult(
                "Price must be positive and representable as decimal(28,10).",
                [nameof(Price)]);
        }

        if (Currency is not null && !ApiRequestValidation.IsUsd(Currency))
        {
            yield return new ValidationResult("Only USD prices are supported.", [nameof(Currency)]);
        }

        if (AsOfUtc.HasValue && !ApiRequestValidation.IsUtc(AsOfUtc))
        {
            yield return new ValidationResult("asOfUtc must use the UTC offset.", [nameof(AsOfUtc)]);
        }
    }
}
