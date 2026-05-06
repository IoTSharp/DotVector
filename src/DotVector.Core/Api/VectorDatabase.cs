using System.Collections.Concurrent;
using DotVector.Index.Hnsw;
using DotVector.Model;

namespace DotVector.Api;

/// <summary>
/// DotVector 嵌入式向量数据库的顶层入口。
/// 管理多个向量集合（Collection），支持进程内嵌入式运行。
/// </summary>
/// <remarks>
/// M2 实现：纯内存数据库，多集合通过线程安全字典管理；每个集合内部使用
/// <see cref="DotVector.Index.Flat.FlatIndex{TKey}"/>（多读单写并发）。
/// TODO(M5): 实现基于目录（.dvec/）的持久化存储（catalog.bin + WAL + Segment mmap）。
/// </remarks>
public sealed class VectorDatabase : IDisposable
{
    private readonly ConcurrentDictionary<string, IDisposable> _collections =
        new(StringComparer.Ordinal);
    private readonly string? _directoryPath;
    private bool _disposed;

    /// <summary>
    /// 创建一个纯内存的 <see cref="VectorDatabase"/> 实例。
    /// 数据不会持久化，进程退出后丢失。
    /// </summary>
    public VectorDatabase()
    {
    }

    /// <summary>
    /// 创建或打开指定路径的持久化向量数据库目录（.dvec/）。
    /// </summary>
    /// <param name="directoryPath">数据库目录路径，例如 "my-db.dvec"。</param>
    /// <remarks>
    /// TODO(M5): 实现目录持久化逻辑（catalog.bin + WAL + Segment mmap）。
    /// </remarks>
    public VectorDatabase(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        _directoryPath = directoryPath;
        // TODO(M5): 打开或创建目录，读取 catalog.bin，replay WAL
    }

    /// <summary>当前注册的集合数量。</summary>
    public int CollectionCount => _collections.Count;

    /// <summary>
    /// 创建新的向量集合。
    /// </summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    /// <param name="name">集合名称，在同一数据库内唯一。</param>
    /// <param name="dimensions">向量维度（如 384 / 768 / 1536）。</param>
    /// <param name="metric">距离度量类型，默认为 <see cref="Metric.Cosine"/>。</param>
    /// <returns>新建的集合实例。</returns>
    /// <exception cref="InvalidOperationException">同名集合已存在。</exception>
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
    /// <param name="indexKind">索引类型（<see cref="IndexKind.Flat"/> 或 <see cref="IndexKind.Hnsw"/>）。</param>
    /// <param name="hnswOptions">当 <paramref name="indexKind"/> 为 <see cref="IndexKind.Hnsw"/> 时使用的参数；为 <see langword="null"/> 时使用 <see cref="HnswOptions.Default"/>。</param>
    /// <returns>新建的集合实例。</returns>
    /// <exception cref="InvalidOperationException">同名集合已存在。</exception>
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
        if (!_collections.TryAdd(name, collection))
        {
            collection.Dispose();
            throw new InvalidOperationException($"集合 '{name}' 已存在。");
        }
        return collection;
    }

    /// <summary>
    /// 获取已存在的集合实例（按名称）。
    /// </summary>
    /// <typeparam name="TKey">期望的主键类型。</typeparam>
    /// <param name="name">集合名称。</param>
    /// <returns>集合实例。</returns>
    /// <exception cref="KeyNotFoundException">集合不存在。</exception>
    /// <exception cref="InvalidOperationException">集合存在但 TKey 与创建时不一致。</exception>
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
            catch { /* 忽略单个集合 Dispose 异常，确保其余资源能继续释放 */ }
        }
        _collections.Clear();

        // TODO(M5): 释放 mmap 句柄，flush MemTable，关闭目录句柄。
        _ = _directoryPath; // 保持字段以便 M5 使用。
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
