namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 查询返回的单条搜索结果。
/// </summary>
public sealed class DotVectorSearchResult
{
    /// <summary>
    /// 初始化 <see cref="DotVectorSearchResult"/>。
    /// </summary>
    /// <param name="key">命中记录主键。</param>
    /// <param name="score">距离或相似度分数。</param>
    /// <param name="vectorFieldName">参与查询的向量字段名称。</param>
    public DotVectorSearchResult(object key, float score, string vectorFieldName)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(vectorFieldName);
        Key = key;
        Score = score;
        VectorFieldName = vectorFieldName;
    }

    /// <summary>命中记录主键。</summary>
    public object Key { get; }

    /// <summary>距离或相似度分数。语义与底层集合的 Metric 一致。</summary>
    public float Score { get; }

    /// <summary>参与查询的向量字段名称。</summary>
    public string VectorFieldName { get; }

    /// <summary>命中记录的 payload。</summary>
    public IReadOnlyDictionary<string, object?>? Payload { get; init; }
}
