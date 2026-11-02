using System.IO;
using System.Text.Json;

namespace BuildWorkflow.AICenter.Helpers;

public static class TextFileHelper
{
    public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public static async Task<string> WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return $"File written: {path}";
    }

    public static object ToPreview(string content)
    {
        return new { length = content.Length, preview = content.Length > 500 ? content[..500] + "..." : content };
    }
}
