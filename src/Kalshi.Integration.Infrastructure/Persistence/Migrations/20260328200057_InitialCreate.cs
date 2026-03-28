using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kalshi.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isSqlServer = ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

            migrationBuilder.CreateTable(
                name: "OrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    Status = table.Column<string>(type: isSqlServer ? "nvarchar(32)" : "TEXT", maxLength: 32, nullable: false),
                    FilledQuantity = table.Column<int>(type: isSqlServer ? "int" : "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: isSqlServer ? "datetimeoffset" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    TradeIntentId = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    Status = table.Column<string>(type: isSqlServer ? "nvarchar(32)" : "TEXT", maxLength: 32, nullable: false),
                    FilledQuantity = table.Column<int>(type: isSqlServer ? "int" : "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: isSqlServer ? "datetimeoffset" : "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: isSqlServer ? "datetimeoffset" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PositionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    Ticker = table.Column<string>(type: isSqlServer ? "nvarchar(64)" : "TEXT", maxLength: 64, nullable: false),
                    Side = table.Column<string>(type: isSqlServer ? "nvarchar(16)" : "TEXT", maxLength: 16, nullable: false),
                    Contracts = table.Column<int>(type: isSqlServer ? "int" : "INTEGER", nullable: false),
                    AveragePrice = table.Column<decimal>(type: isSqlServer ? "decimal(10,4)" : "TEXT", precision: 10, scale: 4, nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: isSqlServer ? "datetimeoffset" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: isSqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    Ticker = table.Column<string>(type: isSqlServer ? "nvarchar(64)" : "TEXT", maxLength: 64, nullable: false),
                    Side = table.Column<string>(type: isSqlServer ? "nvarchar(16)" : "TEXT", maxLength: 16, nullable: false),
                    Quantity = table.Column<int>(type: isSqlServer ? "int" : "INTEGER", nullable: false),
                    LimitPrice = table.Column<decimal>(type: isSqlServer ? "decimal(10,4)" : "TEXT", precision: 10, scale: 4, nullable: false),
                    StrategyName = table.Column<string>(type: isSqlServer ? "nvarchar(128)" : "TEXT", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: isSqlServer ? "nvarchar(128)" : "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: isSqlServer ? "datetimeoffset" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_OrderId",
                table: "OrderEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradeIntentId",
                table: "Orders",
                column: "TradeIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionSnapshots_Ticker_Side",
                table: "PositionSnapshots",
                columns: new[] { "Ticker", "Side" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderEvents");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PositionSnapshots");

            migrationBuilder.DropTable(
                name: "TradeIntents");
        }
    }
}
