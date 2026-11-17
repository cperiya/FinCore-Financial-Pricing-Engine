using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps instrument market-price history, USD currency, and UTC observation times.
/// </summary>
internal sealed class MarketPriceConfiguration : IEntityTypeConfiguration<MarketPrice>
{
    /// <summary>
    /// Configures positive USD prices, UTC observation times, relationships, and latest-price lookup index.
    /// </summary>
    /// <param name="builder">The EF Core entity builder.</param>
    public void Configure(EntityTypeBuilder<MarketPrice> builder)
    {
        builder.ToTable("MarketPrices", table =>
        {
            table.HasCheckConstraint(
                "CK_MarketPrices_Currency_USD",
                "[Currency] = 'USD'");
            table.HasCheckConstraint(
                "CK_MarketPrices_Price_Positive",
                "[Price] > 0");
            table.HasCheckConstraint(
                "CK_MarketPrices_AsOfUtc_UTC",
                "DATEPART(TZOFFSET, [AsOfUtc]) = 0");
        });

        builder.HasKey(marketPrice => marketPrice.Id);
        builder.Property(marketPrice => marketPrice.Id).ValueGeneratedNever();
        builder.Property(marketPrice => marketPrice.Price)
            .HasPrecision(28, 10)
            .IsRequired();
        builder.Property(marketPrice => marketPrice.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();
        builder.Property(marketPrice => marketPrice.AsOfUtc)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(marketPrice => marketPrice.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(marketPrice => new
            {
                marketPrice.InstrumentId,
                marketPrice.AsOfUtc,
                marketPrice.Id
            })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_MarketPrices_Instrument_Latest");
    }
}
