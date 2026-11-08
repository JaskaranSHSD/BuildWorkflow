using BuildWorkflow.AICenter;
using BuildWorkflow.AICenter.Connectors;
using BuildWorkflow.AICenter.Tools;
using BuildWorkflow.Services;
using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BuildWorkflow.Filters.MustChangePasswordFilter>();
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Simple cookie authentication; passwords are stored without encryption per current requirement.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "BuildWorkflowAuth";
    });
builder.Services.AddAuthorization();

builder.Services.AddTransient<IAiChatConnector, OllamaConnector>();
builder.Services.AddTransient<IAiChatConnector, OpenAIConnector>();
builder.Services.AddSingleton<WorkflowRunQueue>();
builder.Services.AddSingleton<WorkflowExecutionService>();
builder.Services.AddSingleton<BuildWorkflow.Services.QdrantRagStore>();
builder.Services.AddSingleton<BuildWorkflow.Services.RsaKeyService>();
builder.Services.AddHostedService<WorkflowBackgroundService>();
builder.Services.AddSingleton<AiConnectorFactory>();
builder.Services.AddScoped<AICenterService>();
builder.Services.AddScoped<DatabaseToolExecutor>(serviceProvider =>
    new DatabaseToolExecutor(
        serviceProvider.GetRequiredService<AppDbContext>(),
        serviceProvider.GetRequiredService<BuildWorkflow.AICenter.Helpers.ApiToolHelper>(),
        serviceProvider.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BuildWorkflow.Services.OrganizationContext>();
builder.Services.AddHttpClient<BuildWorkflow.AICenter.Helpers.ApiToolHelper>();

// PostgreSQL via Entity Framework Core.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Ensure the PostgreSQL database and schema exist in development.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Organizations" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Name" character varying(128) NOT NULL,
                "Slug" character varying(128) NULL,
                "DisplayName" text NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FailedLoginAttempts" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockoutEnd" timestamp with time zone NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MustChangePassword" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaSecret" text NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "MfaEnabled" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AllowedFileExtensions" text NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email") WHERE "Email" IS NOT NULL;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "Tools" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "ToolGroups" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "KnowledgeGroups" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "RagDocuments" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            DROP TABLE IF EXISTS "RagChunks";
            ALTER TABLE "UploadedFiles" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "ChatWorkflows" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "ChatSessions" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Tools" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "Tools" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "ToolGroups" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "ToolGroups" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "KnowledgeGroups" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "KnowledgeGroups" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "RagDocuments" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "RagDocuments" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "UploadedFiles" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "UploadedFiles" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "ChatWorkflows" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "ChatWorkflows" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "ChatSessions" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "ChatSessions" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "Description" text NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "SystemInstruction" text NULL;
            ALTER TABLE "WorkflowRuns" DROP COLUMN IF EXISTS "ParentRunId";
            ALTER TABLE "WorkflowRuns" ADD COLUMN IF NOT EXISTS "OutputSummary" text NULL;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "SupportsTools" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "SupportsImages" boolean NOT NULL DEFAULT FALSE;
            CREATE TABLE IF NOT EXISTS "ToolGroups" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Name" character varying(128) NOT NULL,
                "Description" character varying(1024) NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            ALTER TABLE "Tools" ADD COLUMN IF NOT EXISTS "ToolGroupId" uuid NULL;
            CREATE TABLE IF NOT EXISTS "KnowledgeGroups" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "Name" character varying(128) NOT NULL,
                "Description" character varying(1024) NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "KnowledgeGroupId" uuid NULL;
            ALTER TABLE "WorkflowRuns" ADD COLUMN IF NOT EXISTS "ContextSummary" text NULL;
            ALTER TABLE "WorkflowMessages" ADD COLUMN IF NOT EXISTS "WorkflowRunStepId" uuid NULL;
            CREATE TABLE IF NOT EXISTS "WorkflowSteps" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "WorkflowId" uuid NOT NULL,
                "Order" integer NOT NULL,
                "Name" character varying(256) NOT NULL,
                "UserInstruction" text NULL,
                "ModelId" uuid NULL,
                "RagBucketId" uuid NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "WorkflowStepTools" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "WorkflowStepId" uuid NOT NULL,
                "ToolId" uuid NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "WorkflowStepRagBuckets" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "WorkflowStepId" uuid NOT NULL,
                "RagBucketId" uuid NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "WorkflowStepFiles" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "WorkflowStepId" uuid NOT NULL,
                "UploadedFileId" uuid NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "WorkflowRunSteps" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "WorkflowRunId" uuid NOT NULL,
                "WorkflowStepId" uuid NOT NULL,
                "Order" integer NOT NULL,
                "Status" character varying(32) NOT NULL,
                "StartedAt" timestamp with time zone NULL,
                "CompletedAt" timestamp with time zone NULL,
                "Error" text NULL,
                "InputSummary" text NULL,
                "OutputSummary" text NULL,
                "WorkflowSessionId" uuid NULL
            );
            CREATE TABLE IF NOT EXISTS "ToolInvocationLogs" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "ToolId" uuid NULL,
                "ToolName" text NULL,
                "ToolType" text NULL,
                "UserId" uuid NULL,
                "WorkflowRunId" uuid NULL,
                "Status" character varying(32) NOT NULL DEFAULT 'Success',
                "Input" text NULL,
                "Output" text NULL,
                "Error" text NULL,
                "DurationMs" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "ScheduleType" character varying(32) NOT NULL DEFAULT 'Once';
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "DaysOfWeek" text NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "DayOfMonth" integer NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "WeekOfMonth" integer NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "DayOfWeek" integer NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "Hour" integer NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "Minute" integer NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "TimeZoneId" character varying(128) NULL;
            ALTER TABLE "WorkflowSchedules" ADD COLUMN IF NOT EXISTS "EndAt" timestamp with time zone NULL;
        """);

        db.Database.ExecuteSqlRaw("""ALTER TABLE "Organizations" ADD COLUMN IF NOT EXISTS "DisplayName" text NULL; ALTER TABLE "Organizations" ADD COLUMN IF NOT EXISTS "AllowedFileExtensions" text NULL; ALTER TABLE "Organizations" ADD COLUMN IF NOT EXISTS "MaxUploadSizeBytes" bigint NOT NULL DEFAULT 10485760;""");
        var defaultOrg = db.Organizations.OrderBy(o => o.CreatedAt).FirstOrDefault();
        if (defaultOrg == null)
        {
            defaultOrg = new Organization { Name = "Default Organization", Slug = "default", IsActive = true };
            db.Organizations.Add(defaultOrg);
            db.SaveChanges();
        }

        var defaultOrgId = defaultOrg.Id;

        foreach (var user in db.Users.Where(u => u.OrganizationId == null).ToList())
        {
            user.OrganizationId = defaultOrgId;
            if (user.Role == "Admin")
            {
                user.Role = "OrgAdmin";
            }
        }
        db.SaveChanges();

        if (!db.Users.Any())
        {
            db.Users.Add(new AppUser
            {
                Username = "admin",
                Password = "admin",
                Role = "Admin"
            });
            db.SaveChanges();
        }

        if (!db.Users.Any(u => u.Role == "PlatformAdmin"))
        {
            db.Users.Add(new AppUser
            {
                Username = "platformadmin",
                Password = "platformadmin",
                Role = "PlatformAdmin",
                OrganizationId = null
            });
            db.SaveChanges();
        }

        foreach (var legacyWorkflow in db.Workflows.ToList())
        {
            if (db.WorkflowSteps.Any(s => s.WorkflowId == legacyWorkflow.Id))
            {
                continue;
            }

            var step = new WorkflowStep
            {
                WorkflowId = legacyWorkflow.Id,
                Order = 1,
                Name = legacyWorkflow.Name + " Step",
                UserInstruction = legacyWorkflow.UserInstruction,
                ModelId = legacyWorkflow.ModelId,
                RagBucketId = legacyWorkflow.RagBucketId,
                IsActive = legacyWorkflow.IsActive
            };
            db.WorkflowSteps.Add(step);
            db.SaveChanges();

            foreach (var bucket in db.WorkflowRagBuckets.Where(b => b.WorkflowId == legacyWorkflow.Id).ToList())
            {
                db.WorkflowStepRagBuckets.Add(new WorkflowStepRagBucket { WorkflowStepId = step.Id, RagBucketId = bucket.RagBucketId });
            }

            foreach (var file in db.WorkflowFiles.Where(f => f.WorkflowId == legacyWorkflow.Id).ToList())
            {
                db.WorkflowStepFiles.Add(new WorkflowStepFile { WorkflowStepId = step.Id, UploadedFileId = file.UploadedFileId });
            }

            db.SaveChanges();
        }

        if (!db.AiModels.Any())
        {
            db.AiModels.Add(new AiModelDefinition
            {
                Name = "Beam Cloud",
                Provider = "Ollama",
                Endpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434",
                ModelName = builder.Configuration["Ollama:Model"] ?? "beam:cloud",
                SystemPrompt = "You are a helpful AI assistant. Answer concisely and accurately.",
                IsActive = true,
                SupportsTools = true,
                SupportsImages = false,
                OrganizationId = defaultOrgId
            });
            db.SaveChanges();
        }        if (!db.Tools.Any(t => t.Name == "Test Api GET Sample"))
        {
            var getTool = new ToolDefinition
            {
                Name = "Test Api GET Sample",
                Description = "Calls the test GET API with sample data.",
                ToolType = "Api",
                IsActive = true
            };
            db.Tools.Add(getTool);
            db.SaveChanges();
            db.ApiToolConfigurations.Add(new ApiToolConfiguration
            {
                ToolId = getTool.Id,
                HttpMethod = "GET",
                Endpoint = "/api/test/tools/get-sample",
                AuthType = "None"
            });
            db.SaveChanges();
        }

        if (!db.Tools.Any(t => t.Name == "Test Api POST Sample"))
        {
            var postTool = new ToolDefinition
            {
                Name = "Test Api POST Sample",
                Description = "Calls the test POST API with sample data.",
                ToolType = "Api",
                IsActive = true
            };
            db.Tools.Add(postTool);
            db.SaveChanges();
            db.ApiToolConfigurations.Add(new ApiToolConfiguration
            {
                ToolId = postTool.Id,
                HttpMethod = "POST",
                Endpoint = "/api/test/tools/post-sample",
                RequestTemplate = "{\"input\":\"{{input}}\"}",
                AuthType = "None"
            });
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization skipped: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();


