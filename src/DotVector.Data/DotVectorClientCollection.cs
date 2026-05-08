using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Query;

namespace DotVector.Data;

/// <summary>
/// 高层客户端集合句柄。提供 Upsert / Delete / Get / Search / Query 等统一 API。
/// </summary>
/// <remarks>
/// 此类型是无状态的薄包装；并发调用安全，与底层 <see cref="IDotVectorClient"/> 的并发约束一致。
/// 命名有意区别于 VectorData 适配层的 <see cref="DotVectorCollection{TKey,TRecord}"/>。
/// </remarks>
public sealed class DotVectorClientCollection
{
    private readonly IDotVectorClient _client;

    /// <summary>构造集合句柄。</summary>
    /// <param name="client">底层协议客户端。</param>
    /// <param name="name">集合名称。</param>
    public DotVectorClientCollection(IDotVectorClient client, string name)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _client = client;
        Name = name;
    }

    /// <summary>集合名称。</summary>
    public string Name { get; }

    /// <summary>插入或更新单条记录。</summary>
    public ValueTask UpsertAsync(
        string id,
        float[] vector,
        IReadOnlyDictionary<string, object>? payload = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(vector);
        var record = new VectorUpsertRecord(id, vector) { Payload = payload };
        return _client.UpsertAsync(Name, [record], ct);
    }

    /// <summary>批量插入或更新。</summary>
    public ValueTask UpsertAsync(IEnumerable<Point> points, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = new List<VectorUpsertRecord>();
        foreach (Point p in points)
        {
            list.Add(new VectorUpsertRecord(p.Id, p.Vector) { Payload = p.Payload });
        }
        return _client.UpsertAsync(Name, list, ct);
    }

    /// <summary>
    /// 批量插入或更新（扁平向量重载，便于 C / Python / 其它语言连接器复用）。
    /// </summary>
    public ValueTask UpsertBatchAsync(
        IReadOnlyList<string> ids,
        ReadOnlyMemory<float> flatVectors,
        int dimension,
        IReadOnlyList<IReadOnlyDictionary<string, object>?>? payloads = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        if (flatVectors.Length != ids.Count * dimension)
        {
            throw new ArgumentException(
                $"flatVectors 长度（{flatVectors.Length}）不等于 ids.Count * dimension（{ids.Count * dimension}）。",
                nameof(flatVectors));
        }
        if (payloads is not null && payloads.Count != ids.Count)
        {
            throw new ArgumentException("payloads 长度必须等于 ids 长度。", nameof(payloads));
        }

        var records = new List<VectorUpsertRecord>(ids.Count);
        ReadOnlySpan<float> span = flatVectors.Span;
        for (int i = 0; i < ids.Count; i++)
        {
            var vector = new float[dimension];
            span.Slice(i * dimension, dimension).CopyTo(vector);
            records.Add(new VectorUpsertRecord(ids[i], vector)
            {
                Payload = payloads?[i],
            });
        }
        return _client.UpsertAsync(Name, records, ct);
    }

    /// <summary>按 ID 删除一条或多条记录。</summary>
    public ValueTask DeleteAsync(params string[] ids)
        => _client.DeleteAsync(Name, ids, default);

    /// <summary>按 ID 批量删除。</summary>
    public ValueTask DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
        => _client.DeleteAsync(Name, ids, ct);

    /// <summary>按 ID 取回若干条记录。</summary>
    public async ValueTask<IReadOnlyList<Point>> GetAsync(
        IReadOnlyList<string> ids,
        bool includeVector = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        IReadOnlyList<VectorRecordDto> raw = await _client.GetAsync(Name, ids, includeVector, ct).ConfigureAwait(false);
        var list = new List<Point>(raw.Count);
        foreach (VectorRecordDto r in raw)
        {
            list.Add(new Point(r.Id, r.Vector ?? [], r.Payload));
        }
        return list;
    }

    /// <summary>近似最近邻搜索。</summary>
    public async ValueTask<IReadOnlyList<ScoredPoint>> SearchAsync(
        float[] queryVector,
        int topK = 10,
        Filter? filter = null,
        bool includeVector = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var req = new VectorSearchRequest(queryVector, topK)
        {
            Filter = filter,
            IncludeVector = includeVector,
        };
        IReadOnlyList<VectorSearchResult> raw = await _client.SearchAsync(Name, req, ct).ConfigureAwait(false);
        var list = new List<ScoredPoint>(raw.Count);
        foreach (VectorSearchResult r in raw)
        {
            list.Add(new ScoredPoint(r.Id, r.Score, r.Payload, r.Vector));
        }
        return list;
    }

    /// <summary>仅按 payload 过滤检索。</summary>
    public async ValueTask<IReadOnlyList<Point>> QueryAsync(
        Filter filter,
        int top = 100,
        bool includeVector = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        var req = new VectorScrollRequest(filter, top) { IncludeVector = includeVector };
        IReadOnlyList<VectorRecordDto> raw = await _client.ScrollAsync(Name, req, ct).ConfigureAwait(false);
        var list = new List<Point>(raw.Count);
        foreach (VectorRecordDto r in raw)
        {
            list.Add(new Point(r.Id, r.Vector ?? [], r.Payload));
        }
        return list;
    }

    /// <summary>读取本集合的元数据。集合不存在时返回 <see langword="null"/>。</summary>
    public async ValueTask<CollectionInfo?> DescribeAsync(CancellationToken ct = default)
    {
        IReadOnlyList<CollectionInfo> all = await _client.ListCollectionsAsync(ct).ConfigureAwait(false);
        for (int i = 0; i < all.Count; i++)
        {
            if (string.Equals(all[i].Name, Name, StringComparison.Ordinal))
            {
                return all[i];
            }
        }
        return null;
    }

    /// <summary>记录数（基于 <see cref="DescribeAsync"/>，不存在返回 0）。</summary>
    public async ValueTask<long> CountAsync(CancellationToken ct = default)
    {
        CollectionInfo? info = await DescribeAsync(ct).ConfigureAwait(false);
        return info?.RecordCount ?? 0;
    }
}
