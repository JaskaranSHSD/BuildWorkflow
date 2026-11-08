using System;
using System.Collections.Generic;

namespace BuildWorkflow.Models;

public class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SystemInstruction { get; set; }
    public string? UserInstruction { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? RagBucketId { get; set; }

    public List<WorkflowStep> Steps { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

}
