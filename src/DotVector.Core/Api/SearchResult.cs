namespace DotVector.Api;

/// <summary>
/// 单条向量搜索结果。
/// </summary>
/// <typeparam name="TKey">记录主键类型。</typeparam>
/// <remarks>
/// TODO(M2): 在 FlatIndex.Search 中填充此结果。
/// </remarks>
public sealed class SearchResult<TKey>
    where TKey : notnull
{
    /// <summary>
    /// 初始化 <see cref="SearchResult{TKey}"/> 的新实例。
    /// </summary>
    /// <param name="key">匹配记录的主键。</param>
    /// <param name="score">距离或相似度分数（语义取决于集合的 Metric）。</param>
    public SearchResult(TKey key, float score)
    {
        Key = key;
        Score = score;
    }

    /// <summary>匹配记录的主键。</summary>
    public TKey Key { get; }

    /// <summary>
    /// 距离或相似度分数。对 L2/Cosine/Hamming 为距离（越小越相似），
    /// 对 InnerProduct/DotProduct 为相似度（越大越相似）。
    /// </summary>
    public float Score { get; }
}
