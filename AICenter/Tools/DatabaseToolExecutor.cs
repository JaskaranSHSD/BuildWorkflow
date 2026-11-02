using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Renci.SshNet;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BuildWorkflow.AICenter.Tools;

public class DatabaseToolExecutor
{
    private static readonly AsyncLocal<List<string>?> GeneratedFileTokens = new();

    public static void BeginGeneratedFileTracking() => GeneratedFileTokens.Value = new List<string>();
    public static IReadOnlyList<string> GetGeneratedFileTokens() => GeneratedFileTokens.Value?.Distinct().Select(x => x).ToList() ?? new List<string>();
    public static void ClearGeneratedFileTracking() => GeneratedFileTokens.Value = null;
    public static void RecordGeneratedFile(Guid fileId) => GeneratedFileTokens.Value?.Add($"file:{fileId}");

    private readonly AppDbContext _db;
    private readonly Helpers.ApiToolHelper _apiHelper;
    private readonly IWebHostEnvironment _environment;

    public DatabaseToolExecutor(AppDbContext db, Helpers.ApiToolHelper apiHelper, IWebHostEnvironment environment)
    {
        _db = db;
        _apiHelper = apiHelper;
        _environment = environment;
    }

    public async Task<IList<AITool>> GetAllowedToolsAsync(Guid? userId, IEnumerable<Guid>? selectedToolIds = null, CancellationToken cancellationToken = default)
    {
        List<ToolDefinition> tools;

        if (!userId.HasValue)
        {
            tools = new List<ToolDefinition>();
        }
        else
        {
            var user = await _db.Users.FindAsync(new object[] { userId.Value }, cancellationToken);
            if (user?.IsAdmin == true)
            {
                tools = await _db.Tools.Include(t => t.ApiConfiguration).Include(t => t.SqlConfiguration).Include(t => t.SftpConfiguration).Where(t => t.IsActive).ToListAsync<ToolDefinition>(cancellationToken);
            }
            else
            {
                var assignedIds = await _db.UserToolPermissions
                    .Where(p => p.UserId == userId.Value)
                    .Select(p => p.ToolId)
                    .ToListAsync<Guid>(cancellationToken);

                tools = await _db.Tools
                    .Include(t => t.ApiConfiguration)
                    .Include(t => t.SqlConfiguration).Include(t => t.SftpConfiguration)
                    .Where(t => t.IsActive && assignedIds.Contains(t.Id))
                    .ToListAsync<ToolDefinition>(cancellationToken);
            }
        }

        if (selectedToolIds != null)
        {
            var selectedSet = selectedToolIds.Where(id => id != Guid.Empty).ToHashSet();
            if (selectedSet.Count > 0)
            {
                tools = tools.Where(t => selectedSet.Contains(t.Id)).ToList();
            }
            else
            {
                tools = new List<ToolDefinition>();
            }
        }

        var aiTools = new List<AITool>();
        foreach (var tool in tools)
        {
            if (IsFixedSqlToolType(tool.ToolType))
            {
                aiTools.Add(CreateFixedSqlTool(tool));
            }
            else if (string.Equals(tool.ToolType, "SqlReader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.ToolType, "Database", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateSqlTool(tool));
            }
            else if (string.Equals(tool.ToolType, "Excel", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "TextFile", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "File", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "Files", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "Pdf", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "FileReader", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateFileReaderTool(tool));
            }
            else if (string.Equals(tool.ToolType, "Sftp", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateSftpTool(tool));
            }
            else if (string.Equals(tool.ToolType, "ExportExcel", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "ExcelExport", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateExcelExportTool(tool, userId));
            }
            else if (string.Equals(tool.ToolType, "ExportText", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "TextExport", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateTextExportTool(tool, userId));
            }
            else if (string.Equals(tool.ToolType, "ExportWord", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "WordExport", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateWordExportTool(tool, userId));
            }
            else if (string.Equals(tool.ToolType, "SqlToExcel", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(tool.ToolType, "SqlExcel", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.Add(CreateSqlToExcelTool(tool, userId));
            }
            else if (string.Equals(tool.ToolType, "Mcp", StringComparison.OrdinalIgnoreCase))
            {
                aiTools.AddRange(await CreateMcpToolsAsync(tool, cancellationToken));
            }
            else
            {
                aiTools.Add(CreateTool(tool));
            }
        }

        return aiTools;
    }

    private AITool CreateExcelTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            (string filePath) => ReadExcel(filePath),
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Read an Excel file into JSON." : tool.Description);
    }

    private static string ReadExcel(string filePath)
    {
        var table = Helpers.ExcelHelper.ReadSheet(filePath);
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow row in table.Rows)
        {
            var item = new Dictionary<string, object?>();
            foreach (DataColumn column in table.Columns)
            {
                item[column.ColumnName] = row[column] == DBNull.Value ? null : row[column];
            }
            rows.Add(item);
        }
        return System.Text.Json.JsonSerializer.Serialize(rows);
    }

    private AITool CreateSqlTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            async (string input) =>
            {
                if (string.IsNullOrWhiteSpace(tool.SqlConfiguration?.ConnectionString))
                {
                    return "No connection string is configured for this SQL tool.";
                }

                if (string.Equals(input?.Trim(), "schema", StringComparison.OrdinalIgnoreCase))
                {
                    return await Helpers.SqlDatabaseReader.GetSchemaAsync(tool.SqlConfiguration!.ConnectionString, tool.SqlConfiguration.DatabaseType);
                }

                return await Helpers.SqlDatabaseReader.ExecuteReadOnlyQueryAsync(tool.SqlConfiguration!.ConnectionString, tool.SqlConfiguration.DatabaseType, input ?? string.Empty);
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Read data from a SQL database." : tool.Description);
    }

    private AITool CreateFixedSqlTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(tool.SqlConfiguration?.ConnectionString))
                {
                    return "No connection string is configured for this SQL tool.";
                }

