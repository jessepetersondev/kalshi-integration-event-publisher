using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kalshi.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Side",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "TradeIntents",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "LimitPrice",
                table: "TradeIntents",
                type: "TEXT",
                precision: 10,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommandSchemaVersion",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OriginService",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetClientOrderId",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetExternalOrderId",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPositionSide",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPositionTicker",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetPublisherOrderId",
                table: "TradeIntents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientOrderId",
                table: "Orders",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommandEventId",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                table: "Orders",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastResultMessage",
                table: "Orders",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastResultStatus",
                table: "Orders",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishStatus",
                table: "Orders",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseBody = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLifecycleEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResultEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ExternalOrderId",
                table: "Orders",
                column: "ExternalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_OccurredAt",
                table: "AuditRecords",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIssues_OccurredAt",
                table: "OperationalIssues",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLifecycleEvents_OrderId",
                table: "OrderLifecycleEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ResultEvents_OrderId",
                table: "ResultEvents",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "OperationalIssues");

            migrationBuilder.DropTable(
                name: "OrderLifecycleEvents");

            migrationBuilder.DropTable(
                name: "ResultEvents");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ActionType",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "CommandSchemaVersion",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "OriginService",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "TargetClientOrderId",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "TargetExternalOrderId",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "TargetPositionSide",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "TargetPositionTicker",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "TargetPublisherOrderId",
                table: "TradeIntents");

            migrationBuilder.DropColumn(
                name: "ClientOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CommandEventId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastResultMessage",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastResultStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PublishStatus",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Side",
                table: "TradeIntents",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "TradeIntents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "LimitPrice",
                table: "TradeIntents",
                type: "TEXT",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 4,
                oldNullable: true);
        }
    }
}
