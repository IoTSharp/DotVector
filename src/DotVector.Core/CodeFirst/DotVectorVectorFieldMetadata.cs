using DotVector.Model;

namespace DotVector.CodeFirst;

/// <summary>
/// 描述 Code-First 实体中的单个向量字段。
/// </summary>
public sealed class DotVectorVectorFieldMetadata
{
    /// <summary>
    /// 初始化 <see cref="DotVectorVectorFieldMetadata"/>。
    /// </summary>
    /// <param name="name">向量字段名称。</param>
    /// <param name="collectionName">显式底层集合名称；为 <see langword="null"/> 时由 set 名称派生。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="metric">距离度量。</param>
    /// <param name="indexKind">索引类型。</param>
    /// <param name="indexOptions">索引参数。</param>
    public DotVectorVectorFieldMetadata(
        string name,
        string? collectionName,
        int dimensions,
        Metric metric,
        IndexKind indexKind,
        DotVectorIndexOptions indexOptions)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentNullException.ThrowIfNull(indexOptions);
        Name = name;
        CollectionName = string.IsNullOrWhiteSpace(collectionName) ? null : collectionName;
        Dimensions = dimensions;
        Metric = metric;
        IndexKind = indexKind;
        IndexOptions = indexOptions;
    }

    /// <summary>向量字段名称。</summary>
    public string Name { get; }

    /// <summary>显式底层集合名称；为 <see langword="null"/> 时由 set 名称派生。</summary>
    public string? CollectionName { get; }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>距离度量。</summary>
    public Metric Metric { get; }

    /// <summary>索引类型。</summary>
    public IndexKind IndexKind { get; }

    /// <summary>索引参数。</summary>
    public DotVectorIndexOptions IndexOptions { get; }
}
