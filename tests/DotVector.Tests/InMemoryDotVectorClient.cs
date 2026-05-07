using DotVector.Core;
using DotVector.Core.Protocol;

namespace DotVector.Tests;

/// <summary>
/// 用于测试 DotVector.Data 适配层的内存实现 <see cref="IDotVectorClient"/>。
/// 使用暴力余弦相似度搜索；不进行任何持久化。
/// </summary>
internal sealed class InMemoryDotVectorClient : IDotVectorClient
{
    private readonly Dictionary<string, Collection> _collections = new(StringComparer.Ordinal);

    /// <summary>统计 CreateCollection 调用次数，便于断言。</summary>
    public int CreateCollectionCalls { get; private set; }

    /// <summary>已知的集合名称只读视图。</summary>
    public IReadOnlyCollection<string> CollectionNames => _collections.Keys;

    /// <summary>获取指定集合中已存储的记录数（若集合不存在抛 <see cref="KeyNotFoundException"/>）。</summary>
    public int RecordCount(string name) => _collections[name].Records.Count;

    public ValueTask CreateCollectionAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CreateCollectionCalls++;
        _collections[request.Name] = new Collection(request.Dimensions, request.Metric);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        _collections.Remove(collectionName);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpsertAsync(string collectionName, IReadOnlyList<VectorUpsertRecord> records, CancellationToken cancellationToken = default)
    {
        var col = _collections[collectionName];
        foreach (var r in records)
        {
            col.Records[r.Id] = (r.Vector, r.Payload);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string collectionName, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var col = _collections[collectionName];
        foreach (var id in ids)
        {
            col.Records.Remove(id);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<VectorSearchResult>> SearchAsync(string collectionName, VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        var col = _collections[collectionName];
        var query = request.QueryVector;
        var hits = new List<VectorSearchResult>();
        foreach (var (id, value) in col.Records)
        {
            var score = CosineSimilarity(query, value.Vector);
            hits.Add(new VectorSearchResult(id, score)
            {
                Payload = value.Payload,
                Vector = request.IncludeVector ? (float[])value.Vector.Clone() : null,
            });
        }
        IReadOnlyList<VectorSearchResult> result = hits
            .OrderByDescending(h => h.Score)
            .Take(request.TopK)
            .ToList();
        return ValueTask.FromResult(result);
    }

    public ValueTask<IReadOnlyList<VectorRecordDto>> GetAsync(
        string collectionName,
        IReadOnlyList<string> ids,
        bool includeVector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var col = _collections[collectionName];
        var results = new List<VectorRecordDto>(ids.Count);
        foreach (var id in ids)
        {
            if (col.Records.TryGetValue(id, out var value))
            {
                results.Add(new VectorRecordDto(id)
                {
                    Vector = includeVector ? (float[])value.Vector.Clone() : null,
                    Payload = value.Payload,
                });
            }
        }
        return ValueTask.FromResult<IReadOnlyList<VectorRecordDto>>(results);
    }

    public ValueTask<bool> PingAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static float CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-12));
    }

    private sealed class Collection
    {
        public Collection(int dimensions, string metric)
        {
            Dimensions = dimensions;
            Metric = metric;
        }

        public int Dimensions { get; }
        public string Metric { get; }
        public Dictionary<string, (float[] Vector, IReadOnlyDictionary<string, object>? Payload)> Records { get; } = new();
    }
}
