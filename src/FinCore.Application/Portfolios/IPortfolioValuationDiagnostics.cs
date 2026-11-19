namespace FinCore.Application.Portfolios;

/// <summary>
/// Receives optional diagnostics for the portfolio valuation stages.
/// </summary>
/// <remarks>
/// The default application configuration does not register a diagnostics sink.
/// Implementations must not change valuation results or business rules.
/// </remarks>
public interface IPortfolioValuationDiagnostics
{
    /// <summary>
    /// Begins measuring one valuation stage.
    /// </summary>
    /// <param name="stageName">The stable name of the stage.</param>
    /// <param name="inputCount">The number of logical input items entering the stage.</param>
    /// <returns>A stage scope that must be completed with its output count.</returns>
    IPortfolioValuationDiagnosticStage BeginStage(string stageName, int inputCount);
}

/// <summary>
/// Represents one optional portfolio valuation diagnostic stage.
/// </summary>
public interface IPortfolioValuationDiagnosticStage : IDisposable
{
    /// <summary>
    /// Completes the stage with its logical output count.
    /// </summary>
    /// <param name="outputCount">The number of logical output items produced.</param>
    void Complete(int outputCount);
}
