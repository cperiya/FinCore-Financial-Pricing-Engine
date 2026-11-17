using System.ComponentModel.DataAnnotations;
using FinCore.Domain.Portfolios;

namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// Request to create a USD-denominated portfolio.
/// </summary>
public sealed record CreatePortfolioRequest : IValidatableObject
{
    /// <summary>
    /// Gets the portfolio name.
    /// </summary>
    [Required]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the requested portfolio base currency; only USD is supported.
    /// </summary>
    [Required]
    public string? BaseCurrency { get; init; }

    /// <summary>
    /// Validates the portfolio name and supported base currency.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Validation errors for unsupported values.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && !ApiRequestValidation.IsText(Name, Portfolio.MaximumNameLength))
        {
            yield return new ValidationResult(
                $"A non-blank name of at most {Portfolio.MaximumNameLength} characters is required.",
                [nameof(Name)]);
        }

        if (BaseCurrency is not null && !ApiRequestValidation.IsUsd(BaseCurrency))
        {
            yield return new ValidationResult("Only USD portfolios are supported.", [nameof(BaseCurrency)]);
        }
    }
}
