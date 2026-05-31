using DotVector.Primitives;

namespace DotVector.Indexing;

/// <summary>
/// 向量索引构建输入。
/// </summary>
/// <param name="Algorithm">索引算法。</param>
/// <param name="Metric">KNN 距离度量。</param>
/// <param name="Vectors">行优先连续 float32 向量载荷，长度必须为 <c>Count * Dimension</c>。</param>
/// <param name="Count">向量数量。</param>
/// <param name="Dimension">向量维度。</param>
/// <param name="Hnsw">HNSW 参数；当 <paramref name="Algorithm"/> 为 <see cref="VectorIndexAlgorithm.Hnsw"/> 时使用。</param>
public sealed record VectorIndexBuildInput(
    VectorIndexAlgorithm Algorithm,
    KnnMetric Metric,
    ReadOnlyMemory<float> Vectors,
    int Count,
    int Dimension,
    VectorIndexHnswOptions? Hnsw = null);
