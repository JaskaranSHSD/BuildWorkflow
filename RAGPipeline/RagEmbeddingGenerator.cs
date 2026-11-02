using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;

namespace BuildWorkflow.RAGPipeline;

public static class RagEmbeddingGenerator
{
    private static readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<float[]?> GenerateAsync(string endpoint, string? model, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var client = _clients.GetOrAdd(endpoint, static e => new HttpClient
            {
                BaseAddress = new Uri(e.EndsWith("/") ? e : e + "/"),
                Timeout = TimeSpan.FromSeconds(60)
            });

            var response = await client.PostAsJsonAsync("api/embeddings", new { model, prompt = text.Length > 8000 ? text[..8000] : text }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (json.TryGetProperty("embedding", out var embeddingElement) && embeddingElement.ValueKind == JsonValueKind.Array)
            {
                var values = new List<float>();
                foreach (var item in embeddingElement.EnumerateArray())
                {
                    values.Add(item.GetSingle());
                }
                return values.ToArray();
            }
        }
        catch
        {
            // Embedding is optional; retrieval falls back to keyword search.
        }

        return null;
    }

    public static string? ToJson(float[]? vector)
    {
        return vector == null ? null : JsonSerializer.Serialize(vector);
    }

    public static float[]? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(json);
        }
        catch
        {
            return null;
        }
    }

    public static double CosineSimilarity(float[]? left, float[]? right)
    {
        if (left == null || right == null || left.Length == 0 || left.Length != right.Length)
        {
            return 0;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}