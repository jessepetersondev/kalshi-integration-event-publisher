using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kalshi.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublisherReliabilityOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_TradeIntentId",
                table: "Orders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliedAt",
                table: "ResultEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApplyAttemptCount",
                table: "ResultEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastApplyAttemptAt",
                table: "ResultEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "ResultEvents",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PublisherOutboxAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublisherOutboxAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublisherOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ProcessorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LastFailureKind = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublisherOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResultEvents_AppliedAt",
                table: "ResultEvents",
                column: "AppliedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClientOrderId",
                table: "Orders",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradeIntentId",
                table: "Orders",
                column: "TradeIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublisherOutboxAttempts_MessageId_AttemptNumber",
                table: "PublisherOutboxAttempts",
                columns: new[] { "MessageId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublisherOutboxMessages_AggregateType_AggregateId",
                table: "PublisherOutboxMessages",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublisherOutboxMessages_Status_NextAttemptAt",
                table: "PublisherOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublisherOutboxAttempts");

            migrationBuilder.DropTable(
                name: "PublisherOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_ResultEvents_AppliedAt",
                table: "ResultEvents");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ClientOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TradeIntentId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "ResultEvents");

            migrationBuilder.DropColumn(
                name: "ApplyAttemptCount",
                table: "ResultEvents");

            migrationBuilder.DropColumn(
                name: "LastApplyAttemptAt",
                table: "ResultEvents");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "ResultEvents");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradeIntentId",
                table: "Orders",
                column: "TradeIntentId");
        }
    }
}
