using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildWorkflow.Services;

public class QdrantRagStore
{
    private readonly QdrantClient _client;
    private readonly string _collection;
    private readonly int _configuredDimension;
    private int? _dimension;
    private readonly ILogger<QdrantRagStore>? _logger;

    public QdrantRagStore(IConfiguration configuration, ILogger<QdrantRagStore>? logger = null)
    {
        var endpoint = configuration["Qdrant:Endpoint"] ?? "http://localhost:6333";
        var apiKey = configuration["Qdrant:ApiKey"];
        _collection = configuration["Qdrant:Collection"] ?? "rag_chunks";
        _configuredDimension = configuration.GetValue<int?>("Qdrant:VectorSize") ?? 768;
        _logger = logger;
        var uri = new Uri(endpoint);
        var port = uri.Port;
        if (port == 6333) port = 6334;
        _client = new QdrantClient(uri.Host, port, https: uri.Scheme == "https", apiKey: string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
    }

    public async Task UpsertChunksAsync(Guid bucketId, Guid documentId, IEnumerable<(string Content, float[]? Embedding)> chunks, CancellationToken ct = default)
    {
        var points = new List<PointStruct>();
        foreach (var (content, embedding) in chunks)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            var vector = embedding;
            if (vector == null || vector.Length == 0)
            {
                vector = new float[_dimension ?? _configuredDimension];
            }
            await EnsureCollectionAsync(vector.Length, ct);
            points.Add(new PointStruct
            {
                Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = vector,
                Payload =
                {
                    ["bucket_id"] = bucketId.ToString(),
                    ["document_id"] = documentId.ToString(),
                    ["content"] = content
                }
            });
        }

        if (points.Count == 0) return;
        await _client.UpsertAsync(_collection, points, cancellationToken: ct);
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var filter = new Filter
        {
            Must = { new Condition { Field = new FieldCondition { Key = "document_id", Match = new Match { Keyword = documentId.ToString() } } } }
        };
        await _client.DeleteAsync(_collection, filter, cancellationToken: ct);
    }

    public async Task DeleteBucketAsync(Guid bucketId, CancellationToken ct = default)
    {
        var filter = new Filter
        {
            Must = { new Condition { Field = new FieldCondition { Key = "bucket_id", Match = new Match { Keyword = bucketId.ToString() } } } }
        };
        await _client.DeleteAsync(_collection, filter, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(IEnumerable<Guid> bucketIds, string query, float[]? queryEmbedding, int maxScan, CancellationToken ct = default)
    {
        var ids = bucketIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
        if (ids.Count == 0) return Array.Empty<string>();

        if (queryEmbedding != null && queryEmbedding.Length > 0)
        {
            try
            {
                var filter = BuildBucketFilter(ids);
                var results = await _client.SearchAsync(_collection, queryEmbedding, filter: filter, limit: 12, cancellationToken: ct);
                _logger?.LogInformation("Qdrant vector search buckets={Buckets} returned={Count}", string.Join(",", ids), results.Count);
                return results.Select(r => r.Payload.TryGetValue("content", out var v) ? v.StringValue ?? string.Empty : string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
            catch { }
        }

        var collected = new List<string>();
        try
        {
            var filter = BuildBucketFilter(ids);
            var scroll = await _client.ScrollAsync(_collection, filter: filter, limit: (uint)Math.Max(1, maxScan), cancellationToken: ct);
            collected.AddRange(scroll.Result.Select(p => p.Payload.TryGetValue("content", out var v) ? v.StringValue ?? string.Empty : string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            _logger?.LogInformation("Qdrant scroll buckets={Buckets} returned={Count}", string.Join(",", ids), scroll.Result.Count);
        }
        catch { }
        return collected;
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchHitsAsync(IEnumerable<Guid> bucketIds, string query, float[]? queryEmbedding, int limit, CancellationToken ct = default)
    {
        var ids = bucketIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
        if (ids.Count == 0) return Array.Empty<KnowledgeHit>();

        if (queryEmbedding != null && queryEmbedding.Length > 0)
        {
            try
            {
                var filter = BuildBucketFilter(ids);
                var results = await _client.SearchAsync(_collection, queryEmbedding, filter: filter, limit: (ulong)limit, cancellationToken: ct);
                _logger?.LogInformation("Knowledge vector search returned {Count}", results.Count);
                return results.Select(r =>
                {
                    var content = r.Payload.TryGetValue("content", out var v) ? v.StringValue ?? string.Empty : string.Empty;
                    var bucketId = r.Payload.TryGetValue("bucket_id", out var b) && Guid.TryParse(b.StringValue, out var bg) ? bg : Guid.Empty;
                    var documentId = r.Payload.TryGetValue("document_id", out var d) && Guid.TryParse(d.StringValue, out var dg) ? dg : Guid.Empty;
                    return new KnowledgeHit(content, r.Score, bucketId, documentId);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Knowledge vector search failed");
            }
        }

        var collected = new List<KnowledgeHit>();
        try
        {
            var filter = BuildBucketFilter(ids);
            var scroll = await _client.ScrollAsync(_collection, filter: filter, limit: (uint)Math.Max(1, limit), cancellationToken: ct);
            var scrollTokens = query.ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2)
                .Distinct()
                .ToList();
            foreach (var p in scroll.Result)
            {
                var content = p.Payload.TryGetValue("content", out var v) ? v.StringValue ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(content)) continue;
                var bucketId = p.Payload.TryGetValue("bucket_id", out var b) && Guid.TryParse(b.StringValue, out var bg) ? bg : Guid.Empty;
                var documentId = p.Payload.TryGetValue("document_id", out var d) && Guid.TryParse(d.StringValue, out var dg) ? dg : Guid.Empty;
                var keywordScore = scrollTokens.Sum(t => CountOccurrences(content.ToLowerInvariant(), t));
                collected.Add(new KnowledgeHit(content, keywordScore, bucketId, documentId));
            }
            _logger?.LogInformation("Knowledge scroll returned {Count}", scroll.Result.Count);
            var tokens = query.ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2)
                .Distinct()
                .ToList();
            var ranked = collected
                .Select(h => new { Hit = h, Score = tokens.Sum(t => CountOccurrences(h.Content.ToLowerInvariant(), t)) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Hit.Content.Length)
                .Take(limit)
                .Select(x => x.Hit)
                .ToList();
            if (ranked.Any(x => tokens.Sum(t => CountOccurrences(x.Content.ToLowerInvariant(), t)) > 0))
            {
                return ranked;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Knowledge scroll failed");
        }
        return collected;
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

    private Filter BuildBucketFilter(List<Guid> ids)
    {
        var filter = new Filter();
        if (ids.Count == 1)
        {
            filter.Must.Add(new Condition { Field = new FieldCondition { Key = "bucket_id", Match = new Match { Keyword = ids[0].ToString() } } });
        }
        else
        {
            var should = new Filter();
            foreach (var id in ids)
            {
                should.Should.Add(new Condition { Field = new FieldCondition { Key = "bucket_id", Match = new Match { Keyword = id.ToString() } } });
            }
            filter.Must.Add(new Condition { Filter = should });
        }
        return filter;
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken ct)
    {
        if (_dimension.HasValue && _dimension.Value == vectorSize) return;
        try
        {
            var collections = await _client.ListCollectionsAsync(ct);
            if (!collections.Contains(_collection))
            {
                await _client.CreateCollectionAsync(_collection, new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine }, cancellationToken: ct);
            }
            _dimension = vectorSize;
        }
        catch { }
    }
}

public record KnowledgeHit(string Content, double Score, Guid BucketId, Guid DocumentId);
