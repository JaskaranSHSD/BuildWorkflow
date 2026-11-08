using System;

namespace BuildWorkflow.Models;

public class ToolInvocationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ToolId { get; set; }
    public string? ToolName { get; set; }
    public string? ToolType { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public string Status { get; set; } = "Success";
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public int? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
