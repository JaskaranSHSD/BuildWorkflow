using System.Data;
using System.IO;
using System.Text.Json;

namespace BuildWorkflow.AICenter.Helpers;

public static class FileReaderHelper
{
    public static string ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "A file path is required.";
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return "File not found: " + fullPath;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();

        if (extension == ".pdf")
        {
            return PdfFileHelper.ReadText(fullPath);
        }

        if (extension == ".docx")
        {
            return WordFileHelper.ReadText(fullPath);
        }

        if (extension == ".xlsx")
        {
            var table = ExcelHelper.ReadSheet(fullPath);
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
            return JsonSerializer.Serialize(rows);
        }

        return File.ReadAllText(fullPath);
    }
}
