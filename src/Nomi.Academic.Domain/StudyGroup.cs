namespace Nomi.Academic.Domain;

public sealed class StudyGroup
{
    private StudyGroup() { }
    public StudyGroup(Guid ownerUserId, string name, string? description, DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || description?.Length > 2000)
            throw new ArgumentException("Invalid group.");
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Active = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public bool Active { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

public sealed class StudyGroupMembership
{
    private StudyGroupMembership() { }
    public StudyGroupMembership(Guid groupId, Guid userId, string role)
    {
        if (groupId == Guid.Empty || userId == Guid.Empty || role is not ("owner" or "admin" or "member"))
            throw new ArgumentException("Invalid group membership.");
        GroupId = groupId;
        UserId = userId;
        Role = role;
        Active = true;
    }

    public Guid GroupId { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = "member";
    public bool Active { get; private set; }
    public int Version { get; private set; }
    public StudyGroup Group { get; private set; } = null!;
}
