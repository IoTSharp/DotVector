using DotVector.Core;
using DotVector.Core.Protocol;

namespace DotVector.Data;

/// <summary>
/// DotVector 的 <c>Microsoft.Extensions.VectorData</c> 适配层。
/// </summary>
/// <remarks>
/// <para>
/// <b>架构说明</b>：此项目是客户端适配器，通过 <see cref="IDotVectorClient"/>
/// 协议接口访问 DotVector 服务端，不直接引用 <c>DotVector</c>（服务端程序集）。
/// </para>
/// <para>
/// <b>运行时注入</b>：调用方在构建时通过 DI 注入具体的 <see cref="IDotVectorClient"/> 实现：
/// <list type="bullet">
///   <item><description>
///     远程访问（M9）：<c>GrpcDotVectorClient</c>（位于本项目）
///     — DotVector 以独立进程或容器运行，通过 gRPC 通信。
///   </description></item>
///   <item><description>
///     进程内直连（M9）：<c>LocalDotVectorClient</c>（位于 DotVector 服务端程序集）
///     — DotVector 嵌入式运行，<see cref="IDotVectorClient"/> 直接调用本地实例，零序列化开销。
///   </description></item>
/// </list>
/// </para>
/// TODO(M7): 实现 <c>DotVectorVectorStore : IVectorStore</c>。
/// TODO(M7): 实现 <c>DotVectorCollection&lt;TKey, TRecord&gt; : IVectorStoreRecordCollection</c>。
/// TODO(M9): 实现 <c>GrpcDotVectorClient : IDotVectorClient</c>（gRPC 传输）。
/// </remarks>
public static class DotVectorVectorDataPlaceholder
{
    /// <summary>
    /// 返回当前适配层状态说明（M7 实现前的占位）。
    /// </summary>
    public static string GetStatus() =>
        "DotVector.Data — Microsoft.Extensions.VectorData 客户端适配层。" +
        " 通过 IDotVectorClient 协议接口访问服务端（不直接依赖 DotVector 程序集）。" +
        " VectorData 适配将在 M7 实现，gRPC 客户端将在 M9 实现。";

    /// <summary>
    /// 演示如何通过注入的 <see cref="IDotVectorClient"/> 发起搜索（占位示例）。
    /// </summary>
    /// <param name="client">运行时注入的客户端实现（gRPC 或进程内）。</param>
    /// <param name="collectionName">集合名称。</param>
    /// <param name="queryVector">查询向量。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// TODO(M7): 此方法将被 IVectorStoreRecordCollection.VectorizedSearchAsync 替代。
    /// </remarks>
    public static ValueTask<IReadOnlyList<VectorSearchResult>> SearchAsync(
        IDotVectorClient client,
        string collectionName,
        float[] queryVector,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        var request = new VectorSearchRequest(queryVector, topK);
        return client.SearchAsync(collectionName, request, cancellationToken);
    }
}
