using DotVector.Model;

namespace DotVector.CodeFirst;

/// <summary>
/// 标记实体中的向量属性，并声明该向量字段的维度与距离度量。
/// </summary>
/// <remarks>
/// 支持的属性类型为 <c>float[]</c>、<see cref="Memory{T}"/> of <see cref="float"/>
/// 与 <see cref="ReadOnlyMemory{T}"/> of <see cref="float"/>。
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DotVectorVectorAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="DotVectorVectorAttribute"/>。
    /// </summary>
    /// <param name="dimensions">向量维度。</param>
    public DotVectorVectorAttribute(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        Dimensions = dimensions;
    }

    /// <summary>向量维度。</summary>
    public int Dimensions { get; }

    /// <summary>向量字段名称；未设置时使用 CLR 属性名。</summary>
    public string? Name { get; set; }

    /// <summary>
    /// 底层集合名称；未设置时由 <see cref="DotVectorSet{TEntity}"/> 的名称和向量字段名派生。
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>距离度量类型。默认 <see cref="Metric.Cosine"/>。</summary>
    public Metric Metric { get; set; } = Metric.Cosine;
}
