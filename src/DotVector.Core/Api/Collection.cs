using System.Buffers;
using System.Collections.Concurrent;
using DotVector.Core;
using DotVector.Index.DiskAnn;
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
public sealed class Collection<TKey> : IDisposable, IPersistableCollection
    where TKey : notnull
{
    private readonly IIndex<TKey> _index;
    private readonly ConcurrentDictionary<TKey, IReadOnlyDictionary<string, object?>> _payloads = new();
    private readonly ScalarIndex<TKey> _scalarIndex = new();
    private IWriteSink<TKey>? _writeSink;
    private PersistentDirectory? _persistent;
    private Guid _collectionId;
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
    /// <param name="vamanaOptions">当 <paramref name="indexKind"/> 为 <see cref="IndexKind.Vamana"/> 时使用的参数；为 <see langword="null"/> 时使用默认值。</param>
    internal Collection(
        string name,
        int dimensions,
        Metric metric,
        IndexKind indexKind = IndexKind.Flat,
        HnswOptions? hnswOptions = null,
        IvfOptions? ivfOptions = null,
        IvfPqOptions? ivfPqOptions = null,
        VamanaOptions? vamanaOptions = null)
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
            IndexKind.Vamana => new VamanaIndex<TKey>(dimensions, metric, vamanaOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(indexKind), indexKind, "未支持的索引类型。"),
        };
    }

    /// <summary>
    /// 设置写入观察者（仅供持久化层 <see cref="DotVector.Storage.PersistentDirectory"/> 使用）。
    /// 设置后，<see cref="Insert"/> / <see cref="InsertBatch"/> / <see cref="Delete"/>
    /// 会在修改索引之前先通知 sink。回放期间应保持为 <see langword="null"/> 以避免重复写 WAL。
    /// </summary>
    internal void AttachWriteSink(IWriteSink<TKey>? sink) => _writeSink = sink;

    /// <summary>
    /// 关联持久化目录与本集合的 ID，并设置写入观察者。
    /// 设置后，<see cref="Flush"/> / <see cref="Compact"/> 才有效；
    /// 写入操作会先通知 sink 写 WAL，再变更索引。
    /// </summary>
    internal void AttachPersistence(PersistentDirectory? persistent, Guid collectionId, IWriteSink<TKey>? sink)
    {
        _persistent = persistent;
        _collectionId = collectionId;
        _writeSink = sink;
    }

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
        if (_payloads.TryRemove(key, out IReadOnlyDictionary<string, object?>? oldPayload))
        {
            _scalarIndex.Remove(key, oldPayload);
        }
        return removed;
    }

    /// <summary>
    /// 获取指定键的标量 payload（M6）。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <returns>payload 字典；若记录不存在或未提供 payload 则返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// payload 自 M11 起持久化（WAL + Segment <c>payload.bin</c>），重启后保留。
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
    /// 设置或更新指定记录的标量 payload（M11）。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="payload">payload 字典；为 <see langword="null"/> 或空字典表示清空 payload。</param>
    /// <remarks>
    /// 自 M11 起，payload 写入会同步到 WAL 与 Segment（<c>payload.bin</c>），重启后保留。
    /// </remarks>
    public void SetPayload(TKey key, IReadOnlyDictionary<string, object?>? payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        bool isEmpty = payload is null || payload.Count == 0;
        byte[] encoded = isEmpty ? Array.Empty<byte>() : PayloadCodec.Encode(payload!);
        _writeSink?.OnPayload(key, encoded);
        _payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? oldPayload);
        if (isEmpty)
        {
            _payloads.TryRemove(key, out _);
            _scalarIndex.Update(key, oldPayload, newPayload: null);
        }
        else
        {
            var snapshot = new Dictionary<string, object?>(payload!.Count, StringComparer.Ordinal);
            foreach (var kv in payload!)
            {
                snapshot[kv.Key] = kv.Value;
            }
            _payloads[key] = snapshot;
            _scalarIndex.Update(key, oldPayload, snapshot);
        }
    }

    /// <summary>
    /// 按主键尝试取回向量记录（M7.1）。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="record">命中时输出包含 <see cref="VectorRecord{TKey}.Vector"/> 与 <see cref="VectorRecord{TKey}.Payload"/> 的记录；未命中输出 <see langword="null"/>。</param>
    /// <returns>命中返回 <see langword="true"/>，未命中返回 <see langword="false"/>。</returns>
    /// <remarks>
    /// 当前 (M7.1) 实现假定向量在内存索引中（FlatIndex 持有完整数据；持久化重启时 <see cref="VectorDatabase"/> 会把
    /// Segment 全量重放回 FlatIndex）。其他索引类型（HNSW / IVF）尚未提供按 key 取回，调用会抛出
    /// <see cref="NotSupportedException"/>。
    /// </remarks>
    public bool TryGet(TKey key, out VectorRecord<TKey>? record)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        if (_index is not FlatIndex<TKey> flat)
        {
            throw new NotSupportedException(
                "TryGet 当前仅支持 FlatIndex 后端（M7.1）；HNSW / IVF 的按 key 取回计划在后续 milestone 实现。");
        }

        float[] buffer = new float[Dimensions];
        if (!flat.TryCopyVectorTo(key, buffer))
        {
            record = null;
            return false;
        }

        record = BuildRecord(key, buffer);
        return true;
    }

    /// <summary>
    /// 按主键批量取回向量记录（M7.1）。
    /// </summary>
    /// <param name="keys">主键列表。</param>
    /// <param name="includeVectors">是否在结果中携带向量数据。当前实现始终携带（向量已驻留内存），
    /// 该参数保留为协议/序列化层的优化提示，未来若引入按需从 Segment 加载的路径时启用。</param>
    /// <returns>命中记录列表（顺序与 <paramref name="keys"/> 一致；缺失主键被跳过，不会出现在结果中）。</returns>
    public IReadOnlyList<VectorRecord<TKey>> GetMany(ReadOnlySpan<TKey> keys, bool includeVectors)
    {
        _ = includeVectors; // 当前实现始终携带向量；保留参数以兼容协议层语义。
        ThrowIfDisposed();

        if (_index is not FlatIndex<TKey> flat)
        {
            throw new NotSupportedException(
                "GetMany 当前仅支持 FlatIndex 后端（M7.1）；HNSW / IVF 的按 key 取回计划在后续 milestone 实现。");
        }

        var results = new List<VectorRecord<TKey>>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            TKey key = keys[i];
            ArgumentNullException.ThrowIfNull(key);
            float[] buffer = new float[Dimensions];
            if (flat.TryCopyVectorTo(key, buffer))
            {
                results.Add(BuildRecord(key, buffer));
            }
        }
        return results;
    }

    private VectorRecord<TKey> BuildRecord(TKey key, float[] vector)
    {
        var record = new VectorRecord<TKey>(key, vector);
        if (_payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? payload) && payload.Count > 0)
        {
            // VectorRecord.Payload 类型为 Dictionary<string, object>（不允许 null 值），
            // 这里把内部 IReadOnlyDictionary<string, object?> 中的非 null 值复制出来。
            var copy = new Dictionary<string, object>(payload.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in payload)
            {
                if (kv.Value is not null)
                {
                    copy[kv.Key] = kv.Value;
                }
            }
            if (copy.Count > 0)
            {
                record = new VectorRecord<TKey>(key, vector) { Payload = copy };
            }
        }
        return record;
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

        // M11：尝试通过 ScalarIndex 把过滤下推为候选键集合，并直接在 FlatIndex 上做 SearchSubset。
        if (filter is not null && _index is FlatIndex<TKey> flatIndex)
        {
            if (_scalarIndex.TryResolveCandidates(filter, out HashSet<TKey>? candidates) && candidates is not null)
            {
                if (candidates.Count == 0)
                {
                    return Array.Empty<SearchResult<TKey>>();
                }

                var subsetPool = ArrayPool<(TKey Key, float Score)>.Shared;
                int subsetFetch = Math.Min(candidates.Count, Math.Max(topK, 1));
                (TKey Key, float Score)[] subsetBuffer = subsetPool.Rent(subsetFetch);
                try
                {
                    int subsetWritten = flatIndex.SearchSubset(query, subsetFetch, candidates, subsetBuffer.AsSpan(0, subsetFetch));
                    if (subsetWritten == 0)
                    {
                        return Array.Empty<SearchResult<TKey>>();
                    }
                    var subsetResults = new List<SearchResult<TKey>>(Math.Min(subsetWritten, topK));
                    for (int i = 0; i < subsetWritten && subsetResults.Count < topK; i++)
                    {
                        TKey k = subsetBuffer[i].Key;
                        IReadOnlyDictionary<string, object?>? p = _payloads.TryGetValue(k, out var pv) ? pv : null;
                        // 双保险：pre-filter 是保守集合，但仍用 Filter.Matches 做最终判定，
                        // 避免 ScalarIndex 不支持的形态把不该匹配的键漏到结果里。
                        if (!filter.Matches(p)) continue;
                        subsetResults.Add(new SearchResult<TKey>(k, subsetBuffer[i].Score) { Payload = p });
                    }
                    return subsetResults;
                }
                finally
                {
                    subsetPool.Return(subsetBuffer, clearArray: true);
                }
            }
        }

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

    /// <summary>
    /// 把当前内存索引快照刷入新 Segment，并旋转 WAL（M10 / M12.3）。
    /// 仅当集合已通过 <see cref="DotVector.Api.VectorDatabase"/> 关联到持久化目录、
    /// 且使用 <see cref="IndexKind.Flat"/> 或 <see cref="IndexKind.Vamana"/> 索引时才有效。
    /// </summary>
    /// <exception cref="NotSupportedException">底层索引不是 <see cref="FlatIndex{TKey}"/> 或 <see cref="VamanaIndex{TKey}"/>。</exception>
    public void Flush()
    {
        ThrowIfDisposed();
        if (_persistent is null) { return; }
        if (_index is FlatIndex<TKey> flat)
        {
            _persistent.FlushCollection(_collectionId, flat, EncodePayloadForKey);
        }
        else if (_index is VamanaIndex<TKey> vamana)
        {
            _persistent.FlushVamanaCollection(_collectionId, vamana, vamana.Options, EncodePayloadForKey);
        }
        else
        {
            throw new NotSupportedException("Flush 当前仅支持 Flat / Vamana 索引；HNSW / IVF 持久化将在后续 Milestone 提供。");
        }
    }

    /// <summary>
    /// 合并集合的所有 Segment 为单个 Segment（M10）。
    /// 仅当集合已通过 <see cref="DotVector.Api.VectorDatabase"/> 关联到持久化目录时才有效。
    /// </summary>
    public void Compact()
    {
        ThrowIfDisposed();
        if (_persistent is null) { return; }
        _persistent.CompactCollection<TKey>(_collectionId);
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
        _payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? oldPayload);
        if (payload is null || payload.Count == 0)
        {
            // 不主动 OnPayload(empty)，因为 Insert/Delete 已经覆盖语义；
            // 仅清理内存映射。
            if (_payloads.TryRemove(key, out _))
            {
                _scalarIndex.Update(key, oldPayload, newPayload: null);
            }
            return;
        }

        // 拷贝一份并转为 IReadOnlyDictionary<string, object?>，与 Filter 的签名一致。
        var snapshot = new Dictionary<string, object?>(payload.Count, StringComparer.Ordinal);
        foreach (var kv in payload)
        {
            snapshot[kv.Key] = kv.Value;
        }
        _payloads[key] = snapshot;
        _scalarIndex.Update(key, oldPayload, snapshot);

        // M11：随插入一并写 payload WAL 记录，确保重启后 payload 不丢失。
        if (_writeSink is not null)
        {
            byte[] encoded = PayloadCodec.Encode(snapshot);
            _writeSink.OnPayload(key, encoded);
        }
    }

    /// <summary>从恢复路径直接注入 payload 内存视图（不触发 WAL/sink）。M11。</summary>
    internal void RestorePayload(TKey key, IReadOnlyDictionary<string, object?>? payload)
    {
        _payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? oldPayload);
        if (payload is null || payload.Count == 0)
        {
            if (_payloads.TryRemove(key, out _))
            {
                _scalarIndex.Update(key, oldPayload, newPayload: null);
            }
            return;
        }
        _payloads[key] = payload;
        _scalarIndex.Update(key, oldPayload, payload);
    }

    /// <summary>Flush 时按键编码 payload；无 payload 返回 <see langword="null"/>。M11。</summary>
    private byte[]? EncodePayloadForKey(TKey key)
    {
        return _payloads.TryGetValue(key, out IReadOnlyDictionary<string, object?>? p) && p is { Count: > 0 }
            ? PayloadCodec.Encode(p)
            : null;
    }

    /// <summary>
    /// 用一份完整快照批量恢复 Vamana 索引内部状态（M12.3）。
    /// 仅在初始化阶段（索引为空、无并发写入）调用。
    /// </summary>
    internal void RestoreVamanaSnapshot(
        IReadOnlyList<TKey> keys,
        ReadOnlySpan<float> vectors,
        int entryPoint,
        IReadOnlyList<int[]> neighbors,
        IReadOnlyCollection<int> tombstones)
    {
        if (_index is not VamanaIndex<TKey> vamana)
        {
            throw new InvalidOperationException("RestoreVamanaSnapshot 仅适用于 Vamana 索引。");
        }
        vamana.RestoreBulk(keys, vectors, entryPoint, neighbors, tombstones);
    }
}
