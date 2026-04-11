using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Orders;

namespace Kalshi.Integration.UnitTests;

public sealed class ApplicationRecordTests
{
    [Fact]
    public void ApplicationEventEnvelope_Create_ShouldPopulateDefaults()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

        ApplicationEventEnvelope envelope = ApplicationEventEnvelope.Create("trading", "order.created");

        DateTimeOffset after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.NotEqual(Guid.Empty, envelope.Id);
        Assert.Equal("trading", envelope.Category);
        Assert.Equal("order.created", envelope.Name);
        Assert.Empty(envelope.Attributes);
        Assert.InRange(envelope.OccurredAt, before, after);
    }

    [Fact]
    public void ApplicationEventEnvelope_Create_ShouldRespectExplicitValues()
    {
        DateTimeOffset occurredAt = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
        Dictionary<string, string?> attributes = new()
        {
            ["ticker"] = "KXBTC",
            ["status"] = "accepted"
        };

        ApplicationEventEnvelope envelope = ApplicationEventEnvelope.Create(
            category: "risk",
            name: "trade-intent.validated",
            resourceId: "resource-1",
            correlationId: "corr-1",
            idempotencyKey: "idem-1",
            attributes: attributes,
            occurredAt: occurredAt);

        Assert.Equal("resource-1", envelope.ResourceId);
        Assert.Equal("corr-1", envelope.CorrelationId);
        Assert.Equal("idem-1", envelope.IdempotencyKey);
        Assert.Same(attributes, envelope.Attributes);
        Assert.Equal(occurredAt, envelope.OccurredAt);
    }

    [Fact]
    public void AuditRecord_Create_ShouldPopulateExpectedFields()
    {
        DateTimeOffset occurredAt = new(2026, 3, 28, 12, 1, 0, TimeSpan.Zero);

        AuditRecord record = AuditRecord.Create(
            category: "trading",
            action: "create-order",
            outcome: "accepted",
            correlationId: "corr-1",
            details: "Order created.",
            idempotencyKey: "idem-1",
            resourceId: "order-1",
            occurredAt: occurredAt);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("trading", record.Category);
        Assert.Equal("create-order", record.Action);
        Assert.Equal("accepted", record.Outcome);
        Assert.Equal("corr-1", record.CorrelationId);
        Assert.Equal("idem-1", record.IdempotencyKey);
        Assert.Equal("order-1", record.ResourceId);
        Assert.Equal("Order created.", record.Details);
        Assert.Equal(occurredAt, record.OccurredAt);
    }

    [Fact]
    public void OperationalIssue_Create_ShouldPopulateExpectedFields()
    {
        DateTimeOffset occurredAt = new(2026, 3, 28, 12, 2, 0, TimeSpan.Zero);

        OperationalIssue issue = OperationalIssue.Create(
            category: "integration",
            severity: "error",
            source: "kalshi-webhook",
            message: "Payload rejected.",
            details: "Missing status.",
            occurredAt: occurredAt);

        Assert.NotEqual(Guid.Empty, issue.Id);
        Assert.Equal("integration", issue.Category);
        Assert.Equal("error", issue.Severity);
        Assert.Equal("kalshi-webhook", issue.Source);
        Assert.Equal("Payload rejected.", issue.Message);
        Assert.Equal("Missing status.", issue.Details);
        Assert.Equal(occurredAt, issue.OccurredAt);
    }

    [Fact]
    public void IdempotencyRecord_Create_ShouldPopulateExpectedFields()
    {
        DateTimeOffset createdAt = new(2026, 3, 28, 12, 3, 0, TimeSpan.Zero);

        IdempotencyRecord record = IdempotencyRecord.Create("orders", "idem-1", "hash-1", 201, "{\"id\":1}", createdAt);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("orders", record.Scope);
        Assert.Equal("idem-1", record.Key);
        Assert.Equal("hash-1", record.RequestHash);
        Assert.Equal(201, record.StatusCode);
        Assert.Equal("{\"id\":1}", record.ResponseBody);
        Assert.Equal(createdAt, record.CreatedAt);
    }

    [Fact]
    public void RiskDecision_ShouldExposeConstructorValues()
    {
        RiskDecision decision = new(false, "rejected", new[] { "reason-1" }, 5, true);

        Assert.False(decision.Accepted);
        Assert.Equal("rejected", decision.Decision);
        Assert.Equal(new[] { "reason-1" }, decision.Reasons);
        Assert.Equal(5, decision.MaxOrderSize);
        Assert.True(decision.DuplicateCorrelationIdDetected);
    }

    [Fact]
    public void ExecutionUpdateResult_ShouldExposeConstructorValues()
    {
        OrderResponse orderResponse = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "KXBTC",
            "yes",
            1,
            0.45m,
            "Breakout",
            "filled",
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new List<OrderEventResponse>());

        ExecutionUpdateResult result = new(orderResponse.Id, "filled", 1, orderResponse.UpdatedAt, orderResponse);

        Assert.Equal(orderResponse.Id, result.OrderId);
        Assert.Equal("filled", result.Status);
        Assert.Equal(1, result.FilledQuantity);
        Assert.Equal(orderResponse.UpdatedAt, result.OccurredAt);
        Assert.Same(orderResponse, result.Order);
    }
}
