using UglyToad.PdfPig;

namespace BuildWorkflow.AICenter.Helpers;

public static class PdfFileHelper
{
    public static string ReadText(string path)
    {
        using var document = PdfDocument.Open(path);
        var pages = new List<string>();

        foreach (var page in document.GetPages())
        {
            pages.Add(page.Text);
        }

        return string.Join(Environment.NewLine, pages);
    }
}
