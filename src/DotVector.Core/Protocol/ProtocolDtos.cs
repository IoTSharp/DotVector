namespace DotVector.Core.Protocol;

/// <summary>
/// 创建向量集合的请求 DTO。
/// 由 <see cref="IDotVectorClient.CreateCollectionAsync"/> 使用。
/// </summary>
/// <remarks>
/// 此 DTO 在协议层传输，不含服务端内部实现细节。
/// TODO(M9): 映射到 gRPC Protobuf CreateCollectionRequest 消息。
/// </remarks>
public sealed class CreateCollectionRequest
{
    /// <summary>
    /// 使用必要参数初始化 <see cref="CreateCollectionRequest"/>。
    /// </summary>
    /// <param name="name">集合名称，在同一数据库实例内唯一。</param>
    /// <param name="dimensions">向量维度（例如 384 / 768 / 1536）。</param>
    /// <param name="metric">距离度量类型（字符串形式以避免跨层依赖）。</param>
    public CreateCollectionRequest(string name, int dimensions, string metric = "Cosine")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        Name = name;
        Dimensions = dimensions;
        Metric = metric;
    }

    /// <summary>集合名称。</summary>
    public string Name { get; }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>
    /// 距离度量类型字符串。有效值：<c>L2</c> / <c>Cosine</c> / <c>InnerProduct</c> / <c>Hamming</c> / <c>DotProduct</c>。
    /// </summary>
    public string Metric { get; }
}

/// <summary>
/// 向量 Upsert（插入或更新）记录 DTO。
/// </summary>
/// <remarks>
/// TODO(M9): 映射到 gRPC Protobuf UpsertRecord 消息。
/// </remarks>
public sealed class VectorUpsertRecord
{
    /// <summary>
    /// 初始化 <see cref="VectorUpsertRecord"/>。
    /// </summary>
    /// <param name="id">记录唯一标识（字符串，协议层统一使用字符串 ID）。</param>
    /// <param name="vector">float32 向量数据。</param>
    public VectorUpsertRecord(string id, float[] vector)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(vector);
        Id = id;
        Vector = vector;
    }

    /// <summary>记录唯一标识（协议层统一使用字符串）。</summary>
    public string Id { get; }

    /// <summary>float32 向量数据。</summary>
    public float[] Vector { get; }

    /// <summary>
    /// 可选的标量 payload，用于 M6 标量过滤。
    /// 键值对中值类型仅限 JSON 可序列化的基本类型（string / long / double / bool）。
    /// </summary>
    /// <remarks>
    /// TODO(M6): 实现 payload 序列化与过滤逻辑。
    /// </remarks>
    public IReadOnlyDictionary<string, object>? Payload { get; init; }
}

/// <summary>
/// 向量搜索请求 DTO。
/// </summary>
/// <remarks>
/// TODO(M9): 映射到 gRPC Protobuf SearchRequest 消息。
/// </remarks>
public sealed class VectorSearchRequest
{
    /// <summary>
    /// 初始化 <see cref="VectorSearchRequest"/>。
    /// </summary>
    /// <param name="queryVector">查询向量（float32）。</param>
    /// <param name="topK">返回最相似的 K 条结果。</param>
    public VectorSearchRequest(float[] queryVector, int topK = 10)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        QueryVector = queryVector;
        TopK = topK;
    }

    /// <summary>查询向量。</summary>
    public float[] QueryVector { get; }

    /// <summary>返回结果数量，默认 10。</summary>
    public int TopK { get; }

    /// <summary>
    /// 可选的标量过滤条件（M6 后启用）。
    /// 格式待定，占位使用字符串表达式。
    /// </summary>
    /// <remarks>
    /// TODO(M6): 定义结构化过滤 DSL，替换字符串占位。
    /// </remarks>
    public string? Filter { get; init; }
}

/// <summary>
/// 向量搜索的单条结果 DTO。
/// </summary>
/// <remarks>
/// TODO(M9): 映射到 gRPC Protobuf SearchResult 消息。
/// </remarks>
public sealed class VectorSearchResult
{
    /// <summary>
    /// 初始化 <see cref="VectorSearchResult"/>。
    /// </summary>
    /// <param name="id">匹配记录的 ID。</param>
    /// <param name="score">距离或相似度分数。</param>
    public VectorSearchResult(string id, float score)
    {
        Id = id;
        Score = score;
    }

    /// <summary>匹配记录的 ID。</summary>
    public string Id { get; }

    /// <summary>
    /// 距离或相似度分数。语义取决于集合的 Metric：
    /// L2/Cosine/Hamming 为距离（越小越相似），InnerProduct/DotProduct 为相似度（越大越相似）。
    /// </summary>
    public float Score { get; }

    /// <summary>
    /// 可选的标量 payload（M6 后启用）。
    /// </summary>
    public IReadOnlyDictionary<string, object>? Payload { get; init; }
}
