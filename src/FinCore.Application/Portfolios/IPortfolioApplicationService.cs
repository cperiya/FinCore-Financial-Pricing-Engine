using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines portfolio creation, retrieval, listing, and derived-position use cases.
/// </summary>
public interface IPortfolioApplicationService
{
    /// <summary>
    /// Creates a USD portfolio using the current UTC time.
    /// </summary>
    /// <param name="name">The portfolio name.</param>
    /// <param name="cancellationToken">A token that can cancel persistence.</param>
    /// <returns>The created portfolio.</returns>
    Task<Portfolio> CreatePortfolioAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a portfolio by identifier.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the query.</param>
    /// <returns>The portfolio or <see langword="null"/> when it does not exist.</returns>
    Task<Portfolio?> GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists portfolios in stable creation-time and identifier order.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the query.</param>
    /// <returns>Portfolios ordered by creation time descending, then identifier ascending.</returns>
    Task<IReadOnlyList<Portfolio>> ListPortfoliosAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calculates positions for an existing portfolio from its complete trade history.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the queries.</param>
    /// <returns>Positive holdings; an existing portfolio with no trades has no positions.</returns>
    /// <exception cref="PortfolioNotFoundException">Thrown when the portfolio does not exist.</exception>
    Task<IReadOnlyList<Position>> GetPositionsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken);
}
