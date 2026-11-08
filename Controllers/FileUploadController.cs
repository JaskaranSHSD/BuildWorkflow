using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api/files")]
public class FileUploadController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;

    private const long MaxRequestSize = 50 * 1024 * 1024;
    private const long DefaultMaxFileSize = 10 * 1024 * 1024;

    public FileUploadController(AppDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [Authorize]
    [RequestSizeLimit(MaxRequestSize)]
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? sessionKey, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A non-empty file is required." });
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? userId = Guid.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".zip"
        };
        long maxSize = DefaultMaxFileSize;

        if (userId.HasValue)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
            if (user?.OrganizationId.HasValue == true)
            {
                var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == user.OrganizationId.Value, cancellationToken);
                if (org != null)
                {
                    if (org.MaxUploadSizeBytes > 0)
                    {
                        maxSize = org.MaxUploadSizeBytes;
                    }
                    var orgAllowed = (org.AllowedFileExtensions ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (orgAllowed.Length > 0)
                    {
                        allowedExtensions = new HashSet<string>(orgAllowed, StringComparer.OrdinalIgnoreCase);
                    }

                    if (user.AllowedFileExtensions != null)
                    {
                        var userAllowed = user.AllowedFileExtensions
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (userAllowed.Length > 0)
                        {
                            allowedExtensions = new HashSet<string>(userAllowed, StringComparer.OrdinalIgnoreCase)
                                .Intersect(allowedExtensions, StringComparer.OrdinalIgnoreCase)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        }
                        else
                        {
                            allowedExtensions.Clear();
                        }
                    }
                }
            }
            else if (user != null && user.AllowedFileExtensions != null)
            {
                var userAllowed = user.AllowedFileExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (userAllowed.Length > 0)
                {
                    allowedExtensions = new HashSet<string>(userAllowed, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    allowedExtensions.Clear();
                }
            }
        }

        if (file.Length > maxSize)
        {
            return BadRequest(new { error = "File is too large. Maximum size is " + maxSize + " bytes." });
        }

        var fileExtension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!allowedExtensions.Contains(fileExtension))
        {
            return BadRequest(new { error = "File type is not allowed. Allowed types: " + string.Join(", ", allowedExtensions) + "." });
        }

        var safeSession = SanitizeSegment(string.IsNullOrWhiteSpace(sessionKey) ? Guid.NewGuid().ToString("N") : sessionKey);
        var userSegment = userId.HasValue ? userId.Value.ToString("N") : "anonymous";
        var uploadFolder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", userSegment, safeSession);
        Directory.CreateDirectory(uploadFolder);

        var originalFileName = Path.GetFileName(file.FileName);
        var safeFileName = SanitizeFileName(originalFileName);
        var storedFileName = $"{Guid.NewGuid():N}_{safeFileName}";
        var storedPath = Path.Combine(uploadFolder, storedFileName);

        await using (var stream = System.IO.File.Create(storedPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var uploadedFile = new UploadedFile
        {
            UserId = userId,
            SessionKey = safeSession,
            OriginalFileName = originalFileName,
            StoredPath = storedPath,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Size = file.Length
        };

        _db.UploadedFiles.Add(uploadedFile);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            fileId = uploadedFile.Id,
            fileName = originalFileName,
            token = $"file:{uploadedFile.Id}"
        });
    }

    [Authorize]
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var uploadedFile = await _db.UploadedFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (uploadedFile == null)
        {
            return NotFound();
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? userId = Guid.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        if (!isAdmin && uploadedFile.UserId != userId)
        {
            return Forbid();
        }

        // Organization isolation: org/platform admins may only access files owned by users in their organization.
        if (isAdmin && !User.IsInRole("PlatformAdmin"))
        {
            var currentUserIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(currentUserIdValue, out var currentUserId))
            {
                var currentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                var owner = uploadedFile.UserId.HasValue
                    ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uploadedFile.UserId.Value, cancellationToken)
                    : null;

                if (currentUser?.OrganizationId != owner?.OrganizationId)
                {
                    return Forbid();
                }
            }
        }

        var fullPath = Path.GetFullPath(uploadedFile.StoredPath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, uploadedFile.ContentType, uploadedFile.OriginalFileName);
    }

    private static string SanitizeSegment(string value)
    {
        var safe = new string(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "uploaded_file" : name;
    }
}
