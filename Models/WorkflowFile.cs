using System;

namespace BuildWorkflow.Models;

public class WorkflowFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid UploadedFileId { get; set; }
}
