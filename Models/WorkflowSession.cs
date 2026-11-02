using System;

namespace BuildWorkflow.Models;

public class WorkflowSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WorkflowId { get; set; }
    public Guid? UserId { get; set; }
    public string Title { get; set; } = "Workflow run";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<WorkflowMessage> Messages { get; set; } = new List<WorkflowMessage>();
}
