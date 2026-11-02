namespace BuildWorkflow.Models;

public class RagBucket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string StoreType { get; set; } = "PostgreSQL";

    public string? ConnectionInfo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? KnowledgeGroupId { get; set; }

    public KnowledgeGroup? KnowledgeGroup { get; set; }
}
