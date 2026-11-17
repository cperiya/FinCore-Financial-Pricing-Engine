using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the initial portfolio-management relational schema.
    /// </summary>
    public partial class InitialPortfolioPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, nullable: false),
                    InstrumentType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                    table.CheckConstraint("CK_Instruments_Currency_USD", "[Currency] = 'USD'");
                    table.CheckConstraint("CK_Instruments_InstrumentType_Equity", "[InstrumentType] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Portfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => x.Id);
                    table.CheckConstraint("CK_Portfolios_BaseCurrency_USD", "[BaseCurrency] = 'USD'");
                    table.CheckConstraint("CK_Portfolios_CreatedAt_UTC", "DATEPART(TZOFFSET, [CreatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, nullable: false),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPrices", x => x.Id);
                    table.CheckConstraint("CK_MarketPrices_AsOfUtc_UTC", "DATEPART(TZOFFSET, [AsOfUtc]) = 0");
                    table.CheckConstraint("CK_MarketPrices_Currency_USD", "[Currency] = 'USD'");
                    table.CheckConstraint("CK_MarketPrices_Price_Positive", "[Price] > 0");
                    table.ForeignKey(
                        name: "FK_MarketPrices_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.CheckConstraint("CK_Trades_ExecutedAt_UTC", "DATEPART(TZOFFSET, [ExecutedAt]) = 0");
                    table.CheckConstraint("CK_Trades_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_Trades_Side_Supported", "[Side] IN (1, 2)");
                    table.CheckConstraint("CK_Trades_UnitPrice_Positive", "[UnitPrice] > 0");
                    table.ForeignKey(
                        name: "FK_Trades_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Instruments_Symbol",
                table: "Instruments",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketPrices_Instrument_Latest",
                table: "MarketPrices",
                columns: new[] { "InstrumentId", "AsOfUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_InstrumentId",
                table: "Trades",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Portfolio_Instrument_Execution",
                table: "Trades",
                columns: new[] { "PortfolioId", "InstrumentId", "ExecutedAt", "Side", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketPrices");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "Portfolios");
        }
    }
}
