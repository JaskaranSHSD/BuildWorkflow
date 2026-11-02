namespace BuildWorkflow.Models;

public class WorkflowRagBucket
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid RagBucketId { get; set; }
}