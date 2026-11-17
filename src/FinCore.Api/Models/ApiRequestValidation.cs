namespace FinCore.Api.Models;

internal static class ApiRequestValidation
{
    private const decimal MaximumFinancialValue = 999999999999999999.9999999999m;

    internal static bool IsText(string? value, int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;
    }

    internal static bool IsUsd(string? currency)
    {
        return string.Equals(currency?.Trim(), "USD", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPositiveFinancialValue(decimal? value)
    {
        return value is > 0 &&
               value <= MaximumFinancialValue &&
               decimal.Round(value.Value, 10) == value.Value;
    }

    internal static bool IsUtc(DateTimeOffset? value)
    {
        return value.HasValue && value.Value.Offset == TimeSpan.Zero;
    }
}
