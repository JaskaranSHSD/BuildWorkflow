using BuildWorkflow.AICenter;
using BuildWorkflow.AICenter.Connectors;
using BuildWorkflow.AICenter.Tools;
using BuildWorkflow.Services;
using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BuildWorkflow.Filters.MustChangePasswordFilter>();
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "BuildWorkflowAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();

// Rate limiting is built into .NET 8+, add the middleware
builder.Services.AddRateLimiter(_ => { });

builder.Services.AddTransient<IAiChatConnector, OllamaConnector>();
builder.Services.AddTransient<IAiChatConnector, OpenAIConnector>();
builder.Services.AddSingleton<WorkflowRunQueue>();
builder.Services.AddSingleton<WorkflowExecutionService>();
builder.Services.AddSingleton<BuildWorkflow.Services.QdrantRagStore>();
builder.Services.AddSingleton<BuildWorkflow.Services.SecretEncryptionService>();
builder.Services.AddSingleton<BuildWorkflow.Services.RsaKeyService>();
builder.Services.AddHostedService<WorkflowBackgroundService>();
builder.Services.AddSingleton<AiConnectorFactory>();
builder.Services.AddScoped<AICenterService>();
builder.Services.AddScoped<DatabaseToolExecutor>(serviceProvider =>
    new DatabaseToolExecutor(
        serviceProvider.GetRequiredService<AppDbContext>(),
        serviceProvider.GetRequiredService<BuildWorkflow.AICenter.Helpers.ApiToolHelper>(),
        serviceProvider.GetRequiredService<IWebHostEnvironment>(),
        serviceProvider.GetService<ILogger<BuildWorkflow.AICenter.Tools.DatabaseToolExecutor>>()));
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
        var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
        var maintConnStr = connStr.Replace("Database=BuildWorkflow", "Database=postgres");
        bool needRecreate = true;
        try
        {
            using var checkConn = new Npgsql.NpgsqlConnection(connStr);
            checkConn.Open();
            using var checkCmd = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_name='Users' AND column_name='PasswordHash'", checkConn);
            needRecreate = Convert.ToInt32(checkCmd.ExecuteScalar()) == 0;
        }
        catch
        {
            needRecreate = true;
        }
        if (needRecreate)
        {
            using var maintConn = new Npgsql.NpgsqlConnection(maintConnStr);
            maintConn.Open();
            using (var dropCmd = new Npgsql.NpgsqlCommand("DROP DATABASE IF EXISTS \"BuildWorkflow\" WITH (FORCE);", maintConn)) dropCmd.ExecuteNonQuery();
            using (var createCmd = new Npgsql.NpgsqlCommand("CREATE DATABASE \"BuildWorkflow\";", maintConn)) createCmd.ExecuteNonQuery();
            Npgsql.NpgsqlConnection.ClearAllPools();
        }
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
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Permissions" bigint NOT NULL DEFAULT 0;
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
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "IsEmbedding" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "EmbeddingModelId" uuid NULL;
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

        foreach (var existingAdmin in db.Users.Where(u => (u.Role == "Admin" || u.Role == "OrgAdmin" || u.Role == "PlatformAdmin") && u.Permissions == 0).ToList())
        {
            existingAdmin.Permissions = (long)UserModulePermission.All;
        }
        db.SaveChanges();

        // Seed a single platform administrator account (password is BCrypt-hashed).
        // On a fresh/clean database this is the only account created automatically.
        if (!db.Users.Any(u => u.Role == "PlatformAdmin"))
        {
            db.Users.Add(new AppUser
            {
                Username = "platformadmin",
                FullName = "Platform Administrator",
                Password = "PlatformAdmin@2025!",
                Role = "PlatformAdmin",
                OrganizationId = defaultOrgId,
                IsActive = true,
                MfaEnabled = true,
                Permissions = (long)UserModulePermission.All
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

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' cdn.jsdelivr.net cdn.tailwindcss.com; style-src 'self' 'unsafe-inline' fonts.googleapis.com cdn.jsdelivr.net; font-src 'self' fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';";
    }
    await next();
});

app.UseRouting();

app.UseRateLimiter();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();


