namespace BuildWorkflow.Models;

public class RagChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RagBucketId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public Guid RagDocumentId { get; set; }

    public string Content { get; set; } = string.Empty;

    public double[]? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}