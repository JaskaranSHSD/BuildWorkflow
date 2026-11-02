using BuildWorkflow.AICenter.Helpers;
using System.Data;
using System.Text;

namespace BuildWorkflow.RAGPipeline;

public static class RagFileTextExtractor
{
    public static string ExtractText(string path, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".csv" or ".md" => File.ReadAllText(path),
            ".pdf" => PdfFileHelper.ReadText(path),
            ".docx" => WordFileHelper.ReadText(path),
            ".xlsx" or ".xls" => ExtractExcel(path),
            _ => TryReadText(path)
        };
    }

    private static string ExtractExcel(string path)
    {
        var table = ExcelHelper.ReadSheet(path);
        var builder = new StringBuilder();
        foreach (DataRow row in table.Rows)
        {
            builder.AppendLine(string.Join(" | ", row.ItemArray.Select(value => value?.ToString() ?? string.Empty)));
        }
        return builder.ToString();
    }

    private static string TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }
}