using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines persistence operations for portfolio creation and retrieval.
/// </summary>
public interface IPortfolioRepository
{
    /// <summary>
    /// Adds a portfolio to the current unit of work.
    /// </summary>
    /// <param name="portfolio">The validated portfolio to add.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="portfolio"/> is null.</exception>
    Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a portfolio by its identifier.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The portfolio, or <see langword="null"/> when it does not exist.</returns>
    Task<Portfolio?> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all portfolios in stable creation-time and identifier order.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>Portfolios ordered by creation time descending, then identifier ascending.</returns>
    Task<IReadOnlyList<Portfolio>> GetAllAsync(CancellationToken cancellationToken);
}
