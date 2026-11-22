namespace Nomi.Academic.Domain;

/// <summary>Subject owned by one authenticated Nomi account.</summary>
public sealed class Subject
{
    private Subject() { }

    public Subject(Guid ownerUserId, string name, string? code, string? colorToken, string? iconKey, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name;
        Code = code;
        ColorToken = colorToken;
        IconKey = iconKey;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Code { get; private set; }
    public string? ColorToken { get; private set; }
    public string? IconKey { get; private set; }
    public bool Archived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public ICollection<SubjectProfessor> ProfessorLinks { get; private set; } = new List<SubjectProfessor>();

    public void Update(string? name, string? code, string? colorToken, string? iconKey, DateTimeOffset now)
    {
        if (name is not null) Name = name;
        Code = code;
        ColorToken = colorToken;
        IconKey = iconKey;
        UpdatedAt = now;
        Version++;
    }

    public void SetArchived(bool archived, DateTimeOffset now)
    {
        Archived = archived;
        UpdatedAt = now;
        Version++;
    }

    public void MarkProfessorLinksChanged(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
