using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Implements portfolio lifecycle and trade-derived position use cases.
/// </summary>
public sealed class PortfolioApplicationService : IPortfolioApplicationService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PositionCalculationService _positionCalculationService;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes portfolio use cases with persistence, position calculation, and time dependencies.
    /// </summary>
    /// <param name="portfolios">Portfolio persistence operations.</param>
    /// <param name="trades">Trade-history persistence operations.</param>
    /// <param name="unitOfWork">Persistence commit operations.</param>
    /// <param name="positionCalculationService">The Domain position calculator.</param>
    /// <param name="timeProvider">The UTC clock used for portfolio creation timestamps.</param>
    public PortfolioApplicationService(
        IPortfolioRepository portfolios,
        ITradeRepository trades,
        IUnitOfWork unitOfWork,
        PositionCalculationService positionCalculationService,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(portfolios);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(positionCalculationService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _portfolios = portfolios;
        _trades = trades;
        _unitOfWork = unitOfWork;
        _positionCalculationService = positionCalculationService;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<Portfolio> CreatePortfolioAsync(string name, CancellationToken cancellationToken)
    {
        Portfolio portfolio = new(Guid.NewGuid(), name, "USD", _timeProvider.GetUtcNow());
        await _portfolios.AddAsync(portfolio, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return portfolio;
    }

    /// <inheritdoc />
    public Task<Portfolio?> GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        return _portfolios.GetByIdAsync(portfolioId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Portfolio>> ListPortfoliosAsync(CancellationToken cancellationToken)
    {
        return _portfolios.GetAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Position>> GetPositionsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        if (await _portfolios.GetByIdAsync(portfolioId, cancellationToken) is null)
        {
            throw new PortfolioNotFoundException(portfolioId);
        }

        IReadOnlyList<Trade> trades =
            await _trades.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        return _positionCalculationService.CalculatePositions(trades);
    }
}
