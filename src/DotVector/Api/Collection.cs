using DotVector.Model;

namespace DotVector.Api;

/// <summary>
/// 单个向量集合，封装索引与搜索操作。
/// </summary>
/// <typeparam name="TKey">记录主键类型。</typeparam>
/// <remarks>
/// TODO(M2): 实现基于 FlatIndex 的内存集合（Insert / Search / Delete）。
/// TODO(M3): 支持切换到 HnswIndex。
/// </remarks>
public sealed class Collection<TKey>
    where TKey : notnull
{
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
    }

    /// <summary>集合名称。</summary>
    public string Name { get; }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>距离度量类型。</summary>
    public Metric Metric { get; }

    /// <summary>
    /// 插入单条向量记录。
    /// </summary>
    /// <param name="record">要插入的向量记录。</param>
    /// <remarks>
    /// TODO(M2): 实现 FlatIndex.Add。
    /// </remarks>
    public void Insert(VectorRecord<TKey> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Vector.Length != Dimensions)
        {
            throw new ArgumentException(
                $"向量维度不匹配：期望 {Dimensions}，实际 {record.Vector.Length}。",
                nameof(record));
        }

        // TODO(M2): 实现插入逻辑
    }

    /// <summary>
    /// 执行近似最近邻（ANN）搜索，返回最相似的 K 条记录。
    /// </summary>
    /// <param name="query">查询向量（维度须与集合一致）。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <returns>按相似度排序的搜索结果列表（值越小越相似）。</returns>
    /// <remarks>
    /// TODO(M2): 实现 FlatIndex 线性扫描搜索。
    /// TODO(M3): 实现 HNSW 近似搜索。
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

        // TODO(M2): 实现搜索逻辑
        return [];
    }
}
