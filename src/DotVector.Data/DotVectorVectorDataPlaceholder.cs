namespace DotVector.Data;

/// <summary>
/// DotVector 的 <c>Microsoft.Extensions.VectorData</c> 适配层占位类型。
/// 将在 M7 中实现 <c>IVectorStore</c> 和 <c>IVectorStoreRecordCollection&lt;TKey, TRecord&gt;</c> 接口。
/// </summary>
/// <remarks>
/// TODO(M7): 实现 DotVectorVectorStore（IVectorStore）。
/// TODO(M7): 实现 DotVectorCollection（IVectorStoreRecordCollection&lt;TKey, TRecord&gt;）。
/// TODO(M7): 与 Semantic Kernel Memory / RAG Pipeline 集成测试。
/// </remarks>
public static class DotVectorVectorDataPlaceholder
{
    /// <summary>
    /// 将在 M7 中返回实现了 <c>IVectorStore</c> 的 DotVector 适配器。
    /// </summary>
    public static string GetStatus() => "DotVector.Data M7 占位 — VectorData 适配层将在 M7 实现。";
}
