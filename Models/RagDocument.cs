namespace BuildWorkflow.Models;

public class RagDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RagBucketId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? EmbeddingJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
