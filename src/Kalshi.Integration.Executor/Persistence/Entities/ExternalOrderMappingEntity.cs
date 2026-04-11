namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExternalOrderMappingEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionRecordId { get; set; }
    public Guid PublisherOrderId { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
