namespace BuildWorkflow.Models;

public class UserRagBucket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public Guid RagBucketId { get; set; }

    public RagBucket RagBucket { get; set; } = null!;
}
