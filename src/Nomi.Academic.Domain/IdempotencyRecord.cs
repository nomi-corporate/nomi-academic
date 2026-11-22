namespace Nomi.Academic.Domain;

/// <summary>Persisted response for replaying a completed idempotent create operation.</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(Guid ownerUserId, string operation, string key, string requestHash,
        DateTimeOffset createdAt, DateTimeOffset leaseExpiresAt)
    {
        OwnerUserId = ownerUserId;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        Status = "pending";
        CreatedAt = createdAt;
        LeaseExpiresAt = leaseExpiresAt;
    }

    public Guid OwnerUserId { get; private set; }
    public string Operation { get; private set; } = "";
    public string Key { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
    public string Status { get; private set; } = "pending";
    public string? ResponseJson { get; private set; }
    public string? ETag { get; private set; }
    public string? Location { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }

    public void RenewLease(DateTimeOffset expiresAt) => LeaseExpiresAt = expiresAt;

    public void Complete(string responseJson, string etag, string location)
    {
        Status = "completed";
        ResponseJson = responseJson;
        ETag = etag;
        Location = location;
    }
}