                if (string.IsNullOrWhiteSpace(tool.SqlConfiguration.Query))
                {
                    return "No query or procedure is defined for this SQL tool.";
                }

                return await Helpers.SqlDatabaseReader.ExecuteReadOnlyQueryAsync(tool.SqlConfiguration.ConnectionString, tool.SqlConfiguration.DatabaseType, tool.SqlConfiguration.Query);
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Run the predefined SQL query or procedure and return its results." : tool.Description);
    }

    private static bool IsFixedSqlToolType(string? toolType)
    {
        if (string.IsNullOrWhiteSpace(toolType)) return false;
        var normalized = toolType.Trim().ToLowerInvariant();
        return normalized == "sqlsinglequery" || normalized == "sqlquery" || normalized == "sqlprocedure" ||
               normalized == "sqlstoredprocedure" || normalized == "fixedsqlquery" || normalized == "sqlsingle";
    }

    private AITool CreateTextFileTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            ExecuteTextFileAsync,
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Read or write text files." : tool.Description);
    }

    private static async Task<string> ExecuteTextFileAsync(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.StartsWith("write:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed["write:".Length..].Split("::", 2, StringSplitOptions.None);
            if (parts.Length < 2)
            {
                return "Usage: write:path::content";
            }
            return await Helpers.TextFileHelper.WriteAllTextAsync(parts[0].Trim(), parts[1]);
        }

        return await Helpers.TextFileHelper.ReadAllTextAsync(trimmed);
    }

    private AITool CreatePdfTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            (string filePath) => Helpers.PdfFileHelper.ReadText(filePath),
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Read text from a PDF file." : tool.Description);
    }

    public AITool CreateAttachedFileTool(Guid userId, IEnumerable<Guid> allowedFileIds)
    {
        var allowed = allowedFileIds?.ToHashSet() ?? new HashSet<Guid>();
        return AIFunctionFactory.Create(
            (string fileToken) => ReadAttachedFileAsync(userId, allowed, fileToken),
            name: "read_attached_file",
            description: "Read one of the files attached to the current message using its file:<id> token.");
    }

    private async Task<string> ReadAttachedFileAsync(Guid userId, HashSet<Guid> allowedFileIds, string fileToken)
    {
        if (string.IsNullOrWhiteSpace(fileToken) || !fileToken.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return "Invalid file token. Expected file:<id>.";
        }

        if (!Guid.TryParse(fileToken["file:".Length..].Trim(), out var fileId))
        {
            return "Invalid file id.";
        }

        if (!allowedFileIds.Contains(fileId))
        {
            return "This file is not attached to the current message.";
        }

        var uploadedFile = await _db.UploadedFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId);

        if (uploadedFile == null)
        {
            return "Uploaded file not found or access denied.";
        }

        var fullPath = Path.GetFullPath(uploadedFile.StoredPath);
        var allowedRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "TempFilesLocal"));
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
        {
            return "Uploaded file path is invalid or file no longer exists.";
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        try
        {
            if (extension == ".pdf")
            {
                return Helpers.PdfFileHelper.ReadText(fullPath);
            }

            if (extension == ".xlsx")
            {
                var table = Helpers.ExcelHelper.ReadSheet(fullPath);
                return System.Text.Json.JsonSerializer.Serialize(table);
            }

            return await System.IO.File.ReadAllTextAsync(fullPath);
        }
        catch (Exception ex)
        {
            return $"Could not read uploaded file: {ex.Message}";
        }
    }

    private AITool CreateFileReaderTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            (string path) => Helpers.FileReaderHelper.ReadFile(path),
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Read a file (text, PDF, or Excel) and return its contents." : tool.Description);
    }

    private AITool CreateTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            async (string input) => await ExecuteAsync(tool, input),
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : tool.Description);
    }

    private async Task<string> ExecuteAsync(ToolDefinition tool, string input)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(tool.ApiConfiguration?.HeadersJson))
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(tool.ApiConfiguration!.HeadersJson!) ?? headers;
        }

        if (string.Equals(tool.ApiConfiguration?.AuthType, "Bearer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tool.ApiConfiguration?.Credential))
        {
            headers["Authorization"] = $"Bearer {tool.ApiConfiguration?.Credential}";
        }
        else if (string.Equals(tool.ApiConfiguration?.AuthType, "ApiKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tool.ApiConfiguration?.Credential))
        {
            headers["X-Api-Key"] = tool.ApiConfiguration?.Credential ?? string.Empty;
        }

        var method = string.IsNullOrWhiteSpace(tool.ApiConfiguration?.HttpMethod) ? "GET" : tool.ApiConfiguration!.HttpMethod.ToUpperInvariant();

        if (method == "GET")
        {
            var endpoint = tool.ApiConfiguration?.Endpoint ?? string.Empty;
            var separator = endpoint.Contains('?') ? "&" : "?";
            var url = $"{endpoint}{separator}input={Uri.EscapeDataString(input)}";
            var response = await _apiHelper.GetAsync(url);
            return ExtractResponse(response, tool.ApiConfiguration?.ResponsePath);
        }

        object payload = string.IsNullOrWhiteSpace(tool.ApiConfiguration?.RequestTemplate)
            ? new { input }
            : (tool.ApiConfiguration!.RequestTemplate!).Replace("{{input}}", input, StringComparison.Ordinal);

        var postResponse = await _apiHelper.PostJsonAsync(tool.ApiConfiguration?.Endpoint ?? string.Empty, payload);
        return ExtractResponse(postResponse, tool.ApiConfiguration?.ResponsePath);
    }

    private static string ExtractResponse(string response, string? responsePath)
    {
        if (string.IsNullOrWhiteSpace(responsePath))
        {
            return response;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var current = document.RootElement;
            foreach (var part in responsePath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                {
                    current = next;
                }
                else
                {
                    return response;
                }
            }
            return current.ToString();
        }
        catch
        {
            return response;
        }
    }

    private async Task<List<AITool>> CreateMcpToolsAsync(ToolDefinition tool, CancellationToken cancellationToken)
    {
        var result = new List<AITool>();
        var endpoint = tool.ApiConfiguration?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return result;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(tool.ApiConfiguration?.HeadersJson))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(tool.ApiConfiguration!.HeadersJson!);
                if (parsed != null)
                {
                    foreach (var (key, value) in parsed)
                    {
                        headers[key] = value;
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                TransportMode = HttpTransportMode.AutoDetect,
                AdditionalHeaders = headers
            };

            await using var transport = new HttpClientTransport(options);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            foreach (var mcpTool in mcpTools)
            {
                var toolName = SanitizeName($"{tool.Name}_{mcpTool.Name}");
                var description = string.IsNullOrWhiteSpace(mcpTool.Description)
                    ? $"{tool.Name}: {mcpTool.Name}"
                    : mcpTool.Description;

                result.Add(AIFunctionFactory.Create(
                    async (AIFunctionArguments arguments, CancellationToken ct) =>
                        await InvokeMcpToolAsync(endpoint, headers, mcpTool.Name, arguments, ct),
                    new AIFunctionFactoryOptions
                    {
                        Name = toolName,
                        Description = description
                    }));
            }
        }
        catch (Exception ex)
        {
            result.Add(AIFunctionFactory.Create(
                (string input) => $"MCP server error ({tool.Name}): {ex.Message}",
                name: SanitizeName(tool.Name),
                description: $"MCP server {tool.Name} is unavailable."));
        }

        return result;
    }

    private static async Task<string> InvokeMcpToolAsync(string endpoint, Dictionary<string, string> headers, string mcpToolName, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = headers
        };

        await using var transport = new HttpClientTransport(options);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in arguments)
        {
            args[key] = value;
        }

        var callResult = await client.CallToolAsync(mcpToolName, args, cancellationToken: cancellationToken);

        var texts = callResult.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (texts.Count > 0)
        {
            return string.Join("\n", texts);
        }

        return callResult.ToString() ?? "MCP tool completed successfully.";
    }

    private static string SanitizeName(string name)
    {
        var result = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(result) || char.IsDigit(result[0]))
        {
            result = "tool_" + result;
        }
        return result;
    }
    private AITool CreateExcelExportTool(ToolDefinition tool, Guid? userId)
    {
        return AIFunctionFactory.Create(
            async (string content) =>
            {
                var folder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", "exports");
                Directory.CreateDirectory(folder);
                var outputPath = Path.Combine(folder, $"export_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.xlsx");
                try
                {
                    var rows = (content ?? string.Empty)
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split(new[] { ',', '\t' }, StringSplitOptions.None).Cast<object?>().ToList())
                        .ToList();
                    Helpers.ExcelHelper.WriteWorkbook(outputPath, new[] { ("Export", rows.Cast<IEnumerable<object?>>()) });

                    var fileInfo = new FileInfo(outputPath);
                    var uploadedFile = new UploadedFile
                    {
                        UserId = userId,
                        SessionKey = "tool-generated",
                        OriginalFileName = fileInfo.Name,
                        StoredPath = Path.GetFullPath(outputPath),
                        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        Size = fileInfo.Length
                    };
                    _db.UploadedFiles.Add(uploadedFile);
                    await _db.SaveChangesAsync();
                    DatabaseToolExecutor.RecordGeneratedFile(uploadedFile.Id);
                    return $"Excel file generated: {fileInfo.Name}. Download token: file:{uploadedFile.Id}";
                }
                catch (Exception ex)
                {
                    return $"Could not generate Excel file: {ex.Message}";
                }
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Export text/CSV input to an Excel file." : tool.Description);
    }

    private AITool CreateTextExportTool(ToolDefinition tool, Guid? userId)
    {
        return AIFunctionFactory.Create(
            async (string content) =>
            {
                var folder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", "exports");
                Directory.CreateDirectory(folder);
                var fileName = $"export_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.txt";
                var outputPath = Path.Combine(folder, fileName);
                try
                {
                    await System.IO.File.WriteAllTextAsync(outputPath, content ?? string.Empty);
                    var fileInfo = new FileInfo(outputPath);
                    var uploadedFile = new UploadedFile
                    {
                        UserId = userId,
                        SessionKey = "tool-generated",
                        OriginalFileName = fileInfo.Name,
                        StoredPath = Path.GetFullPath(outputPath),
                        ContentType = "text/plain",
                        Size = fileInfo.Length
                    };
                    _db.UploadedFiles.Add(uploadedFile);
                    await _db.SaveChangesAsync();
                    DatabaseToolExecutor.RecordGeneratedFile(uploadedFile.Id);
                    return $"Text file generated: {fileInfo.Name}. Download token: file:{uploadedFile.Id}";
                }
                catch (Exception ex)
                {
                    return $"Could not generate text file: {ex.Message}";
                }
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Export text input to a TXT file." : tool.Description);
    }

    private AITool CreateWordExportTool(ToolDefinition tool, Guid? userId)
    {
        return AIFunctionFactory.Create(
            async (string content) =>
            {
                var folder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", "exports");
                Directory.CreateDirectory(folder);
                var fileName = $"export_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.docx";
                var outputPath = Path.Combine(folder, fileName);
                try
                {
                    Helpers.WordFileHelper.WriteDocument(outputPath, content ?? string.Empty);
                    var fileInfo = new FileInfo(outputPath);
                    var uploadedFile = new UploadedFile
                    {
                        UserId = userId,
                        SessionKey = "tool-generated",
                        OriginalFileName = fileInfo.Name,
                        StoredPath = Path.GetFullPath(outputPath),
                        ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        Size = fileInfo.Length
                    };
                    _db.UploadedFiles.Add(uploadedFile);
                    await _db.SaveChangesAsync();
                    DatabaseToolExecutor.RecordGeneratedFile(uploadedFile.Id);
                    return $"Word document generated: {fileInfo.Name}. Download token: file:{uploadedFile.Id}";
                }
                catch (Exception ex)
                {
                    return $"Could not generate Word document: {ex.Message}";
                }
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Export text input to a Word (.docx) file." : tool.Description);

    }

    private AITool CreateSftpTool(ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            (string input) => ExecuteSftpAsync(tool.SftpConfiguration, input),
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "List, upload, or download files over SFTP." : tool.Description);
    }

    private static async Task<string> ExecuteSftpAsync(SftpToolConfiguration? config, string input)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.Username))
        {
            return "No SFTP configuration is available for this tool.";
        }

        var trimmed = (input ?? string.Empty).Trim();
        try
        {
            using var client = new SftpClient(config.Host, config.Port, config.Username, config.Password ?? string.Empty);
            client.Connect();

            if (trimmed.StartsWith("list", StringComparison.OrdinalIgnoreCase))
            {
                var path = string.IsNullOrWhiteSpace(trimmed["list".Length..].Trim(':')) ? config.RemoteBasePath : trimmed["list".Length..].Trim().TrimStart(':').Trim();
                if (string.IsNullOrWhiteSpace(path)) path = ".";
                var entries = client.ListDirectory(path)
                    .Where(e => !e.IsDirectory || (e.Name != "." && e.Name != ".."))
                    .Select(e => e.Name + (e.IsDirectory ? "/" : ""))
                    .ToList();
                return System.Text.Json.JsonSerializer.Serialize(entries);
            }

            if (trimmed.StartsWith("upload:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed["upload:".Length..].Split("::", 2, StringSplitOptions.None);
                if (parts.Length < 2)
                {
                    return "Usage: upload:localPath::remotePath";
                }
                var localPath = parts[0].Trim();
                var remotePath = parts[1].Trim();
                if (!System.IO.File.Exists(localPath))
                {
                    return $"Local file not found: {localPath}";
                }
                using var stream = System.IO.File.OpenRead(localPath);
                client.UploadFile(stream, remotePath);
                return $"Uploaded {localPath} to {remotePath}.";
            }

            if (trimmed.StartsWith("download:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed["download:".Length..].Split("::", 2, StringSplitOptions.None);
                if (parts.Length < 2)
                {
                    return "Usage: download:remotePath::localPath";
                }
                var remotePath = parts[0].Trim();
                var localPath = parts[1].Trim();
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                using var stream = System.IO.File.Create(localPath);
                client.DownloadFile(remotePath, stream);
                return $"Downloaded {remotePath} to {localPath}.";
            }

            return "Usage: list [path], upload:localPath::remotePath, or download:remotePath::localPath";
        }
        catch (Exception ex)
        {
            return $"SFTP operation failed: {ex.Message}";
        }
    }

    private AITool CreateSqlToExcelTool(ToolDefinition tool, Guid? userId)
    {
        return AIFunctionFactory.Create(
            async (string input) =>
            {
                if (string.IsNullOrWhiteSpace(tool.SqlConfiguration?.ConnectionString))
                {
                    return "No SQL configuration is available for this tool.";
                }

                var trimmed = (input ?? string.Empty).Trim();
                var query = trimmed;
                var outputPath = string.Empty;

                var separatorIndex = trimmed.LastIndexOf("::");
                if (separatorIndex > 0)
                {
                    query = trimmed[..separatorIndex].Trim();
                    outputPath = trimmed[(separatorIndex + 2)..].Trim();
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Usage: <SQL query> or <SQL query>::outputPath";
                }

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    var folder = Path.Combine(_environment.ContentRootPath, "TempFilesLocal", "exports");
                    Directory.CreateDirectory(folder);
                    outputPath = Path.Combine(folder, $"query_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.xlsx");
                }

                try
                {
                    var table = await Helpers.SqlDatabaseReader.ExecuteReadOnlyQueryToTableAsync(tool.SqlConfiguration!.ConnectionString, tool.SqlConfiguration.DatabaseType, query);
                    var rows = table.Rows.Cast<DataRow>().Select(row => row.ItemArray.Select(value => value == DBNull.Value ? null : value).Cast<object?>());
                    Helpers.ExcelHelper.WriteWorkbook(outputPath, new[] { ("Query Results", rows) });
                    var fileInfo = new FileInfo(outputPath);
                    var uploadedFile = new UploadedFile
                    {
                        UserId = userId,
                        SessionKey = "tool-generated",
                        OriginalFileName = fileInfo.Name,
                        StoredPath = Path.GetFullPath(outputPath),
                        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        Size = fileInfo.Length
                    };
                    _db.UploadedFiles.Add(uploadedFile);
                    await _db.SaveChangesAsync();
                    DatabaseToolExecutor.RecordGeneratedFile(uploadedFile.Id);
                    return $"Excel file generated: {fileInfo.Name} ({table.Rows.Count} rows). Download token: file:{uploadedFile.Id}";
                }
                catch (Exception ex)
                {
                    return $"Could not generate Excel file: {ex.Message}";
                }
            },
            name: SanitizeName(tool.Name),
            description: string.IsNullOrWhiteSpace(tool.Description) ? "Run a read-only SQL query and generate an Excel file." : tool.Description);
    }

}
