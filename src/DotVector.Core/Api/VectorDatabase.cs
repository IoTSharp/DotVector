using System.Buffers.Binary;
using System.Collections.Concurrent;
using DotVector.Catalog;
using DotVector.Exceptions;
using DotVector.Format;
using DotVector.Index.Flat;
using DotVector.Index.Hnsw;
using DotVector.Index.Ivf;
using DotVector.IO;
using DotVector.Model;
using DotVector.Storage;
using DotVector.Wal;

namespace DotVector.Api;

/// <summary>
/// DotVector 嵌入式向量数据库的顶层入口。
/// 管理多个向量集合（Collection），支持进程内嵌入式运行，
/// 可选基于 <c>.dvec/</c> 目录的持久化（M5）。
/// </summary>
public sealed class VectorDatabase : IDisposable
{
    private readonly ConcurrentDictionary<string, IDisposable> _collections =
        new(StringComparer.Ordinal);
    private readonly PersistentDirectory? _persistent;
    private bool _disposed;

    /// <summary>
    /// 创建一个纯内存的 <see cref="VectorDatabase"/> 实例。数据不会持久化。
    /// </summary>
    public VectorDatabase()
    {
    }

    /// <summary>
    /// 创建或打开指定路径的持久化向量数据库目录（<c>.dvec/</c>）。
    /// </summary>
    /// <param name="directoryPath">数据库目录路径，例如 <c>"my-db.dvec"</c>。</param>
    /// <remarks>
    /// 启动流程：
    /// <list type="number">
    ///   <item>确保 <c>.dvec/wal/</c> 与 <c>.dvec/collections/</c> 子目录存在。</item>
    ///   <item>从 <c>catalog.bin</c> 加载所有集合元数据。</item>
    ///   <item>对每个集合按 <see cref="KeyTypeCode"/> 构造 <see cref="Collection{TKey}"/>，
    ///         并回放 WAL 中属于该集合的所有有效记录。</item>
    /// </list>
    /// </remarks>
    public VectorDatabase(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        _persistent = PersistentDirectory.Open(directoryPath);
        RestoreFromCatalog();
    }

    private void RestoreFromCatalog()
    {
        if (_persistent is null) return;
        foreach (CatalogEntry entry in _persistent.Entries)
        {
            switch (entry.KeyType)
            {
                case KeyTypeCode.Int32:
                    RestoreCollectionTyped<int>(entry);
                    break;
                case KeyTypeCode.Int64:
                    RestoreCollectionTyped<long>(entry);
                    break;
                case KeyTypeCode.Guid:
                    RestoreCollectionTyped<Guid>(entry);
                    break;
                case KeyTypeCode.String:
                    RestoreCollectionTyped<string>(entry);
                    break;
                default:
                    throw new DotVectorException($"不支持的 KeyType：{entry.KeyType}");
            }
        }
    }

    private void RestoreCollectionTyped<TKey>(CatalogEntry entry) where TKey : notnull
    {
        var collection = new Collection<TKey>(entry.Name, entry.Dimensions, entry.Metric, entry.IndexKind);
        // 1. 先加载已落盘的 Segment（M10：仅 Flat）
        CollectionManifest manifest = _persistent!.GetManifest(entry.CollectionId);
        if (entry.IndexKind == IndexKind.Flat)
        {
            LoadSegmentsInto(collection, entry);
        }
        // 2. 回放 manifest 之后的 WAL（不附加 sink，避免回写）
        ReplayWalInto(collection, entry, (long)manifest.LastCoveredWalSequence);
        // 3. 关联持久化与写入 sink
        collection.AttachPersistence(
            _persistent,
            entry.CollectionId,
            _persistent.CreateSink<TKey>(entry.CollectionId));
        _collections[entry.Name] = collection;
    }

    private void LoadSegmentsInto<TKey>(Collection<TKey> collection, CatalogEntry entry) where TKey : notnull
    {
        if (_persistent is null) return;
        int totalRestored = 0;
        foreach (SegmentReader<TKey> seg in _persistent.LoadSegments<TKey>(entry.CollectionId))
        {
            using (seg)
            {
                if (seg.Header.VectorCount == 0) { continue; }
                float[] vectors = seg.ReadAllVectors();
                int dim = entry.Dimensions;
                int n = seg.Keys.Count;
                var batch = new List<VectorRecord<TKey>>(n);
                for (int i = 0; i < n; i++)
                {
                    float[] v = new float[dim];
                    Array.Copy(vectors, (long)i * dim, v, 0, dim);
                    batch.Add(new VectorRecord<TKey>(seg.Keys[i], v));
                }
                collection.InsertBatch(batch);
                totalRestored += n;

                // M11：恢复 payload（不写 WAL，避免回放副作用）
                IReadOnlyList<byte[]?>? encodedPayloads = seg.EncodedPayloads;
                if (encodedPayloads is not null)
                {
                    for (int i = 0; i < n; i++)
                    {
                        byte[]? enc = encodedPayloads[i];
                        if (enc is { Length: > 0 })
                        {
                            Dictionary<string, object?> dict = PayloadCodec.Decode(enc);
                            collection.RestorePayload(seg.Keys[i], dict);
                        }
                    }
                }
            }
        }
        _persistent.NotifyRestoredRowCount(entry.CollectionId, totalRestored);
    }

