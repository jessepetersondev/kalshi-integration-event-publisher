using System.Text.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Reliability;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Executor.Persistence.Entities;

namespace Kalshi.Integration.Executor.Messaging;

public sealed class DeadLetterEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void QueueAsync(ExecutorDbContext dbContext, Guid executionRecordId, ApplicationEventEnvelope envelope, DateTimeOffset now)
    {
        if (dbContext.OutboxMessages.Any(x => x.Id == envelope.Id))
        {
            return;
        }

        dbContext.OutboxMessages.Add(new ExecutorOutboxMessageEntity
        {
            Id = envelope.Id,
            ExecutionRecordId = executionRecordId,
            MessageType = "dead-letter",
            PayloadJson = JsonSerializer.Serialize(envelope, SerializerOptions),
            Status = OutboxMessageStatus.Pending.ToString(),
            AttemptCount = 0,
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }
}
