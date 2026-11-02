namespace BuildWorkflow.Models;

public class ChatWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? RagBucketId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChatWorkflowStep> Steps { get; set; } = new();
}
