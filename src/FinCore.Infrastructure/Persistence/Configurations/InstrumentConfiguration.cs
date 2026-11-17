using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinCore.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps supported equity instruments and their USD currency invariant.
/// </summary>
internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    /// <summary>
    /// Configures the instrument table and supported equity/USD constraints.
    /// </summary>
    /// <param name="builder">The EF Core entity builder.</param>
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("Instruments", table =>
        {
            table.HasCheckConstraint(
                "CK_Instruments_Currency_USD",
                "[Currency] = 'USD'");
            table.HasCheckConstraint(
                "CK_Instruments_InstrumentType_Equity",
                "[InstrumentType] = 1");
        });

        builder.HasKey(instrument => instrument.Id);
        builder.Property(instrument => instrument.Id).ValueGeneratedNever();
        builder.Property(instrument => instrument.Symbol)
            .HasMaxLength(Instrument.MaximumSymbolLength)
            .IsRequired();
        builder.Property(instrument => instrument.Name)
            .HasMaxLength(Instrument.MaximumNameLength)
            .IsRequired();
        builder.Property(instrument => instrument.Currency)
            .HasColumnType("char(3)")
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();
        builder.Property(instrument => instrument.InstrumentType)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(instrument => instrument.Symbol)
            .IsUnique()
            .HasDatabaseName("UX_Instruments_Symbol");
    }
}
