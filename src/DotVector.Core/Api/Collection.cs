using System.Buffers;
using System.Collections.Concurrent;
using DotVector.Core;
using DotVector.Index.Flat;
using DotVector.Index.Hnsw;
using DotVector.Index.Ivf;
using DotVector.Model;
using DotVector.Query;
using DotVector.Storage;

namespace DotVector.Api;

/// <summary>
/// 单个向量集合，封装索引与搜索操作。
/// </summary>
/// <typeparam name="TKey">记录主键类型。</typeparam>
/// <remarks>
/// M2 实现：底层默认使用 <see cref="FlatIndex{TKey}"/>（线性扫描精确检索）。
/// M3 起：可选用 <see cref="HnswIndex{TKey}"/>（图索引近似检索，召回率 ≥ 0.95）。
/// M4 起：可选用 <see cref="IvfFlatIndex{TKey}"/> / <see cref="IvfPqIndex{TKey}"/>（倒排聚类 + 乘积量化）。
/// </remarks>
public sealed class Collection<TKey> : IDisposable
    where TKey : notnull
{
    private readonly IIndex<TKey> _index;
    private readonly ConcurrentDictionary<TKey, IReadOnlyDictionary<string, object?>> _payloads = new();
    private IWriteSink<TKey>? _writeSink;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="Collection{TKey}"/> 的新实例（内部构造，由 VectorDatabase 调用）。
    /// </summary>
    /// <param name="name">集合名称。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="metric">距离度量类型。</param>
    /// <param name="indexKind">索引类型。</param>
    /// <param name="hnswOptions">当 <paramref name="indexKind"/> 为 <see cref="IndexKind.Hnsw"/> 时使用的参数；为 <see langword="null"/> 时使用默认值。</param>
    /// <param name="ivfOptions">当 <paramref name="indexKind"/> 为 <see cref="IndexKind.IvfFlat"/> 时使用的参数；为 <see langword="null"/> 时使用默认值。</param>
    /// <param name="ivfPqOptions">当 <paramref name="indexKind"/> 为 <see cref="IndexKind.IvfPq"/> 时使用的参数；为 <see langword="null"/> 时使用默认值。</param>
    internal Collection(
        string name,
        int dimensions,
        Metric metric,
        IndexKind indexKind = IndexKind.Flat,
        HnswOptions? hnswOptions = null,
        IvfOptions? ivfOptions = null,
        IvfPqOptions? ivfPqOptions = null)
    {
        Name = name;
        Dimensions = dimensions;
        Metric = metric;
        IndexKind = indexKind;
        _index = indexKind switch
        {
            IndexKind.Flat => new FlatIndex<TKey>(dimensions, metric),
            IndexKind.Hnsw => new HnswIndex<TKey>(dimensions, metric, hnswOptions),
            IndexKind.IvfFlat => new IvfFlatIndex<TKey>(dimensions, metric, ivfOptions),
            IndexKind.IvfPq => new IvfPqIndex<TKey>(dimensions, metric, ivfPqOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(indexKind), indexKind, "未支持的索引类型。"),
        };
    }

    /// <summary>
    /// 设置写入观察者（仅供持久化层 <see cref="DotVector.Storage.PersistentDirectory"/> 使用）。
    /// 设置后，<see cref="Insert"/> / <see cref="InsertBatch"/> / <see cref="Delete"/>
    /// 会在修改索引之前先通知 sink。回放期间应保持为 <see langword="null"/> 以避免重复写 WAL。
    /// </summary>
    internal void AttachWriteSink(IWriteSink<TKey>? sink) => _writeSink = sink;

    /// <summary>集合名称。</summary>
    public string Name { get; }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>距离度量类型。</summary>
    public Metric Metric { get; }

    /// <summary>底层使用的索引类型。</summary>
    public IndexKind IndexKind { get; }

    /// <summary>当前集合中的向量条数。</summary>
    public long Count => _index.Count;

    /// <summary>
    /// 插入单条向量记录。
    /// </summary>
    /// <param name="record">要插入的向量记录。</param>
    /// <exception cref="ArgumentException">向量维度不匹配，或键已存在。</exception>
    public void Insert(VectorRecord<TKey> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ThrowIfDisposed();
        _writeSink?.OnInsert(record.Key, record.Vector);
        _index.Add(record.Key, record.Vector);
        StorePayload(record.Key, record.Payload);
    }

    /// <summary>
    /// 批量插入向量记录。
    /// </summary>
    /// <param name="records">要插入的向量记录集合。</param>
    /// <remarks>
    /// 对 <see cref="IndexKind.Flat"/> 集合使用专门的原子批量接口；
    /// 对其它索引（如 HNSW）退化为逐条插入。
    /// </remarks>
    public void InsertBatch(IEnumerable<VectorRecord<TKey>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        ThrowIfDisposed();

        var list = records as IReadOnlyList<VectorRecord<TKey>>
            ?? new List<VectorRecord<TKey>>(records);
        int n = list.Count;
        if (n == 0)
        {
            return;
        }

        var keys = new TKey[n];
        var packed = new float[(long)n * Dimensions];
        Span<float> dst = packed;
        for (int i = 0; i < n; i++)
        {
            VectorRecord<TKey> r = list[i];
            ArgumentNullException.ThrowIfNull(r, nameof(records));
            if (r.Vector.Length != Dimensions)
            {
                throw new ArgumentException(
                    $"records[{i}] 向量维度不匹配：期望 {Dimensions}，实际 {r.Vector.Length}。",
                    nameof(records));
            }
            keys[i] = r.Key;
            r.Vector.AsSpan().CopyTo(dst.Slice(i * Dimensions, Dimensions));
        }

        if (_writeSink is not null)
        {
            ReadOnlySpan<float> srcSink = packed;
            for (int i = 0; i < n; i++)
            {
                _writeSink.OnInsert(keys[i], srcSink.Slice(i * Dimensions, Dimensions));
            }
        }

        if (_index is FlatIndex<TKey> flat)
        {
            flat.AddBatch(keys, packed);
        }
        else
        {
            ReadOnlySpan<float> src = packed;
            for (int i = 0; i < n; i++)
            {
                _index.Add(keys[i], src.Slice(i * Dimensions, Dimensions));
            }
        }

        for (int i = 0; i < n; i++)
        {
            StorePayload(keys[i], list[i].Payload);
        }
    }

    /// <summary>
    /// 删除指定主键的记录。
    /// </summary>
    /// <param name="key">要删除的记录主键。</param>
    /// <returns>删除成功返回 <see langword="true"/>；未找到返回 <see langword="false"/>。</returns>
    public bool Delete(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();
        _writeSink?.OnDelete(key);
        bool removed = _index.Remove(key);
        _payloads.TryRemove(key, out _);
        return removed;
    }

    /// <summary>
    /// 获取指定键的标量 payload（M6）。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <returns>payload 字典；若记录不存在或未提供 payload 则返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// payload 仅保存在内存中，<b>不写入 WAL</b>，重启后会丢失。
    /// 完整的 payload 持久化将在后续 milestone 与 Segment 落盘一并实现。
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? GetPayload(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();
        return _payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? payload)
            ? payload
            : null;
    }

    /// <summary>
    /// 执行近似最近邻（ANN）搜索，返回最相似的 K 条记录。
    /// </summary>
    /// <param name="query">查询向量（维度须与集合一致）。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <returns>按相似度排序的搜索结果列表。</returns>
    /// <remarks>
    /// 分数语义：对 L2 / Cosine / DotProduct 越小越相似；对 InnerProduct 越大越相似。
    /// </remarks>
    public IReadOnlyList<SearchResult<TKey>> Search(
        ReadOnlySpan<float> query,
        int topK = 10)
        => Search(query, topK, filter: null);

    /// <summary>
    /// 带标量过滤的近似最近邻搜索（M6）。
    /// </summary>
    /// <param name="query">查询向量（维度须与集合一致）。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <param name="filter">标量过滤表达式；为 <see langword="null"/> 时与无过滤 <see cref="Search(ReadOnlySpan{float}, int)"/> 等价。</param>
    /// <returns>满足过滤条件且按相似度排序的前 <paramref name="topK"/> 条结果。</returns>
    /// <remarks>
    /// 实现策略：向底层索引过取 (over-fetch) 后在 Collection 层进行 post-filter。
    /// 这使用于任何 <see cref="IIndex{TKey}"/> 实现，代价是高选择率过滤下召回率会下降；
    /// 在 M6 范围内默认过取倍率为 8。后续可在各索引内部推动谓词下推。
    /// </remarks>
    public IReadOnlyList<SearchResult<TKey>> Search(
        ReadOnlySpan<float> query,
        int topK,
        Filter? filter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        if (query.Length != Dimensions)
        {
            throw new ArgumentException(
                $"查询向量维度不匹配：期望 {Dimensions}，实际 {query.Length}。",
                nameof(query));
        }
        ThrowIfDisposed();

        int fetch = topK;
        if (filter is not null)
        {
            long count = _index.Count;
            if (count == 0)
            {
                return Array.Empty<SearchResult<TKey>>();
            }
            long desired = Math.Max((long)topK * 8, topK + 32L);
            fetch = (int)Math.Min(desired, count);
        }

        var pool = ArrayPool<(TKey Key, float Score)>.Shared;
        (TKey Key, float Score)[] buffer = pool.Rent(fetch);
        try
        {
            int written = _index.Search(query, fetch, buffer.AsSpan(0, fetch));
            if (written == 0)
            {
                return Array.Empty<SearchResult<TKey>>();
            }

            int capacity = filter is null ? written : Math.Min(written, topK);
            var results = new List<SearchResult<TKey>>(capacity);
            for (int i = 0; i < written; i++)
            {
                TKey key = buffer[i].Key;
                IReadOnlyDictionary<string, object?>? payload = _payloads.TryGetValue(key, out var p) ? p : null;
                if (filter is not null && !filter.Matches(payload))
                {
                    continue;
                }
                results.Add(new SearchResult<TKey>(key, buffer[i].Score) { Payload = payload });
                if (results.Count >= topK)
                {
                    break;
                }
            }
            return results;
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_index is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void StorePayload(TKey key, Dictionary<string, object>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            _payloads.TryRemove(key, out _);
            return;
        }

        // 拷贝一份并转为 IReadOnlyDictionary<string, object?>，与 Filter 的签名一致。
        var snapshot = new Dictionary<string, object?>(payload.Count, StringComparer.Ordinal);
        foreach (var kv in payload)
        {
            snapshot[kv.Key] = kv.Value;
        }
        _payloads[key] = snapshot;
    }
}
