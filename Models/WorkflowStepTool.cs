namespace BuildWorkflow.Models;

public class WorkflowStepTool
{
    public Guid Id { get; set; }

    public Guid WorkflowStepId { get; set; }

    public Guid ToolId { get; set; }
}