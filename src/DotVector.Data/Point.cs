namespace DotVector.Data;

/// <summary>
/// 一条向量记录（用于写入 / 取回）。
/// </summary>
/// <param name="Id">记录唯一标识。</param>
/// <param name="Vector">float32 向量数据。</param>
/// <param name="Payload">可选的标量 payload；值类型限定为 <see cref="string"/> / <see cref="long"/> /
/// <see cref="double"/> / <see cref="bool"/>。</param>
public sealed record Point(string Id, float[] Vector, IReadOnlyDictionary<string, object>? Payload = null);

/// <summary>
/// 一条搜索命中（带得分）。
/// </summary>
/// <param name="Id">记录唯一标识。</param>
/// <param name="Score">距离或相似度分数；语义取决于集合的 <see cref="DistanceMetric"/>：
/// L2 / Cosine / Hamming 为距离（越小越相似），InnerProduct / DotProduct 为相似度（越大越相似）。</param>
/// <param name="Payload">可选的 payload。</param>
/// <param name="Vector">命中的向量数据；仅当请求时 <c>includeVector=true</c> 才回填。</param>
public sealed record ScoredPoint(
    string Id,
    float Score,
    IReadOnlyDictionary<string, object>? Payload = null,
    float[]? Vector = null);
