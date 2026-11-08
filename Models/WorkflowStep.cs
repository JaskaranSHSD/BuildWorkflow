namespace BuildWorkflow.Models;

public class WorkflowStep
{
    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? UserInstruction { get; set; }

    public Guid? ModelId { get; set; }

    public Guid? RagBucketId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}