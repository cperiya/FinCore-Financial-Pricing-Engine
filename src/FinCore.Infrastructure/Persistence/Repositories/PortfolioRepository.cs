using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server-backed portfolio repository.
/// </summary>
internal sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly FinCoreDbContext _dbContext;

    /// <summary>
    /// Initializes a repository using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context that tracks portfolio operations.</param>
    public PortfolioRepository(FinCoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        await _dbContext.Portfolios.AddAsync(portfolio, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Portfolio?> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        return _dbContext.Portfolios
            .AsNoTracking()
            .SingleOrDefaultAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Portfolio>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Portfolios
            .AsNoTracking()
            .OrderByDescending(portfolio => portfolio.CreatedAt)
            .ThenBy(portfolio => portfolio.Id)
            .ToArrayAsync(cancellationToken);
    }
}
