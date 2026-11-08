using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace BuildWorkflow.Services;

public class WorkflowBackgroundService : BackgroundService
{
    private readonly WorkflowRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowExecutionService _runner;
    private readonly ILogger<WorkflowBackgroundService> _logger;

    public WorkflowBackgroundService(WorkflowRunQueue queue, IServiceScopeFactory scopeFactory, WorkflowExecutionService runner, ILogger<WorkflowBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load queued runs from DB
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queued = await db.WorkflowRuns.Where(r => r.Status == "Queued").Select(r => r.Id).ToListAsync(stoppingToken);
            foreach (var id in queued) _queue.Enqueue(id);
        }

        var processTask = Task.Run(async () =>
        {
            await foreach (var runId in _queue.DequeueAll(stoppingToken))
            {
                try { await _runner.RunAsync(runId, stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Unhandled workflow run error"); }
            }
        }, stoppingToken);

        var schedulerTask = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTimeOffset.UtcNow;
                    var due = await db.WorkflowSchedules.Where(s => s.Enabled && s.NextRunAt != null && s.NextRunAt <= now).ToListAsync(stoppingToken);
                    foreach (var schedule in due)
                    {
                        var workflow = await db.Workflows.FirstOrDefaultAsync(w => w.Id == schedule.WorkflowId && w.IsActive, stoppingToken);
                        if (workflow == null) { schedule.Enabled = false; continue; }
                        var run = new WorkflowRun { WorkflowId = workflow.Id, UserId = schedule.UserId, Status = "Queued", ModelId = workflow.ModelId };
                        db.WorkflowRuns.Add(run);
                        await db.SaveChangesAsync(stoppingToken);
                        _queue.Enqueue(run.Id);

                        if (schedule.ScheduleType == "Once" && schedule.IntervalMinutes.HasValue)
                            schedule.ScheduleType = "Interval";
                        schedule.LastRunAt = now;
                        var next = RecurrenceCalculator.GetNextRun(schedule, now);
                        if (next.HasValue)
                        {
                            schedule.NextRunAt = next.Value;
                        }
                        else
                        {
                            // one-off or exhausted recurrence
                            schedule.Enabled = schedule.ScheduleType == "Interval"; // keep interval schedules alive? disable one-offs
                            if (schedule.ScheduleType == "Once") schedule.Enabled = false;
                            schedule.NextRunAt = schedule.Enabled ? RecurrenceCalculator.GetNextRun(schedule, now) : null;
                        }
                        if (schedule.ScheduleType == "Once") { schedule.Enabled = false; schedule.NextRunAt = null; }
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Scheduler error"); }
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }, stoppingToken);

        await Task.WhenAll(processTask, schedulerTask);
    }
}
