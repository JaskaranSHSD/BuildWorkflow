namespace BuildWorkflow.Models;

public class WorkflowStepRagBucket
{
    public Guid Id { get; set; }

    public Guid WorkflowStepId { get; set; }

    public Guid RagBucketId { get; set; }
}