    private void ReplayWalInto<TKey>(Collection<TKey> collection, CatalogEntry entry, long minWalSeqExclusive) where TKey : notnull
    {
        if (_persistent is null) return;
        foreach (WalRecord record in _persistent.ReadWalFor(entry.CollectionId, minWalSeqExclusive))
        {
            ReadOnlySpan<byte> body = record.Body;
            SpanReader reader = new(body);
            KeyTypeCode code = (KeyTypeCode)reader.ReadByte();
            if (code != entry.KeyType)
            {
                throw new DotVectorException(
                    $"WAL 记录键类型 {code} 与集合 '{entry.Name}' 的 {entry.KeyType} 不一致。");
            }
            TKey key = KeyCodec.Read<TKey>(ref reader);

            switch (record.Type)
            {
                case WalRecordType.Insert:
                {
                    uint dim = reader.ReadUInt32();
                    if ((int)dim != entry.Dimensions)
                    {
                        throw new DotVectorException(
                            $"WAL 记录维度 {dim} 与集合 '{entry.Name}' 的 {entry.Dimensions} 不一致。");
                    }
                    int byteCount = (int)dim * sizeof(float);
                    ReadOnlySpan<byte> vecBytes = reader.ReadBytes(byteCount);
                    float[] vector = new float[dim];
                    for (int i = 0; i < dim; i++)
                    {
                        vector[i] = BinaryPrimitives.ReadSingleLittleEndian(
                            vecBytes.Slice(i * sizeof(float), sizeof(float)));
                    }
                    collection.Insert(new VectorRecord<TKey>(key, vector));
                    break;
                }
                case WalRecordType.Delete:
                    collection.Delete(key);
                    break;
                case WalRecordType.SetPayload:
                {
                    uint payloadLen = reader.ReadUInt32();
                    if (payloadLen == 0)
                    {
                        collection.RestorePayload(key, null);
                    }
                    else
                    {
                        ReadOnlySpan<byte> payloadBytes = reader.ReadBytes((int)payloadLen);
                        Dictionary<string, object?> dict = PayloadCodec.Decode(payloadBytes);
                        collection.RestorePayload(key, dict);
                    }
                    break;
                }
                default:
                    throw new DotVectorException($"未知 WAL 记录类型：{record.Type}");
            }
        }
    }

    /// <summary>当前注册的集合数量。</summary>
    public int CollectionCount => _collections.Count;

    /// <summary>
    /// 返回当前所有已注册集合实例的快照（按集合名升序）。仅供本程序集与 InternalsVisibleTo 受信项目使用。
    /// </summary>
    /// <remarks>
    /// 主要用于 M9 <c>LocalDotVectorClient</c> / 协议层在不知道 <c>TKey</c> 的情况下枚举集合元数据。
    /// </remarks>
    internal IReadOnlyList<IDisposable> EnumerateCollections()
    {
        var list = new List<KeyValuePair<string, IDisposable>>(_collections);
        list.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        var result = new IDisposable[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = list[i].Value;
        return result;
    }

    /// <summary>
    /// 尝试按名称获取集合实例（运行时类型），不暴露 <c>TKey</c>。
    /// </summary>
    internal bool TryGetUntyped(string name, out IDisposable collection)
    {
        if (_collections.TryGetValue(name, out IDisposable? entry))
        {
            collection = entry;
            return true;
        }
        collection = null!;
        return false;
    }

    /// <summary>
    /// 创建新的向量集合。
    /// </summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    /// <param name="name">集合名称，在同一数据库内唯一。</param>
    /// <param name="dimensions">向量维度（如 384 / 768 / 1536）。</param>
    /// <param name="metric">距离度量类型，默认为 <see cref="Metric.Cosine"/>。</param>
    /// <returns>新建的集合实例。</returns>
    public Collection<TKey> CreateCollection<TKey>(
        string name,
        int dimensions,
        Metric metric = Metric.Cosine)
        where TKey : notnull
        => CreateCollection<TKey>(name, dimensions, metric, IndexKind.Flat, hnswOptions: null);

    /// <summary>
    /// 创建新的向量集合，并指定底层索引类型。
    /// </summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    /// <param name="name">集合名称，在同一数据库内唯一。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="metric">距离度量类型。</param>
    /// <param name="indexKind">索引类型。</param>
    /// <param name="hnswOptions">当 <paramref name="indexKind"/> 为 HNSW 时使用的参数。</param>
    public Collection<TKey> CreateCollection<TKey>(
        string name,
        int dimensions,
        Metric metric,
        IndexKind indexKind,
        HnswOptions? hnswOptions = null)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ThrowIfDisposed();

        var collection = new Collection<TKey>(name, dimensions, metric, indexKind, hnswOptions);
        RegisterAndAttach(name, dimensions, metric, indexKind, collection);
        return collection;
    }

