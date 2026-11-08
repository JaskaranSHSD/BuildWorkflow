using System;

namespace BuildWorkflow.Models;

public class WorkflowMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowSessionId { get; set; }
    public Guid? WorkflowRunStepId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
