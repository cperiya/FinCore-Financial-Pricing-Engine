using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FinCore.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the persisted FinCore portfolio-management model.
/// </summary>
public sealed class FinCoreDbContext : DbContext
{
    private const int PersistedDecimalScale = 10;
    private const decimal MaximumPersistedDecimal = 999999999999999999.9999999999m;

    /// <summary>
    /// Initializes a context using the configured SQL Server options.
    /// </summary>
    /// <param name="options">Provider and database options for this context.</param>
    public FinCoreDbContext(DbContextOptions<FinCoreDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the persisted portfolios.
    /// </summary>
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();

    /// <summary>
    /// Gets the registered equity instruments.
    /// </summary>
    public DbSet<Instrument> Instruments => Set<Instrument>();

    /// <summary>
    /// Gets the persisted portfolio trades.
    /// </summary>
    public DbSet<Trade> Trades => Set<Trade>();

    /// <summary>
    /// Gets the recorded simulated market prices.
    /// </summary>
    public DbSet<MarketPrice> MarketPrices => Set<MarketPrice>();

    /// <summary>
    /// Saves tracked changes after checking financial values against their SQL decimal columns.
    /// </summary>
    /// <returns>The number of state entries written to the data store.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a trade or market-price value cannot be stored as <c>decimal(28,10)</c> without rounding.
    /// </exception>
    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    /// <summary>
    /// Saves tracked changes after checking financial values against their SQL decimal columns.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">Whether EF Core accepts changes after a successful write.</param>
    /// <returns>The number of state entries written to the data store.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a trade or market-price value cannot be stored as <c>decimal(28,10)</c> without rounding.
    /// </exception>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateFinancialPrecision();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Asynchronously saves tracked changes after validating persisted financial precision.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the database operation.</param>
    /// <returns>A task containing the number of state entries written to the data store.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a trade or market-price value cannot be stored as <c>decimal(28,10)</c> without rounding.
    /// </exception>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves tracked changes after validating persisted financial precision.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">Whether EF Core accepts changes after a successful write.</param>
    /// <param name="cancellationToken">A token that can cancel the database operation.</param>
    /// <returns>A task containing the number of state entries written to the data store.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a trade or market-price value cannot be stored as <c>decimal(28,10)</c> without rounding.
    /// </exception>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateFinancialPrecision();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Applies the explicit entity mappings used by the runtime model and migrations.
    /// </summary>
    /// <param name="modelBuilder">The model builder supplied by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new PortfolioConfiguration());
        modelBuilder.ApplyConfiguration(new InstrumentConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
        modelBuilder.ApplyConfiguration(new MarketPriceConfiguration());
    }

    private void ValidateFinancialPrecision()
    {
        foreach (EntityEntry<Trade> entry in ChangeTracker.Entries<Trade>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            ValidatePersistedDecimal(entry.Entity.Quantity, nameof(Trade.Quantity));
            ValidatePersistedDecimal(entry.Entity.UnitPrice, nameof(Trade.UnitPrice));
        }

        foreach (EntityEntry<MarketPrice> entry in ChangeTracker.Entries<MarketPrice>())
        {
            if (entry.State is not EntityState.Added and not EntityState.Modified)
            {
                continue;
            }

            ValidatePersistedDecimal(entry.Entity.Price, nameof(MarketPrice.Price));
        }
    }

    private static void ValidatePersistedDecimal(decimal value, string propertyName)
    {
        if (value > MaximumPersistedDecimal ||
            value < -MaximumPersistedDecimal ||
            decimal.Round(value, PersistedDecimalScale) != value)
        {
            throw new InvalidOperationException(
                $"{propertyName} must fit decimal(28,{PersistedDecimalScale}) without rounding.");
        }
    }
}
