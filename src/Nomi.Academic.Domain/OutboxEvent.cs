namespace Nomi.Academic.Domain;

// Stored with the aggregate so a future publisher can deliver it reliably.
public sealed class OutboxEvent
{
    private OutboxEvent() { }

    public OutboxEvent(Guid id, string eventType, Guid aggregateId, int aggregateVersion, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = "";
    public Guid AggregateId { get; private set; }
    public int AggregateVersion { get; private set; }
    public string Payload { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
}
