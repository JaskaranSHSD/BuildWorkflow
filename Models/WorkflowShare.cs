using System;

namespace BuildWorkflow.Models;

public class WorkflowShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Guid UserId { get; set; }
    public string Permission { get; set; } = "Run";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
