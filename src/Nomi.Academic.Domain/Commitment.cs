namespace Nomi.Academic.Domain;

// Persisted personal commitments owned by an authenticated Nomi account.
public sealed class Commitment
{
    private Commitment() { }
    public Commitment(Guid ownerUserId, string title, string kind, Guid? subjectId, DateOnly localDate,
        string timeZone, DateTimeOffset now, DateTime? dueInstant = null, string? description = null)
    {
        if (ownerUserId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160
            || kind is not ("exam" or "assignment" or "reminder") || description?.Length > 4000
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _)
            || dueInstant is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("Invalid commitment.");
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Title = title.Trim();
        Kind = kind;
        SubjectId = subjectId;
        LocalDate = localDate;
        TimeZone = timeZone;
        DueInstant = dueInstant;
        Description = description;
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Title { get; private set; } = "";
    public string Kind { get; private set; } = "reminder";
    public string? Description { get; private set; }
    public Guid? SubjectId { get; private set; }
    public Subject? Subject { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public string TimeZone { get; private set; } = "UTC";
    public DateTime? DueInstant { get; private set; }
    public string Status { get; private set; } = "pending";
    public bool Deleted { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
