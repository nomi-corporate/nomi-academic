namespace Nomi.Academic.Domain;

/// <summary>Tenant-scoped relation; the composite foreign keys prevent cross-account links.</summary>
public sealed class SubjectProfessor
{
    private SubjectProfessor() { }

    public SubjectProfessor(Guid ownerUserId, Guid subjectId, Guid professorId)
    {
        OwnerUserId = ownerUserId;
        SubjectId = subjectId;
        ProfessorId = professorId;
    }

    public Guid OwnerUserId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid ProfessorId { get; private set; }
    public Subject? Subject { get; private set; }
    public Professor? Professor { get; private set; }
}
