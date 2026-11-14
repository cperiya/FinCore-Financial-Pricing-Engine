using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FinCore.Api.Models;

/// <summary>
/// Query parameters used to value a stock position.
/// </summary>
public sealed record StockValuationRequest : IValidatableObject
{
    /// <summary>
    /// Gets the required, non-blank stock symbol.
    /// </summary>
    [FromQuery(Name = "symbol")]
    [BindRequired]
    public string? Symbol { get; init; }

    /// <summary>
    /// Gets the positive price of one share, in the caller's currency.
    /// </summary>
    [FromQuery(Name = "unitPrice")]
    [BindRequired]
    public decimal? UnitPrice { get; init; }

    /// <summary>
    /// Gets the positive number of shares; fractional quantities are supported.
    /// </summary>
    [FromQuery(Name = "quantity")]
    [BindRequired]
    public decimal? Quantity { get; init; }

    /// <summary>
    /// Validates that the symbol is non-blank and numeric inputs are positive.
    /// </summary>
    /// <param name="validationContext">The context supplied by the validation framework.</param>
    /// <returns>Validation errors for missing or invalid values, or an empty sequence when valid.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
        {
            yield return new ValidationResult(
                "A non-empty stock symbol is required.",
                [nameof(Symbol)]);
        }

        if (UnitPrice is <= 0)
        {
            yield return new ValidationResult(
                "Unit price must be greater than zero.",
                [nameof(UnitPrice)]);
        }

        if (Quantity is <= 0)
        {
            yield return new ValidationResult(
                "Quantity must be greater than zero.",
                [nameof(Quantity)]);
        }
    }
}
