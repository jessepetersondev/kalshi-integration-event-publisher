using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Kalshi.Integration.Executor.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialExecutorReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublisherOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TradeIntentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSourceEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: true),
                    LimitPrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 4, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientOrderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LastResultEventName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TerminalResultQueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TerminalResultPublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutorInboundMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiveAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    HandledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutorInboundMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutorOperationalIssues",
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
                    table.PrimaryKey("PK_ExecutorOperationalIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutorOutboxAttempts",
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
                    table.PrimaryKey("PK_ExecutorOutboxAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutorOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_ExecutorOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalOrderMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublisherOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientOrderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalOrderMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRecords_ClientOrderId",
                table: "ExecutionRecords",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRecords_ExternalOrderId",
                table: "ExecutionRecords",
                column: "ExternalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRecords_PublisherOrderId",
                table: "ExecutionRecords",
                column: "PublisherOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorOperationalIssues_OccurredAt",
                table: "ExecutorOperationalIssues",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorOutboxAttempts_MessageId_AttemptNumber",
                table: "ExecutorOutboxAttempts",
                columns: new[] { "MessageId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorOutboxMessages_ExecutionRecordId",
                table: "ExecutorOutboxMessages",
                column: "ExecutionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorOutboxMessages_Status_NextAttemptAt",
                table: "ExecutorOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOrderMappings_ClientOrderId",
                table: "ExternalOrderMappings",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOrderMappings_ExternalOrderId",
                table: "ExternalOrderMappings",
                column: "ExternalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOrderMappings_PublisherOrderId",
                table: "ExternalOrderMappings",
                column: "PublisherOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionRecords");

            migrationBuilder.DropTable(
                name: "ExecutorInboundMessages");

            migrationBuilder.DropTable(
                name: "ExecutorOperationalIssues");

            migrationBuilder.DropTable(
                name: "ExecutorOutboxAttempts");

            migrationBuilder.DropTable(
                name: "ExecutorOutboxMessages");

            migrationBuilder.DropTable(
                name: "ExternalOrderMappings");
        }
    }
}

#pragma warning restore CA1861
