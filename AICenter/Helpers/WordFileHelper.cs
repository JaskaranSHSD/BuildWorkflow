using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace BuildWorkflow.AICenter.Helpers;

public static class WordFileHelper
{
    public static string ReadText(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return "File not found: " + fullPath;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension != ".docx")
        {
            return "Word (.docx) files are supported; legacy .doc files are not supported on this platform.";
        }

        try
        {
            var builder = new StringBuilder();
            using var document = WordprocessingDocument.Open(fullPath, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                return string.Empty;
            }

            foreach (var element in body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    builder.AppendLine(paragraph.InnerText);
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>()
                            .Select(cell => string.Join(" ", cell.Elements<Paragraph>().Select(p => p.InnerText)))
                            .ToList();
                        builder.AppendLine(string.Join(" | ", cells));
                    }
                }
            }

            // Include headers and footers when present.
            foreach (var headerPart in document.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                if (headerPart.Header != null)
                {
                    builder.AppendLine(headerPart.Header.InnerText);
                }
            }
            foreach (var footerPart in document.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                if (footerPart.Footer != null)
                {
                    builder.AppendLine(footerPart.Footer.InnerText);
                }
            }

            return builder.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"Could not read Word file: {ex.Message}";
        }
    }

    public static void WriteDocument(string path, string? content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        foreach (var line in (content ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var paragraph = new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve }));
            body.Append(paragraph);
        }

        mainPart.Document.Append(body);
        mainPart.Document.Save();
    }
}