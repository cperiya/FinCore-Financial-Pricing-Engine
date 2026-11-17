using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps portfolio identity, USD currency, and UTC creation time to SQL Server.
/// </summary>
internal sealed class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    /// <summary>
    /// Configures the portfolio table, USD constraint, and UTC timestamp constraint.
    /// </summary>
    /// <param name="builder">The EF Core entity builder.</param>
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        builder.ToTable("Portfolios", table =>
        {
            table.HasCheckConstraint(
                "CK_Portfolios_BaseCurrency_USD",
                "[BaseCurrency] = 'USD'");
            table.HasCheckConstraint(
                "CK_Portfolios_CreatedAt_UTC",
                "DATEPART(TZOFFSET, [CreatedAt]) = 0");
        });

        builder.HasKey(portfolio => portfolio.Id);
        builder.Property(portfolio => portfolio.Id).ValueGeneratedNever();
        builder.Property(portfolio => portfolio.Name)
            .HasMaxLength(Portfolio.MaximumNameLength)
            .IsRequired();
        builder.Property(portfolio => portfolio.BaseCurrency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();
        builder.Property(portfolio => portfolio.CreatedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();
    }
}
