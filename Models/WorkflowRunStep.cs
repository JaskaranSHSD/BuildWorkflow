namespace BuildWorkflow.Models;

public class WorkflowRunStep
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public Guid WorkflowStepId { get; set; }

    public int Order { get; set; }

    public string Status { get; set; } = "Queued";

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }

    public string? InputSummary { get; set; }

    public string? OutputSummary { get; set; }

    public Guid? WorkflowSessionId { get; set; }
}