    /// <summary>创建使用 IVF-Flat 索引的集合。</summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    public Collection<TKey> CreateCollection<TKey>(
        string name,
        int dimensions,
        Metric metric,
        IvfOptions options)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        var collection = new Collection<TKey>(name, dimensions, metric, IndexKind.IvfFlat, hnswOptions: null, ivfOptions: options);
        RegisterAndAttach(name, dimensions, metric, IndexKind.IvfFlat, collection);
        return collection;
    }

    /// <summary>创建使用 IVF-PQ 索引的集合。</summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    public Collection<TKey> CreateCollection<TKey>(
        string name,
        int dimensions,
        Metric metric,
        IvfPqOptions options)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        var collection = new Collection<TKey>(name, dimensions, metric, IndexKind.IvfPq, hnswOptions: null, ivfPqOptions: options);
        RegisterAndAttach(name, dimensions, metric, IndexKind.IvfPq, collection);
        return collection;
    }

    private void RegisterAndAttach<TKey>(
        string name,
        int dimensions,
        Metric metric,
        IndexKind indexKind,
        Collection<TKey> collection) where TKey : notnull
    {
        if (!_collections.TryAdd(name, collection))
        {
            collection.Dispose();
            throw new InvalidOperationException($"集合 '{name}' 已存在。");
        }

        if (_persistent is not null)
        {
            try
            {
                Guid id = _persistent.RegisterCollection<TKey>(name, dimensions, metric, indexKind);
                collection.AttachPersistence(_persistent, id, _persistent.CreateSink<TKey>(id));
            }
            catch
            {
                _collections.TryRemove(name, out _);
                collection.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// 获取已存在的集合实例（按名称）。
    /// </summary>
    /// <typeparam name="TKey">期望的主键类型。</typeparam>
    /// <param name="name">集合名称。</param>
    /// <returns>集合实例。</returns>
    public Collection<TKey> GetCollection<TKey>(string name)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ThrowIfDisposed();

        if (!_collections.TryGetValue(name, out IDisposable? entry))
        {
            throw new KeyNotFoundException($"集合 '{name}' 不存在。");
        }
        if (entry is not Collection<TKey> typed)
        {
            throw new InvalidOperationException(
                $"集合 '{name}' 的主键类型与请求的 {typeof(TKey).FullName} 不一致。");
        }
        return typed;
    }

    /// <summary>
    /// 把所有集合的当前内存状态刷成新 Segment 并旋转 WAL（M10）。
    /// </summary>
    /// <remarks>仅对底层为 <see cref="FlatIndex{TKey}"/> 的集合生效；其它索引会被跳过。</remarks>
    public void Flush()
    {
        ThrowIfDisposed();
        if (_persistent is null) { return; }
        foreach (KeyValuePair<string, IDisposable> kv in _collections)
        {
            if (kv.Value is IPersistableCollection p)
            {
                try { p.Flush(); }
                catch (NotSupportedException) { /* 非 Flat 索引在 M10 跳过 */ }
            }
        }
    }

    /// <summary>合并所有集合的多个 Segment 为单个 Segment（M10）。</summary>
    public void Compact()
    {
        ThrowIfDisposed();
        if (_persistent is null) { return; }
        foreach (KeyValuePair<string, IDisposable> kv in _collections)
        {
            if (kv.Value is IPersistableCollection p)
            {
                p.Compact();
            }
        }
    }

    /// <summary>
    /// 删除指定集合并释放其资源。
    /// </summary>
    /// <param name="name">集合名称。</param>
    /// <returns>删除成功返回 <see langword="true"/>；不存在返回 <see langword="false"/>。</returns>
    public bool DropCollection(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ThrowIfDisposed();

        if (_collections.TryRemove(name, out IDisposable? entry))
        {
            entry.Dispose();
            _persistent?.UnregisterCollection(name);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;

        foreach (KeyValuePair<string, IDisposable> kv in _collections)
        {
            try { kv.Value.Dispose(); }
            catch { /* 忽略单个集合 Dispose 异常 */ }
        }
        _collections.Clear();

        try { _persistent?.Dispose(); }
        catch { /* 关闭阶段忽略 */ }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
