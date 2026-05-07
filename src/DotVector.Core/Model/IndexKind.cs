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

    /// <summary>
    /// IVF-Flat 倒排文件索引（<see cref="DotVector.Index.Ivf.IvfFlatIndex{TKey}"/>），
    /// 通过 K-Means 粗量化后按列表搜索，适合大规模数据 + 中等召回需求（M4 引入）。
    /// </summary>
    IvfFlat = 2,

    /// <summary>
    /// IVF-PQ 倒排文件 + 乘积量化索引（<see cref="DotVector.Index.Ivf.IvfPqIndex{TKey}"/>），
    /// 显著降低内存占用，适合超大规模数据（M4 引入）。
    /// </summary>
    IvfPq = 3,

    /// <summary>
    /// DiskANN / Vamana 单层图索引（<see cref="DotVector.Index.DiskAnn.VamanaIndex{TKey}"/>），
    /// 适合超大规模磁盘驻留场景，构建后图结构稳定、邻居数量受限（M12 引入）。
    /// </summary>
    Vamana = 4,
}
