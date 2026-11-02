using System;

namespace BuildWorkflow.Models;

public class WorkflowTool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid ToolId { get; set; }
}
