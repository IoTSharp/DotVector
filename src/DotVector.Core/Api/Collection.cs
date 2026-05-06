using System.Buffers;
using DotVector.Index.Flat;
using DotVector.Model;

namespace DotVector.Api;

/// <summary>
/// 单个向量集合，封装索引与搜索操作。
/// </summary>
/// <typeparam name="TKey">记录主键类型。</typeparam>
/// <remarks>
/// M2 实现：底层使用 <see cref="FlatIndex{TKey}"/>（线性扫描精确检索）。
/// TODO(M3): 支持切换到 HnswIndex（图索引近似检索）。
/// TODO(M4): 支持切换到 IvfFlatIndex / IvfPqIndex（倒排聚类）。
/// </remarks>
public sealed class Collection<TKey> : IDisposable
    where TKey : notnull
{
    private readonly FlatIndex<TKey> _index;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="Collection{TKey}"/> 的新实例（内部构造，由 VectorDatabase 调用）。
    /// </summary>
    /// <param name="name">集合名称。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="metric">距离度量类型。</param>
    internal Collection(string name, int dimensions, Metric metric)
    {
        Name = name;
        Dimensions = dimensions;
        Metric = metric;
        _index = new FlatIndex<TKey>(dimensions, metric);
    }

    /// <summary>集合名称。</summary>
    public string Name { get; }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>距离度量类型。</summary>
    public Metric Metric { get; }

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
        _index.Add(record.Key, record.Vector);
    }

    /// <summary>
    /// 批量插入向量记录，单次写锁原子完成（要么全部成功，要么全部不变）。
    /// </summary>
    /// <param name="records">要插入的向量记录集合。</param>
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

        _index.AddBatch(keys, packed);
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
        return _index.Remove(key);
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
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        if (query.Length != Dimensions)
        {
            throw new ArgumentException(
                $"查询向量维度不匹配：期望 {Dimensions}，实际 {query.Length}。",
                nameof(query));
        }
        ThrowIfDisposed();

        var pool = ArrayPool<(TKey Key, float Score)>.Shared;
        (TKey Key, float Score)[] buffer = pool.Rent(topK);
        try
        {
            int written = _index.Search(query, topK, buffer.AsSpan(0, topK));
            if (written == 0)
            {
                return Array.Empty<SearchResult<TKey>>();
            }

            var results = new SearchResult<TKey>[written];
            for (int i = 0; i < written; i++)
            {
                results[i] = new SearchResult<TKey>(buffer[i].Key, buffer[i].Score);
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
        _index.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
