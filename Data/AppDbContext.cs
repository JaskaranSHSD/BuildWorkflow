using BuildWorkflow.Models;
using BuildWorkflow.Services;
using Microsoft.EntityFrameworkCore;

namespace BuildWorkflow.Data;

public class AppDbContext : DbContext
{
    private readonly OrganizationContext _organizationContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, OrganizationContext organizationContext) : base(options)
    {
        _organizationContext = organizationContext;
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<AiModelDefinition> AiModels => Set<AiModelDefinition>();

    public DbSet<ToolDefinition> Tools => Set<ToolDefinition>();

    public DbSet<ToolGroup> ToolGroups => Set<ToolGroup>();

    public DbSet<ApiToolConfiguration> ApiToolConfigurations => Set<ApiToolConfiguration>();

    public DbSet<SqlToolConfiguration> SqlToolConfigurations => Set<SqlToolConfiguration>();
    public DbSet<SftpToolConfiguration> SftpToolConfigurations => Set<SftpToolConfiguration>();

    public DbSet<RagBucket> RagBuckets => Set<RagBucket>();

    public DbSet<KnowledgeGroup> KnowledgeGroups => Set<KnowledgeGroup>();
    public DbSet<RagDocument> RagDocuments => Set<RagDocument>();

    public DbSet<UserToolPermission> UserToolPermissions => Set<UserToolPermission>();

    public DbSet<UserRagBucket> UserRagBuckets => Set<UserRagBucket>();

    public DbSet<UserAiModel> UserAiModels => Set<UserAiModel>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessageRecord> ChatMessages => Set<ChatMessageRecord>();
    public DbSet<ChatWorkflow> ChatWorkflows => Set<ChatWorkflow>();
    public DbSet<ChatWorkflowStep> ChatWorkflowSteps => Set<ChatWorkflowStep>();
    public DbSet<ChatWorkflowFile> ChatWorkflowFiles => Set<ChatWorkflowFile>();

    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();
    public DbSet<WorkflowFile> WorkflowFiles => Set<WorkflowFile>();
    public DbSet<WorkflowRagBucket> WorkflowRagBuckets => Set<WorkflowRagBucket>();
    public DbSet<WorkflowSession> WorkflowSessions => Set<WorkflowSession>();
    public DbSet<WorkflowMessage> WorkflowMessages => Set<WorkflowMessage>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowRunStep> WorkflowRunSteps => Set<WorkflowRunStep>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowStepTool> WorkflowStepTools => Set<WorkflowStepTool>();
    public DbSet<WorkflowStepRagBucket> WorkflowStepRagBuckets => Set<WorkflowStepRagBucket>();
    public DbSet<WorkflowStepFile> WorkflowStepFiles => Set<WorkflowStepFile>();
    public DbSet<WorkflowSchedule> WorkflowSchedules => Set<WorkflowSchedule>();
    public DbSet<ToolInvocationLog> ToolInvocationLogs => Set<ToolInvocationLog>();

    public DbSet<WorkflowShare> WorkflowShares => Set<WorkflowShare>();

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(128);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
            entity.Property(e => e.Username).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Password).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AiModelDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Endpoint).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ModelName).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<ToolDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.ToolType).HasMaxLength(64).IsRequired();
            entity.HasOne(e => e.ToolGroup).WithMany().HasForeignKey(e => e.ToolGroupId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ToolGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<ApiToolConfiguration>(entity =>
        {
            entity.HasKey(e => e.ToolId);
            entity.HasOne(e => e.Tool).WithOne(e => e.ApiConfiguration).HasForeignKey<ApiToolConfiguration>(e => e.ToolId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.HttpMethod).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Endpoint).HasMaxLength(1024).IsRequired();
        });

        modelBuilder.Entity<SqlToolConfiguration>(entity =>
        {
            entity.HasKey(e => e.ToolId);
            entity.HasOne(e => e.Tool).WithOne(e => e.SqlConfiguration).HasForeignKey<SqlToolConfiguration>(e => e.ToolId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.DatabaseType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ConnectionString).HasMaxLength(1024).IsRequired();
        });

        modelBuilder.Entity<KnowledgeGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<RagBucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.StoreType).HasMaxLength(64).IsRequired();
            entity.HasOne(e => e.KnowledgeGroup).WithMany().HasForeignKey(e => e.KnowledgeGroupId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserToolPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Tool).WithMany().HasForeignKey(e => e.ToolId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ToolId }).IsUnique();
        });

        modelBuilder.Entity<UploadedFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionKey).HasMaxLength(256).IsRequired();
            entity.Property(e => e.OriginalFileName).HasMaxLength(512).IsRequired();
            entity.Property(e => e.StoredPath).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<UserAiModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AiModel).WithMany().HasForeignKey(e => e.AiModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.AiModelId }).IsUnique();
        });

        modelBuilder.Entity<UserRagBucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.RagBucket).WithMany().HasForeignKey(e => e.RagBucketId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.RagBucketId }).IsUnique();
        });

        modelBuilder.Entity<RagDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.HasIndex(e => e.RagBucketId);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<SftpToolConfiguration>(entity =>
        {
            entity.HasKey(e => e.ToolId);
            entity.HasOne(e => e.Tool).WithOne(e => e.SftpConfiguration).HasForeignKey<SftpToolConfiguration>(e => e.ToolId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Host).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<ChatWorkflow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.HasMany(e => e.Steps).WithOne().HasForeignKey(s => s.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatWorkflowStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserMessage).IsRequired();
        });

        modelBuilder.Entity<ChatWorkflowFile>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<WorkflowMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Content).IsRequired();
        });

        modelBuilder.Entity<WorkflowSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ToolInvocationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WorkflowRagBucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.HasOne<WorkflowDefinition>().WithMany(w => w.Steps).HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.WorkflowId, e.Order });
        });

        modelBuilder.Entity<WorkflowStepTool>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowStep>().WithMany().HasForeignKey(e => e.WorkflowStepId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStepRagBucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowStep>().WithMany().HasForeignKey(e => e.WorkflowStepId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStepFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowStep>().WithMany().HasForeignKey(e => e.WorkflowStepId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowRunStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.HasOne<WorkflowRun>().WithMany(r => r.RunSteps).HasForeignKey(e => e.WorkflowRunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(e => e.WorkflowId).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Messages).WithOne().HasForeignKey(m => m.WorkflowSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<AppUser>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Messages)
                .WithOne(e => e.ChatSession)
                .HasForeignKey(e => e.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessageRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Content).IsRequired();
        });

        ApplyTenantQueryFilters(modelBuilder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<AiModelDefinition>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<ToolDefinition>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<ToolGroup>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<RagBucket>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<KnowledgeGroup>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);

        modelBuilder.Entity<WorkflowDefinition>().HasQueryFilter(e =>
            _organizationContext.IsPlatformAdmin ||
            _organizationContext.OrganizationId == null ||
            e.OrganizationId == _organizationContext.OrganizationId);
    }

    public override int SaveChanges()
    {
        StampTenantOwnedEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantOwnedEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTenantOwnedEntities()
    {
        if (_organizationContext.OrganizationId is not Guid organizationId)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case AppUser u when u.OrganizationId == null:
                    u.OrganizationId = organizationId;
                    break;
                case AiModelDefinition m when m.OrganizationId == null:
                    m.OrganizationId = organizationId;
                    break;
                case ToolDefinition t when t.OrganizationId == null:
                    t.OrganizationId = organizationId;
                    break;
                case ToolGroup g when g.OrganizationId == null:
                    g.OrganizationId = organizationId;
                    break;
                case RagBucket b when b.OrganizationId == null:
                    b.OrganizationId = organizationId;
                    break;
                case KnowledgeGroup k when k.OrganizationId == null:
                    k.OrganizationId = organizationId;
                    break;
                case WorkflowDefinition w when w.OrganizationId == null:
                    w.OrganizationId = organizationId;
                    break;
            }
        }
    }
}