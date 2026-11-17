using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps trades, their positive financial values, UTC execution times, and required parent records.
/// </summary>
internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    /// <summary>
    /// Configures positive trade values, UTC execution times, relationships, and the position-query index.
    /// </summary>
    /// <param name="builder">The EF Core entity builder.</param>
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades", table =>
        {
            table.HasCheckConstraint(
                "CK_Trades_Side_Supported",
                "[Side] IN (1, 2)");
            table.HasCheckConstraint(
                "CK_Trades_Quantity_Positive",
                "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_Trades_UnitPrice_Positive",
                "[UnitPrice] > 0");
            table.HasCheckConstraint(
                "CK_Trades_ExecutedAt_UTC",
                "DATEPART(TZOFFSET, [ExecutedAt]) = 0");
        });

        builder.HasKey(trade => trade.Id);
        builder.Property(trade => trade.Id).ValueGeneratedNever();
        builder.Property(trade => trade.Side)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(trade => trade.Quantity)
            .HasPrecision(28, 10)
            .IsRequired();
        builder.Property(trade => trade.UnitPrice)
            .HasPrecision(28, 10)
            .IsRequired();
        builder.Property(trade => trade.ExecutedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.HasOne<Portfolio>()
            .WithMany()
            .HasForeignKey(trade => trade.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(trade => trade.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(trade => new
            {
                trade.PortfolioId,
                trade.InstrumentId,
                trade.ExecutedAt,
                trade.Side,
                trade.Id
            })
            .HasDatabaseName("IX_Trades_Portfolio_Instrument_Execution");
    }
}
