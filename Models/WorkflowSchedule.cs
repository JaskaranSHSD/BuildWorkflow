using System;

namespace BuildWorkflow.Models;

public class WorkflowSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid? UserId { get; set; }
    public bool Enabled { get; set; } = true;
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public DateTimeOffset? RunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
