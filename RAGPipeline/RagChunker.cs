using System.Text;

namespace BuildWorkflow.RAGPipeline;

public static class RagChunker
{
    public static IEnumerable<string> Chunk(string content, int chunkSize = 1000, int overlap = 100)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        var normalized = content.Replace("\r\n", "\n").Trim();
        var position = 0;

        while (position < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - position);
            var chunk = normalized.Substring(position, length).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return chunk;
            }

            if (position + length >= normalized.Length)
            {
                break;
            }

            position += Math.Max(1, length - overlap);
        }
    }
}