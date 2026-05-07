using DotVector.Query;

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
    /// 可选的标量过滤条件（M6 后启用，M7.2 启用结构化 <see cref="DotVector.Query.Filter"/>）。
    /// 服务端在执行 ANN 搜索时会过取候选并应用此 Filter 做 post-filter。
    /// </summary>
    public Filter? Filter { get; init; }

    /// <summary>
    /// 是否在 <see cref="VectorSearchResult.Vector"/> 中回填命中向量（M7.1）。
    /// 默认 <see langword="false"/>。
    /// </summary>
    public bool IncludeVector { get; init; }
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

    /// <summary>
    /// 命中记录的向量数据。仅当 <see cref="VectorSearchRequest.IncludeVector"/>
    /// 为 <see langword="true"/> 时由服务端回填，否则为 <see langword="null"/>。
    /// </summary>
    public float[]? Vector { get; init; }
}

/// <summary>
/// 按 ID 取回向量记录的结果 DTO（M7.1）。
/// 由 <see cref="IDotVectorClient.GetAsync"/> 返回。
/// </summary>
/// <remarks>
/// TODO(M9): 映射到 gRPC Protobuf VectorRecord 消息。
/// </remarks>
public sealed class VectorRecordDto
{
    /// <summary>
    /// 初始化 <see cref="VectorRecordDto"/>。
    /// </summary>
    /// <param name="id">记录唯一标识。</param>
    public VectorRecordDto(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>记录唯一标识。</summary>
    public string Id { get; }

    /// <summary>
    /// 记录的 float32 向量数据。仅当 <see cref="IDotVectorClient.GetAsync"/>
    /// 的 <c>includeVector</c> 参数为 <see langword="true"/> 时填充，否则为 <see langword="null"/>。
    /// </summary>
    public float[]? Vector { get; init; }

    /// <summary>
    /// 可选的 payload（M6 标量字段）。
    /// </summary>
    public IReadOnlyDictionary<string, object>? Payload { get; init; }
}

/// <summary>
/// 按结构化 <see cref="Filter"/> 过滤条件检索记录的请求 DTO（M7.2）。
/// 由 <see cref="IDotVectorClient.ScrollAsync"/> 使用，对应 VectorData
/// 适配层的 <c>GetAsync(Expression&lt;Func&lt;TRecord,bool&gt;&gt;, top, ...)</c>。
/// </summary>
/// <remarks>
/// 不涉及向量相似度，仅作 payload 字段过滤后按存储顺序返回前 <see cref="Top"/> 条结果。
/// TODO(M9): 映射到 gRPC Protobuf ScrollRequest 消息。
/// </remarks>
public sealed class VectorScrollRequest
{
    /// <summary>
    /// 初始化 <see cref="VectorScrollRequest"/>。
    /// </summary>
    /// <param name="filter">必填的结构化过滤条件。</param>
    /// <param name="top">最多返回的记录数。</param>
    public VectorScrollRequest(Filter filter, int top)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        Filter = filter;
        Top = top;
    }

    /// <summary>过滤条件。</summary>
    public Filter Filter { get; }

    /// <summary>最多返回的记录数。</summary>
    public int Top { get; }

    /// <summary>是否在结果中回填向量数据。</summary>
    public bool IncludeVector { get; init; }
}
