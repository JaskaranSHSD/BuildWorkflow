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
builder.Services.AddControllersWithViews();

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
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "AiModels" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "Tools" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "ToolGroups" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "RagBuckets" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "KnowledgeGroups" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "RagDocuments" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
            ALTER TABLE "RagChunks" ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;
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
            ALTER TABLE "RagChunks" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "RagChunks" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "UploadedFiles" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "UploadedFiles" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "ChatWorkflows" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "ChatWorkflows" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "ChatSessions" ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE "ChatSessions" ADD COLUMN IF NOT EXISTS "UpdatedByUserId" uuid NULL;
            ALTER TABLE "Workflows" ADD COLUMN IF NOT EXISTS "NextWorkflowId" uuid NULL;
            ALTER TABLE "WorkflowRuns" ADD COLUMN IF NOT EXISTS "ParentRunId" uuid NULL;
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
        """);

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

        var adminUser = db.Users.FirstOrDefault(u => u.Role == "OrgAdmin" || u.Role == "Admin");
        if (adminUser != null)
        {
            foreach (var orphan in db.Workflows.Where(w => w.Name.StartsWith("Sample Chain") && w.UserId == null).ToList())
            {
                orphan.UserId = adminUser.Id;
            }
            db.SaveChanges();
        }

        if (!db.Workflows.Any(w => w.Name == "Sample Chain A - Summarize"))
        {
            var sampleA = new WorkflowDefinition
            {
                UserId = adminUser?.Id,
                Name = "Sample Chain A - Summarize",
                SystemInstruction = "You are a helpful AI assistant.",
                UserInstruction = "Summarize the benefits of AI in three concise bullet points."
            };
            var sampleB = new WorkflowDefinition
            {
                UserId = adminUser?.Id,
                Name = "Sample Chain B - Draft Email",
                SystemInstruction = "You are a helpful AI assistant.",
                UserInstruction = "Using the summary from the previous workflow, draft a short professional email about AI benefits."
            };
            var sampleC = new WorkflowDefinition
            {
                UserId = adminUser?.Id,
                Name = "Sample Chain C - Polish Email",
                SystemInstruction = "You are a helpful AI assistant.",
                UserInstruction = "Polish the prior email draft for clarity, tone, and grammar. Output the final version only."
            };
            db.Workflows.AddRange(sampleA, sampleB, sampleC);
            db.SaveChanges();
            sampleA.NextWorkflowId = sampleB.Id;
            sampleB.NextWorkflowId = sampleC.Id;
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


