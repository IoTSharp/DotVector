using DotVector.Core;
using DotVector.Index.Flat;
using DotVector.Index.Hnsw;
using DotVector.Primitives;

namespace DotVector.Indexing;

/// <summary>
/// 基于 DotVector 本地索引实现的向量索引构建器。
/// </summary>
public sealed class LocalVectorIndexBuilder : IVectorIndexBuilder
{
    /// <summary>
    /// 全局共享的默认构建器实例。
    /// </summary>
    public static LocalVectorIndexBuilder Instance { get; } = new();

    /// <inheritdoc />
    public IVectorIndexReader Build(VectorIndexBuildInput input)
    {
        ValidateInput(input);

        return input.Algorithm switch
        {
            VectorIndexAlgorithm.Hnsw => BuildHnsw(input),
            VectorIndexAlgorithm.Flat => BuildFlat(input),
            _ => throw new NotSupportedException($"不支持的向量索引算法：{input.Algorithm}。"),
        };
    }

    private static IVectorIndexReader BuildFlat(VectorIndexBuildInput input)
    {
        var index = new FlatIndex<int>(
            input.Dimension,
            VectorDistance.ToDotVectorMetric(input.Metric),
            input.Count);
        AddRows(index, input.Vectors.Span, input.Count, input.Dimension);
        return new LocalVectorIndexReader(index, input.Algorithm, input.Metric);
    }

    private static IVectorIndexReader BuildHnsw(VectorIndexBuildInput input)
    {
        var options = (input.Hnsw ?? VectorIndexHnswOptions.Default).ToHnswOptions();
        options.Validate();

        var index = new HnswIndex<int>(
            input.Dimension,
            VectorDistance.ToDotVectorMetric(input.Metric),
            options,
            input.Count);
        AddRows(index, input.Vectors.Span, input.Count, input.Dimension);
        return new LocalVectorIndexReader(index, input.Algorithm, input.Metric);
    }

    private static void AddRows(IIndex<int> index, ReadOnlySpan<float> vectors, int count, int dimension)
    {
        for (int row = 0; row < count; row++)
            index.Add(row, vectors.Slice(row * dimension, dimension));
    }

    private static void ValidateInput(VectorIndexBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Dimension);
        ArgumentOutOfRangeException.ThrowIfNegative(input.Count);

        int expectedLength = checked(input.Count * input.Dimension);
        if (input.Vectors.Length != expectedLength)
        {
            throw new ArgumentException(
                $"向量载荷长度必须等于 Count * Dimension（期望 {expectedLength}，实际 {input.Vectors.Length}）。",
                nameof(input));
        }
    }
}
