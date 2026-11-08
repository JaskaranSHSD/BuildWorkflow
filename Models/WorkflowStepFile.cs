namespace BuildWorkflow.Models;

public class WorkflowStepFile
{
    public Guid Id { get; set; }

    public Guid WorkflowStepId { get; set; }

    public Guid UploadedFileId { get; set; }
}