using BuildWorkflow.Data;
using BuildWorkflow.Models;
using BuildWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace BuildWorkflow.Controllers;

[Authorize(Roles = "Admin,OrgAdmin,PlatformAdmin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    private readonly QdrantRagStore _qdrant;
    public AdminController(AppDbContext db, IWebHostEnvironment environment, IConfiguration configuration, QdrantRagStore qdrant)
    {
        _db = db;
        _environment = environment;
        _configuration = configuration;
        _qdrant = qdrant;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.ModelCount = await _db.AiModels.CountAsync();
        ViewBag.ToolCount = await _db.Tools.CountAsync();
        ViewBag.BucketCount = await _db.RagBuckets.CountAsync();
        ViewBag.UserCount = await _db.Users.CountAsync();
        return View();
    }

    public async Task<IActionResult> Models(int page = 1, string? search = null)
    {
        const int pageSize = 50;
        page = page < 1 ? 1 : page;
        var query = _db.AiModels.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => (m.Name ?? "").ToLower().Contains(term) || (m.ModelName ?? "").ToLower().Contains(term));
        }
        query = query.OrderByDescending(m => m.CreatedAt);
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateModel(AiModelDefinition model)
    {
        if (!string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.ModelName))
        {
            _db.AiModels.Add(model);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Models));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateModel(AiModelDefinition model)
    {
        if (model.Id == Guid.Empty)
        {
            return RedirectToAction(nameof(Models));
        }

        var existing = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == model.Id);
        if (existing != null && CanAccessOrganization(existing.OrganizationId) && !string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.ModelName))
        {
            existing.Name = model.Name.Trim();
            existing.Provider = string.IsNullOrWhiteSpace(model.Provider) ? existing.Provider : model.Provider.Trim();
            existing.Endpoint = string.IsNullOrWhiteSpace(model.Endpoint) ? existing.Endpoint : model.Endpoint.Trim();
            existing.ModelName = model.ModelName.Trim();
            existing.SystemPrompt = string.IsNullOrWhiteSpace(model.SystemPrompt) ? null : model.SystemPrompt.Trim();
            existing.IsActive = model.IsActive;
            existing.SupportsTools = model.SupportsTools;
            existing.SupportsImages = model.SupportsImages;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Models));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteModel(Guid id)
    {
        var model = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (model != null && CanAccessOrganization(model.OrganizationId))
        {
            _db.AiModels.Remove(model);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Models));
    }

    public async Task<IActionResult> Tools(int page = 1, string? search = null)
    {
        const int pageSize = 50;
        page = page < 1 ? 1 : page;
        var query = _db.Tools.Include(t => t.ToolGroup).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => (t.Name ?? "").ToLower().Contains(term) || (t.Description ?? "").ToLower().Contains(term));
        }
        query = query.OrderByDescending(t => t.CreatedAt);
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;
        ViewBag.ToolGroups = await _db.ToolGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Search = search;
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTool(
        string name,
        string description,
        string toolType,
        string httpMethod,
        string endpointOrType,
        string headersJson,
        string authType,
        string credential,
        string requestTemplate,
        string responsePath,
        string databaseType,
        string connectionString,
        string sqlQuery,
        string sftpHost,
        int sftpPort,
        string sftpUsername,
        string sftpPassword,
        string sftpRemoteBasePath,
        Guid? toolGroupId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return RedirectToAction(nameof(Tools));
        }

        var tool = new ToolDefinition
        {
            Name = name,
            Description = description ?? string.Empty,
            ToolType = string.IsNullOrWhiteSpace(toolType) ? "Api" : toolType,
            IsActive = true
        };

        _db.Tools.Add(tool);
        await _db.SaveChangesAsync();

        var normalizedType = tool.ToolType.ToLowerInvariant();

        if (normalizedType == "api" || normalizedType == "http" || normalizedType == "mcp")
        {
            _db.ApiToolConfigurations.Add(new ApiToolConfiguration
            {
                ToolId = tool.Id,
                HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod,
                Endpoint = endpointOrType ?? string.Empty,
                HeadersJson = headersJson,
                AuthType = string.IsNullOrWhiteSpace(authType) ? "None" : authType,
                Credential = credential,
                RequestTemplate = requestTemplate,
                ResponsePath = responsePath
            });
        }
        else if (normalizedType == "sqlreader" || normalizedType == "database" ||
                 normalizedType == "sqltoexcel" || normalizedType == "sqlexcel" || normalizedType == "sqlexcel" ||
                 normalizedType == "sqlsinglequery" || normalizedType == "sqlquery" || normalizedType == "sqlprocedure" ||
                 normalizedType == "sqlstoredprocedure" || normalizedType == "fixedsqlquery")
        {
            _db.SqlToolConfigurations.Add(new SqlToolConfiguration
            {
                ToolId = tool.Id,
                DatabaseType = string.IsNullOrWhiteSpace(databaseType) ? "PostgreSQL" : databaseType,
                ConnectionString = connectionString ?? string.Empty,
                Query = sqlQuery ?? string.Empty
            });
        }
        else if (normalizedType == "sftp")
        {
            _db.SftpToolConfigurations.Add(new SftpToolConfiguration
            {
                ToolId = tool.Id,
                Host = sftpHost ?? string.Empty,
                Port = sftpPort <= 0 ? 22 : sftpPort,
                Username = sftpUsername ?? string.Empty,
                Password = sftpPassword ?? string.Empty,
                RemoteBasePath = string.IsNullOrWhiteSpace(sftpRemoteBasePath) ? "/" : sftpRemoteBasePath
            });
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Tools));
    }

    public async Task<IActionResult> EditTool(Guid id)
    {
        var tool = await _db.Tools
            .Include(t => t.ApiConfiguration)
            .Include(t => t.SqlConfiguration)
            .Include(t => t.SftpConfiguration)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tool == null) return NotFound();
        ViewBag.ToolGroups = await _db.ToolGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.ToolLogs = await _db.ToolInvocationLogs.Where(l => l.ToolId == id).OrderByDescending(l => l.CreatedAt).Take(100).ToListAsync();
        return View(tool);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTool(
        Guid id,
        string name,
        string description,
        string toolType,
        string httpMethod,
        string endpointOrType,
        string headersJson,
        string authType,
        string credential,
        string requestTemplate,
        string responsePath,
        string databaseType,
        string connectionString,
        string sqlQuery,
        string sftpHost,
        int sftpPort,
        string sftpUsername,
        string sftpPassword,
        string sftpRemoteBasePath,
        Guid? toolGroupId = null)
    {
        var tool = await _db.Tools
            .Include(t => t.ApiConfiguration)
            .Include(t => t.SqlConfiguration)
            .Include(t => t.SftpConfiguration)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tool == null || !CanAccessOrganization(tool.OrganizationId)) return RedirectToAction(nameof(Tools));

        if (!string.IsNullOrWhiteSpace(name)) tool.Name = name.Trim();
        tool.Description = description ?? string.Empty;
        tool.ToolGroupId = toolGroupId;

        var normalizedType = (tool.ToolType ?? toolType ?? string.Empty).ToLowerInvariant();

        if (normalizedType == "api" || normalizedType == "http" || normalizedType == "mcp")
        {
            if (tool.ApiConfiguration == null)
            {
                tool.ApiConfiguration = new ApiToolConfiguration { ToolId = tool.Id };
                _db.ApiToolConfigurations.Add(tool.ApiConfiguration);
            }
            tool.ApiConfiguration.HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod;
            tool.ApiConfiguration.Endpoint = endpointOrType ?? string.Empty;
            tool.ApiConfiguration.HeadersJson = headersJson;
            tool.ApiConfiguration.AuthType = string.IsNullOrWhiteSpace(authType) ? "None" : authType;
            tool.ApiConfiguration.Credential = credential;
            tool.ApiConfiguration.RequestTemplate = requestTemplate;
            tool.ApiConfiguration.ResponsePath = responsePath;
        }
        else if (normalizedType == "sqlreader" || normalizedType == "database" ||
                 normalizedType == "sqltoexcel" || normalizedType == "sqlexcel" ||
                 normalizedType == "sqlsinglequery" || normalizedType == "sqlquery" || normalizedType == "sqlprocedure" ||
                 normalizedType == "sqlstoredprocedure" || normalizedType == "fixedsqlquery")
        {
            if (tool.SqlConfiguration == null)
            {
                tool.SqlConfiguration = new SqlToolConfiguration { ToolId = tool.Id };
                _db.SqlToolConfigurations.Add(tool.SqlConfiguration);
            }
            tool.SqlConfiguration.DatabaseType = string.IsNullOrWhiteSpace(databaseType) ? "PostgreSQL" : databaseType;
            tool.SqlConfiguration.ConnectionString = connectionString ?? string.Empty;
            tool.SqlConfiguration.Query = sqlQuery ?? string.Empty;
        }
        else if (normalizedType == "sftp")
        {
            if (tool.SftpConfiguration == null)
            {
                tool.SftpConfiguration = new SftpToolConfiguration { ToolId = tool.Id };
                _db.SftpToolConfigurations.Add(tool.SftpConfiguration);
            }
            tool.SftpConfiguration.Host = sftpHost ?? string.Empty;
            tool.SftpConfiguration.Port = sftpPort <= 0 ? 22 : sftpPort;
            tool.SftpConfiguration.Username = sftpUsername ?? string.Empty;
            tool.SftpConfiguration.Password = sftpPassword ?? string.Empty;
            tool.SftpConfiguration.RemoteBasePath = string.IsNullOrWhiteSpace(sftpRemoteBasePath) ? "/" : sftpRemoteBasePath;
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Tools));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTool(Guid id)
    {
        var tool = await _db.Tools.FirstOrDefaultAsync(t => t.Id == id);
        if (tool != null && CanAccessOrganization(tool.OrganizationId))
        {
            _db.Tools.Remove(tool);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Tools));
    }

    public async Task<IActionResult> ToolGroups()
    {
        var groups = await _db.ToolGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Tools = await _db.Tools.OrderBy(t => t.Name).ToListAsync();
        return View(groups);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateToolGroup(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.ToolGroups.Add(new ToolGroup
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
            });
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(ToolGroups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateToolGroup(Guid id, string name, string? description)
    {
        var group = await _db.ToolGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group != null && CanAccessOrganization(group.OrganizationId) && !string.IsNullOrWhiteSpace(name))
        {
            group.Name = name.Trim();
            group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(ToolGroups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteToolGroup(Guid id)
    {
        var group = await _db.ToolGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group != null && CanAccessOrganization(group.OrganizationId))
        {
            var tools = await _db.Tools.Where(t => t.ToolGroupId == id).ToListAsync();
            foreach (var tool in tools)
            {
                tool.ToolGroupId = null;
            }

            _db.ToolGroups.Remove(group);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(ToolGroups));
    }

    public async Task<IActionResult> RagBuckets(int page = 1, int docPage = 1, string? search = null)
    {
        const int pageSize = 50;
        page = page < 1 ? 1 : page;
        docPage = docPage < 1 ? 1 : docPage;

        var bucketBase = _db.RagBuckets.Include(b => b.KnowledgeGroup).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            bucketBase = bucketBase.Where(b => (b.Name ?? "").ToLower().Contains(term) || (b.Description ?? "").ToLower().Contains(term));
        }
        var bucketQuery = bucketBase.OrderByDescending(b => b.CreatedAt);
        var bucketTotal = await bucketQuery.CountAsync();
        var bucketPages = Math.Max(1, (int)Math.Ceiling(bucketTotal / (double)pageSize));
        if (page > bucketPages) page = bucketPages;
        var buckets = await bucketQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var documentQuery = _db.RagDocuments.OrderByDescending(d => d.CreatedAt);
        var documentTotal = await documentQuery.CountAsync();
        var documentPages = Math.Max(1, (int)Math.Ceiling(documentTotal / (double)pageSize));
        if (docPage > documentPages) docPage = documentPages;
        var documents = await documentQuery.Skip((docPage - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        ViewBag.RagAssignments = await _db.UserRagBuckets.Include(a => a.User).Include(a => a.RagBucket).ToListAsync();
        ViewBag.KnowledgeGroups = await _db.KnowledgeGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Documents = documents;
        ViewBag.DocumentPage = docPage;
        ViewBag.DocumentPageSize = pageSize;
        ViewBag.DocumentTotalCount = documentTotal;
        ViewBag.DocumentTotalPages = documentPages;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = bucketTotal;
        ViewBag.TotalPages = bucketPages;
        ViewBag.Search = search;
        return View(buckets);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRagBucket(RagBucket bucket)
    {
        if (!string.IsNullOrWhiteSpace(bucket.Name))
        {
            _db.RagBuckets.Add(bucket);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(RagBuckets));
    }

    public async Task<IActionResult> KnowledgeGroups()
    {
        var groups = await _db.KnowledgeGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Buckets = await _db.RagBuckets.OrderBy(b => b.Name).ToListAsync();
        return View(groups);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateKnowledgeGroup(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.KnowledgeGroups.Add(new KnowledgeGroup
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(KnowledgeGroups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateKnowledgeGroup(Guid id, string name, string? description)
    {
        var group = await _db.KnowledgeGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group != null && CanAccessOrganization(group.OrganizationId) && !string.IsNullOrWhiteSpace(name))
        {
            group.Name = name.Trim();
            group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(KnowledgeGroups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteKnowledgeGroup(Guid id)
    {
        var group = await _db.KnowledgeGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group != null && CanAccessOrganization(group.OrganizationId))
        {
            var buckets = await _db.RagBuckets.Where(b => b.KnowledgeGroupId == id).ToListAsync();
            foreach (var bucket in buckets)
            {
                bucket.KnowledgeGroupId = null;
            }
            _db.KnowledgeGroups.Remove(group);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(KnowledgeGroups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRagBucket(Guid id)
    {
        var bucket = await _db.RagBuckets.FirstOrDefaultAsync(b => b.Id == id);
        if (bucket != null && CanAccessOrganization(bucket.OrganizationId))
        {
            var documents = await _db.RagDocuments.Where(x => x.RagBucketId == bucket.Id).ToListAsync();
            _db.RagDocuments.RemoveRange(documents);
            _db.RagBuckets.Remove(bucket);
            try { await _qdrant.DeleteBucketAsync(bucket.Id); } catch { }
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(RagBuckets));
    }

    public async Task<IActionResult> AssignModels()
    {
        ViewBag.Users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        ViewBag.Models = await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
        ViewBag.Assignments = await _db.UserAiModels.Include(a => a.User).Include(a => a.AiModel).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignModel(Guid userId, Guid modelId)
    {
        if (!await CanManageUserAsync(userId) ||
            !_db.AiModels.Any(m => m.Id == modelId) ||
            !_db.Users.Any(u => u.Id == userId))
        {
            return RedirectToAction(nameof(AssignModels));
        }

        if (!_db.UserAiModels.Any(a => a.UserId == userId && a.AiModelId == modelId))
        {
            _db.UserAiModels.Add(new UserAiModel { UserId = userId, AiModelId = modelId });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignModels));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignModel(Guid id)
    {
        var assignment = await _db.UserAiModels.FirstOrDefaultAsync(a => a.Id == id);
        if (assignment != null && await CanManageUserAsync(assignment.UserId))
        {
            _db.UserAiModels.Remove(assignment);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignModels));
    }

    public async Task<IActionResult> AssignTools()
    {
        ViewBag.Users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        ViewBag.Tools = await _db.Tools.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.ToolGroups = await _db.ToolGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Assignments = await _db.UserToolPermissions.Include(a => a.User).Include(a => a.Tool).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTool(Guid userId, Guid toolId)
    {
        if (!await CanManageUserAsync(userId) || !_db.Tools.Any(t => t.Id == toolId))
        {
            return RedirectToAction(nameof(AssignTools));
        }

        if (!_db.UserToolPermissions.Any(p => p.UserId == userId && p.ToolId == toolId))
        {
            _db.UserToolPermissions.Add(new UserToolPermission { UserId = userId, ToolId = toolId });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignTools));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignToolGroup(Guid userId, Guid toolGroupId)
    {
        if (!await CanManageUserAsync(userId) || !_db.ToolGroups.Any(g => g.Id == toolGroupId))
        {
            return RedirectToAction(nameof(AssignTools));
        }

        var toolIds = await _db.Tools.Where(t => t.ToolGroupId == toolGroupId && t.IsActive).Select(t => t.Id).ToListAsync();
        foreach (var toolId in toolIds)
        {
            if (!_db.UserToolPermissions.Any(p => p.UserId == userId && p.ToolId == toolId))
            {
                _db.UserToolPermissions.Add(new UserToolPermission { UserId = userId, ToolId = toolId });
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(AssignTools));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignTool(Guid id)
    {
        var permission = await _db.UserToolPermissions.FirstOrDefaultAsync(p => p.Id == id);
        if (permission != null && await CanManageUserAsync(permission.UserId))
        {
            _db.UserToolPermissions.Remove(permission);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignTools));
    }

    public async Task<IActionResult> AssignRagBuckets()
    {
        ViewBag.Users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        ViewBag.Buckets = await _db.RagBuckets.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        ViewBag.KnowledgeGroups = await _db.KnowledgeGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.Assignments = await _db.UserRagBuckets.Include(a => a.User).Include(a => a.RagBucket).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRagBucket(Guid userId, Guid bucketId)
    {
        if (!await CanManageUserAsync(userId) || !_db.RagBuckets.Any(b => b.Id == bucketId))
        {
            return RedirectToAction(nameof(AssignRagBuckets));
        }

        if (!_db.UserRagBuckets.Any(p => p.UserId == userId && p.RagBucketId == bucketId))
        {
            _db.UserRagBuckets.Add(new UserRagBucket { UserId = userId, RagBucketId = bucketId });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignRagBuckets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRagBucketGroup(Guid userId, Guid knowledgeGroupId)
    {
        if (!await CanManageUserAsync(userId) || !_db.KnowledgeGroups.Any(g => g.Id == knowledgeGroupId))
        {
            return RedirectToAction(nameof(AssignRagBuckets));
        }

        var bucketIds = await _db.RagBuckets.Where(b => b.KnowledgeGroupId == knowledgeGroupId && b.IsActive).Select(b => b.Id).ToListAsync();
        foreach (var bucketId in bucketIds)
        {
            if (!_db.UserRagBuckets.Any(p => p.UserId == userId && p.RagBucketId == bucketId))
            {
                _db.UserRagBuckets.Add(new UserRagBucket { UserId = userId, RagBucketId = bucketId });
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(AssignRagBuckets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignRagBucket(Guid id)
    {
        var permission = await _db.UserRagBuckets.FirstOrDefaultAsync(p => p.Id == id);
        if (permission != null && await CanManageUserAsync(permission.UserId))
        {
            _db.UserRagBuckets.Remove(permission);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(AssignRagBuckets));
    }

    public async Task<IActionResult> RagKnowledge()
    {
        return RedirectToAction(nameof(RagBuckets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadRagKnowledge(Guid bucketId, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return RedirectToAction(nameof(RagBuckets));
        }

        var bucket = await _db.RagBuckets.FirstOrDefaultAsync(b => b.Id == bucketId);
        if (bucket == null || !CanAccessOrganization(bucket.OrganizationId))
        {
            return RedirectToAction(nameof(RagBuckets));
        }
        var tempFolder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", "rag");
        Directory.CreateDirectory(tempFolder);
        var tempPath = Path.Combine(tempFolder, $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");

        await using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        string content = BuildWorkflow.RAGPipeline.RagFileTextExtractor.ExtractText(tempPath, file.FileName);

        try { System.IO.File.Delete(tempPath); } catch { }

        if (!string.IsNullOrWhiteSpace(content))
        {
            var endpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
            var embeddingModel = _configuration["Ollama:EmbeddingModel"];
            var chunkList = BuildWorkflow.RAGPipeline.RagChunker.Chunk(content).ToList();

            // Embed the document once and all chunks with bounded parallelism.
            // This keeps uploads responsive while avoiding excessive pressure on Ollama.
            using var embeddingLimiter = new SemaphoreSlim(4);
            var documentEmbeddingTask = BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, content, HttpContext.RequestAborted);
            var chunkEmbeddings = new float[]?[chunkList.Count];
            var chunkTasks = Enumerable.Range(0, chunkList.Count).Select(async index =>
            {
                await embeddingLimiter.WaitAsync(HttpContext.RequestAborted);
                try
                {
                    chunkEmbeddings[index] = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, chunkList[index], HttpContext.RequestAborted);
                }
                finally
                {
                    embeddingLimiter.Release();
                }
            }).ToArray();

            await Task.WhenAll(chunkTasks.Prepend(documentEmbeddingTask));
            var documentEmbedding = await documentEmbeddingTask;

            var document = new RagDocument
            {
                RagBucketId = bucketId,
                FileName = Path.GetFileName(file.FileName),
                Content = content,
                EmbeddingJson = BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.ToJson(documentEmbedding)
            };
            _db.RagDocuments.Add(document);
            await _db.SaveChangesAsync();

            // Store vectors and chunk content in Qdrant instead of PostgreSQL.
            var qdrantChunks = chunkList.Select((chunk, i) => (chunk, chunkEmbeddings[i])).ToList();
            await _qdrant.UpsertChunksAsync(bucketId, document.Id, qdrantChunks, HttpContext.RequestAborted);
        }

        return RedirectToAction(nameof(RagBuckets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRagDocument(Guid id)
    {
        var document = await _db.RagDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (document != null)
        {
            var bucket = await _db.RagBuckets.FirstOrDefaultAsync(b => b.Id == document.RagBucketId);
            if (bucket != null && !CanAccessOrganization(bucket.OrganizationId))
            {
                return RedirectToAction(nameof(RagBuckets));
            }

            try { await _qdrant.DeleteDocumentAsync(document.Id); } catch { }
            _db.RagDocuments.Remove(document);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(RagBuckets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CleanTempFiles()
    {
        var root = Path.Combine(_environment.ContentRootPath, "TempFilesLocal");
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
            foreach (var directory in Directory.GetDirectories(root))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        TempData["Message"] = "Temporary files cleaned.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Users(int page = 1, string? search = null)
    {
        const int pageSize = 50;
        page = page < 1 ? 1 : page;
        var query = _db.Users.Where(u => u.Role != "PlatformAdmin").AsQueryable();
        var claimOrg = User.FindFirstValue("OrganizationId");
        if (Guid.TryParse(claimOrg, out var currentOrg))
        {
            query = query.Where(u => u.OrganizationId == currentOrg);
        }
        else
        {
            query = query.Where(u => false);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => (u.Username ?? "").ToLower().Contains(term) || (u.FullName ?? "").ToLower().Contains(term) || (u.Email ?? "").ToLower().Contains(term));
        }
        query = query.OrderByDescending(u => u.CreatedAt);
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;
        ViewBag.Search = search;
        return View(items);
    }

    public async Task<IActionResult> ManageUser(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!User.IsInRole("PlatformAdmin"))
        {
            var claimOrg = User.FindFirstValue("OrganizationId");
            if (!Guid.TryParse(claimOrg, out var currentOrg) || user.OrganizationId != currentOrg)
            {
                return NotFound();
            }
        }

        ViewBag.User = user;
        ViewBag.Organization = user.OrganizationId.HasValue ? await _db.Organizations.FindAsync(user.OrganizationId.Value) : null;
        var isPlatformUser = string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase);
        if (!isPlatformUser && string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            user.MfaSecret = MfaHelper.GenerateSecret();
            user.MfaEnabled = true;
            await _db.SaveChangesAsync();
        }
        ViewBag.MfaSecret = user.MfaSecret;
        ViewBag.MfaPlatformCode = MfaHelper.PlatformCode();
        if (!isPlatformUser && !string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            ViewBag.MfaOtpAuthUri = $"otpauth://totp/BuildWorkflow:{user.Username}?secret={user.MfaSecret}&issuer=BuildWorkflow";
        }
        ViewBag.Models = await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
        ViewBag.Tools = await _db.Tools.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.ToolGroups = await _db.ToolGroups.OrderBy(g => g.Name).ToListAsync();
        ViewBag.KnowledgeGroups = await _db.KnowledgeGroups.OrderBy(g => g.Name).ToListAsync();
        var ragBuckets = await _db.RagBuckets.Include(b => b.KnowledgeGroup).Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        ViewBag.RagBuckets = ragBuckets;
        ViewBag.Buckets = ragBuckets;
        ViewBag.AssignedModels = await _db.UserAiModels
            .Where(p => p.UserId == id)
            .Include(p => p.AiModel)
            .OrderBy(p => p.AiModel.Name)
            .ToListAsync();
        ViewBag.AssignedTools = await _db.UserToolPermissions
            .Where(p => p.UserId == id)
            .Include(p => p.Tool)
            .OrderBy(p => p.Tool.Name)
            .ToListAsync();
        ViewBag.AssignedBuckets = await _db.UserRagBuckets
            .Where(p => p.UserId == id)
            .Include(p => p.RagBucket)
            .OrderBy(p => p.RagBucket.Name)
            .ToListAsync();
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(Guid id, string username, string? fullName, string? email, string? password, string role, bool requirePasswordChange = false, string[]? allowedFileExtensions = null)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || string.IsNullOrWhiteSpace(username))
        {
            return RedirectToAction(nameof(ManageUser), new { id });
        }

        // Organization admins cannot view or modify users outside their own organization.
        if (!User.IsInRole("PlatformAdmin"))
        {
            var claimOrg = User.FindFirstValue("OrganizationId");
            if (!Guid.TryParse(claimOrg, out var currentOrg) || user.OrganizationId != currentOrg)
            {
                return RedirectToAction(nameof(ManageUser), new { id });
            }

            // Organization admins may not create or promote platform-owner accounts.
            if (string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(ManageUser), new { id });
            }
        }

        user.Username = username.Trim();
        user.FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        user.MustChangePassword = requirePasswordChange;
        if (allowedFileExtensions != null)
        {
            var selectedTypes = allowedFileExtensions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            user.AllowedFileExtensions = selectedTypes.Count == 0 ? string.Empty : string.Join(",", selectedTypes);
        }

        if (User.IsInRole("PlatformAdmin") && !string.IsNullOrWhiteSpace(role) &&
            !string.Equals(role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            user.Role = role;
        }

        if (string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(ManageUser), new { id });
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            if (!IsStrongPassword(password))
            {
                TempData["Error"] = "Password must be at least 8 characters and include uppercase, lowercase, number, and special character.";
                return RedirectToAction(nameof(ManageUser), new { id });
            }
            user.Password = password.Trim();
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageUser), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUserModel(Guid userId, Guid modelId)
    {
        if (!await CanManageUserAsync(userId) || !_db.AiModels.Any(m => m.Id == modelId))
        {
            return RedirectToAction(nameof(ManageUser), new { id = userId });
        }

        if (!_db.UserAiModels.Any(p => p.UserId == userId && p.AiModelId == modelId))
        {
            _db.UserAiModels.Add(new UserAiModel { UserId = userId, AiModelId = modelId });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignUserModel(Guid id, Guid userId)
    {
        var assignment = await _db.UserAiModels.FirstOrDefaultAsync(a => a.Id == id);
        if (assignment != null && await CanManageUserAsync(userId))
        {
            _db.UserAiModels.Remove(assignment);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUserTool(Guid userId, Guid? toolId = null, Guid[]? toolIds = null)
    {
        if (!await CanManageUserAsync(userId))
        {
            return RedirectToAction(nameof(ManageUser), new { id = userId });
        }

        var ids = new List<Guid>();
        if (toolId.HasValue && toolId.Value != Guid.Empty) ids.Add(toolId.Value);
        if (toolIds != null) ids.AddRange(toolIds.Where(x => x != Guid.Empty));
        ids = ids.Distinct().ToList();
        ids = ids.Where(id => _db.Tools.Any(t => t.Id == id)).ToList();

        var selected = ids.ToHashSet();
        var assigned = await _db.UserToolPermissions
            .Where(p => p.UserId == userId)
            .ToListAsync();

        foreach (var id in selected)
        {
            if (!assigned.Any(p => p.ToolId == id))
            {
                _db.UserToolPermissions.Add(new UserToolPermission { UserId = userId, ToolId = id });
            }
        }

        foreach (var permission in assigned.Where(p => !selected.Contains(p.ToolId)))
        {
            _db.UserToolPermissions.Remove(permission);
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUserToolGroup(Guid userId, Guid toolGroupId)
    {
        if (!await CanManageUserAsync(userId) || !_db.ToolGroups.Any(g => g.Id == toolGroupId))
        {
            return RedirectToAction(nameof(ManageUser), new { id = userId });
        }

        var toolIds = await _db.Tools.Where(t => t.ToolGroupId == toolGroupId && t.IsActive).Select(t => t.Id).ToListAsync();
        foreach (var toolId in toolIds)
        {
            if (!_db.UserToolPermissions.Any(p => p.UserId == userId && p.ToolId == toolId))
            {
                _db.UserToolPermissions.Add(new UserToolPermission { UserId = userId, ToolId = toolId });
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignUserTool(Guid id, Guid userId)
    {
        var permission = await _db.UserToolPermissions.FirstOrDefaultAsync(p => p.Id == id);
        if (permission != null && await CanManageUserAsync(userId))
        {
            _db.UserToolPermissions.Remove(permission);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUserBucket(Guid userId, Guid? bucketId = null, Guid[]? bucketIds = null)
    {
        if (!await CanManageUserAsync(userId))
        {
            return RedirectToAction(nameof(ManageUser), new { id = userId });
        }

        var ids = new List<Guid>();
        if (bucketId.HasValue && bucketId.Value != Guid.Empty) ids.Add(bucketId.Value);
        if (bucketIds != null) ids.AddRange(bucketIds.Where(x => x != Guid.Empty));
        ids = ids.Distinct().ToList();
        ids = ids.Where(id => _db.RagBuckets.Any(b => b.Id == id)).ToList();

        var selected = ids.ToHashSet();
        var assigned = await _db.UserRagBuckets
            .Where(p => p.UserId == userId)
            .ToListAsync();

        foreach (var id in selected)
        {
            if (!assigned.Any(p => p.RagBucketId == id))
            {
                _db.UserRagBuckets.Add(new UserRagBucket { UserId = userId, RagBucketId = id });
            }
        }

        foreach (var assignment in assigned.Where(p => !selected.Contains(p.RagBucketId)))
        {
            _db.UserRagBuckets.Remove(assignment);
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignUserRagBucketGroup(Guid userId, Guid knowledgeGroupId)
    {
        if (!await CanManageUserAsync(userId) || !_db.KnowledgeGroups.Any(g => g.Id == knowledgeGroupId))
        {
            return RedirectToAction(nameof(ManageUser), new { id = userId });
        }

        var bucketIds = await _db.RagBuckets.Where(b => b.KnowledgeGroupId == knowledgeGroupId && b.IsActive).Select(b => b.Id).ToListAsync();
        foreach (var bucketId in bucketIds)
        {
            if (!_db.UserRagBuckets.Any(p => p.UserId == userId && p.RagBucketId == bucketId))
            {
                _db.UserRagBuckets.Add(new UserRagBucket { UserId = userId, RagBucketId = bucketId });
            }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignUserBucket(Guid id, Guid userId)
    {
        var permission = await _db.UserRagBuckets.FindAsync(id);
        if (permission != null)
        {
            _db.UserRagBuckets.Remove(permission);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ManageUser), new { id = userId });
    }

    private bool IsPlatformAdmin() => User.IsInRole("PlatformAdmin");

    private Guid? CurrentOrganizationId()
    {
        var claimOrg = User.FindFirstValue("OrganizationId");
        return Guid.TryParse(claimOrg, out var orgId) ? orgId : null;
    }

    private bool CanAccessOrganization(Guid? organizationId)
    {
        if (IsPlatformAdmin()) return true;
        var currentOrg = CurrentOrganizationId();
        return currentOrg.HasValue && currentOrg.Value == organizationId;
    }

    private async Task<bool> CanManageUserAsync(Guid userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;
        return CanAccessOrganization(user.OrganizationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.Password))
        {
            return RedirectToAction(nameof(Users));
        }

        user.Username = user.Username.Trim();
        user.Password = user.Password.Trim();
        user.MustChangePassword = true;
        if (!IsStrongPassword(user.Password))
        {
            TempData["Error"] = "Password must be at least 8 characters and include uppercase, lowercase, number, and special character.";
            return RedirectToAction(nameof(Users));
        }
        if (!string.IsNullOrWhiteSpace(user.Email)) user.Email = user.Email.Trim();
        if (!string.IsNullOrWhiteSpace(user.FullName)) user.FullName = user.FullName.Trim();

        var duplicate = _db.Users.Any(u =>
            u.Username == user.Username ||
            (!string.IsNullOrWhiteSpace(user.Email) && u.Email == user.Email));
        if (duplicate)
        {
            return RedirectToAction(nameof(Users));
        }

        // The admin screen must never create platform-owner accounts, even if the
        // role value is tampered with in the request.
        if (string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            user.Role = "User";
        }

        // Non-platform admins can only create users in their own organization and
        // cannot assign privileged roles.
        if (!(User.IsInRole("PlatformAdmin")))
        {
            var claimOrg = User.FindFirstValue("OrganizationId");
            if (Guid.TryParse(claimOrg, out var orgId))
            {
                user.OrganizationId = orgId;
            }
            user.Role = "User";
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnblockUser(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(ManageUser), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Users));
        }

        if (!User.IsInRole("PlatformAdmin"))
        {
            var claimOrg = User.FindFirstValue("OrganizationId");
            if (!Guid.TryParse(claimOrg, out var currentOrg) || user.OrganizationId != currentOrg)
            {
                return RedirectToAction(nameof(Users));
            }
            if (string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Role, "OrgAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(Users));
            }
        }

        user.IsActive = false;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Users));
    }

    private static bool IsStrongPassword(string? password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Length >= 8 &&
            password.Any(char.IsUpper) && password.Any(char.IsLower) &&
            password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c));
    }
}

