using System;

namespace BuildWorkflow.Models;

public class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid? UserId { get; set; }
    public string Status { get; set; } = "Queued"; // Queued, Running, Completed, Failed
    public Guid? ModelId { get; set; }
    public Guid? WorkflowSessionId { get; set; }
    public string? OutputSummary { get; set; }
    public string? ContextSummary { get; set; }
    public List<WorkflowRunStep> RunSteps { get; set; } = new();
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
