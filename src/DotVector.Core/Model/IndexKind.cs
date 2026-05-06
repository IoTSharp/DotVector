namespace DotVector.Model;

/// <summary>
/// 集合内部使用的索引类型。
/// </summary>
public enum IndexKind
{
    /// <summary>
    /// 线性扫描精确索引（<see cref="DotVector.Index.Flat.FlatIndex{TKey}"/>）。
    /// 召回 100%，适合小规模数据或精度优先的场景。
    /// </summary>
    Flat = 0,

    /// <summary>
    /// HNSW 图索引（<see cref="DotVector.Index.Hnsw.HnswIndex{TKey}"/>），
    /// 适合大规模数据的高性能近似最近邻检索（M3 引入）。
    /// </summary>
    Hnsw = 1,

    // TODO(M4): IvfFlat、IvfPq
}
