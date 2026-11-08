using System;

namespace BuildWorkflow.Models;

public class WorkflowSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid? UserId { get; set; }
    public bool Enabled { get; set; } = true;

    // Legacy/optional fields kept for backward compatibility.
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public DateTimeOffset? RunAt { get; set; }

    // Advanced recurrence fields.
    public string ScheduleType { get; set; } = "Once"; // Once, Interval, Daily, Weekly, MonthlyDay, MonthlyNth, MonthlyLastDay, Cron
    public string? DaysOfWeek { get; set; }           // comma-separated 0-6 (Sunday=0)
    public int? DayOfMonth { get; set; }              // 1-31
    public int? WeekOfMonth { get; set; }             // 1-5 (5 = last occurrence within month)
    public int? DayOfWeek { get; set; }               // 0-6
    public int? Hour { get; set; }                    // 0-23
    public int? Minute { get; set; }                  // 0-59
    public string? TimeZoneId { get; set; }
    public DateTimeOffset? EndAt { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
