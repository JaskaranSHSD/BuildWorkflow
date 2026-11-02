namespace BuildWorkflow.RAGPipeline;

public static class RagContextBuilder
{
    public static string Build(string query, IEnumerable<string> documents, int maxCharacters = 6000)
    {
        var tokens = query.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .Distinct()
            .ToList();

        var chunks = documents
            .SelectMany(document => document.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            .Select(chunk => new { Chunk = chunk.Trim(), Score = Score(chunk, tokens) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Chunk))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.Length)
            .ToList();

        var selected = new List<string>();
        var total = 0;
        foreach (var item in chunks)
        {
            if (total + item.Chunk.Length > maxCharacters)
            {
                continue;
            }
            selected.Add(item.Chunk);
            total += item.Chunk.Length;
            if (selected.Count >= 12)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            foreach (var document in documents)
            {
                if (total + document.Length > maxCharacters)
                {
                    break;
                }
                selected.Add(document);
                total += document.Length;
            }
        }

        return string.Join("\n\n", selected);
    }

    private static int Score(string chunk, IEnumerable<string> tokens)
    {
        var lower = chunk.ToLowerInvariant();
        return tokens.Sum(token => CountOccurrences(lower, token));
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}