namespace DotVector.Indexing;

/// <summary>
/// 库级向量索引算法。
/// </summary>
public enum VectorIndexAlgorithm : byte
{
    /// <summary>
    /// 精确线性扫描索引。
    /// </summary>
    Flat = 0,

    /// <summary>
    /// HNSW 图索引。
    /// </summary>
    Hnsw = 1,
}
