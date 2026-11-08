using System.IO;
using System.Text.Json;

namespace BuildWorkflow.AICenter.Helpers;

public static class TextFileHelper
{
    private static readonly string AllowedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TempFilesLocal"));

    private static void ValidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(AllowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Access denied: Path must be within {AllowedRoot}");
        }
    }

    public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public static async Task<string> WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ValidatePath(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return $"File written: {path}";
    }

    public static object ToPreview(string content)
    {
        return new { length = content.Length, preview = content.Length > 500 ? content[..500] + "..." : content };
    }
}
