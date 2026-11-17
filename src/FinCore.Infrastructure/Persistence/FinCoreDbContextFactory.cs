using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinCore.Infrastructure.Persistence;

/// <summary>
/// Creates the SQL Server context for EF Core design-time migration commands.
/// </summary>
public sealed class FinCoreDbContextFactory : IDesignTimeDbContextFactory<FinCoreDbContext>
{
    /// <summary>
    /// Creates a context using the connection string supplied through the
    /// <c>ConnectionStrings__FinCore</c> environment variable.
    /// </summary>
    /// <param name="args">Arguments supplied by the EF Core design-time tool.</param>
    /// <returns>A configured SQL Server context.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no non-empty connection string is configured.
    /// </exception>
    public FinCoreDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FinCore");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__FinCore in the process environment before running EF Core tooling.");
        }

        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name))
            .Options;

        return new FinCoreDbContext(options);
    }
}
