namespace Nomi.Academic.Domain;

/// <summary>A student's private contact record for a professor.</summary>
public sealed class Professor
{
    private Professor() { }

    public Professor(Guid ownerUserId, string name, string? email, string? office, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name;
        Email = email;
        Office = office;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Office { get; private set; }
    public bool Archived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public void Update(string name, string? email, string? office, DateTimeOffset now)
    {
        Name = name;
        Email = email;
        Office = office;
        UpdatedAt = now;
        Version++;
    }

    public void SetArchived(bool archived, DateTimeOffset now)
    {
        if (Archived == archived) return;
        Archived = archived;
        UpdatedAt = now;
        Version++;
    }
}
