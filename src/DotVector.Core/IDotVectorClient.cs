using DotVector.Core.Protocol;

namespace DotVector.Core;
/// <summary>
/// DotVector 服务端的客户端协议抽象接口。
/// 定义了客户端与服务端之间的所有操作契约，与传输协议无关。
/// </summary>
/// <remarks>
/// 实现方式：
/// <list type="bullet">
///   <item><description>M9: <c>GrpcDotVectorClient</c>（gRPC 传输，位于 DotVector.Data）</description></item>
///   <item><description>M9: <c>LocalDotVectorClient</c>（进程内直接调用，供嵌入式场景使用，位于 DotVector）</description></item>
///   <item><description>测试: <c>InMemoryDotVectorClient</c>（内存模拟，用于单元测试）</description></item>
/// </list>
/// <para>
/// <c>DotVector.Data</c>（VectorData 适配层）仅依赖此接口，
/// 不直接引用 <c>DotVector</c>（服务端）。
/// </para>
/// TODO(M9): 实现 GrpcDotVectorClient（gRPC 传输）。
/// TODO(M9): 实现 LocalDotVectorClient（进程内直连，零序列化开销）。
/// </remarks>
public interface IDotVectorClient : IAsyncDisposable
{
    /// <summary>
    /// 创建新的向量集合。
    /// </summary>
    /// <param name="request">创建集合的请求参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask CreateCollectionAsync(
        CreateCollectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定名称的集合。
    /// </summary>
    /// <param name="collectionName">集合名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask DeleteCollectionAsync(
        string collectionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向指定集合插入一批向量记录。
    /// </summary>
    /// <param name="collectionName">目标集合名称。</param>
    /// <param name="records">要插入的向量记录列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask UpsertAsync(
        string collectionName,
        IReadOnlyList<VectorUpsertRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从集合中删除指定 ID 的记录。
    /// </summary>
    /// <param name="collectionName">目标集合名称。</param>
    /// <param name="ids">要删除的记录 ID 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask DeleteAsync(
        string collectionName,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在集合中执行近似最近邻（ANN）搜索。
    /// </summary>
    /// <param name="collectionName">目标集合名称。</param>
    /// <param name="request">搜索请求参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按相似度排序的搜索结果列表。</returns>
    ValueTask<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName,
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查客户端与服务端的连接是否正常。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<bool> PingAsync(CancellationToken cancellationToken = default);
}
