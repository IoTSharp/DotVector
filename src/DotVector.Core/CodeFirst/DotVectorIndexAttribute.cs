using DotVector.Model;

namespace DotVector.CodeFirst;

/// <summary>
/// 为 <see cref="DotVectorVectorAttribute"/> 标记的向量属性声明底层索引类型与索引参数。
/// </summary>
/// <remarks>
/// 未标记该特性时默认使用 <see cref="IndexKind.Flat"/>。参数使用 <c>-1</c> 表示沿用 DotVector
/// 对应索引的默认值；<see cref="Seed"/> 使用 <see cref="int.MinValue"/> 表示不指定随机种子。
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DotVectorIndexAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="DotVectorIndexAttribute"/>。
    /// </summary>
    /// <param name="indexKind">索引类型。</param>
    public DotVectorIndexAttribute(IndexKind indexKind)
    {
        IndexKind = indexKind;
    }

    /// <summary>索引类型。</summary>
    public IndexKind IndexKind { get; }

    /// <summary>HNSW 的 M 参数。小于等于 0 时使用默认值。</summary>
    public int HnswM { get; set; } = -1;

    /// <summary>HNSW 的 efConstruction 参数。小于等于 0 时使用默认值。</summary>
    public int EfConstruction { get; set; } = -1;

    /// <summary>HNSW 的 efSearch 参数。小于等于 0 时使用默认值。</summary>
    public int EfSearch { get; set; } = -1;

    /// <summary>IVF 的倒排列表数量。小于等于 0 时使用默认值。</summary>
    public int NList { get; set; } = -1;

    /// <summary>IVF 搜索时探测的列表数量。小于等于 0 时使用默认值。</summary>
    public int NProbe { get; set; } = -1;

    /// <summary>K-Means 最大迭代次数。小于等于 0 时使用默认值。</summary>
    public int MaxIterations { get; set; } = -1;

    /// <summary>IVF-PQ 的子空间数量。小于等于 0 时使用默认值。</summary>
    public int IvfPqM { get; set; } = -1;

    /// <summary>IVF-PQ 的每个子量化器码本位数。小于等于 0 时使用默认值。</summary>
    public int NBits { get; set; } = -1;

    /// <summary>Vamana 的最大邻居数。小于等于 0 时使用默认值。</summary>
    public int MaxDegree { get; set; } = -1;

    /// <summary>Vamana 的候选列表大小。小于等于 0 时使用默认值。</summary>
    public int SearchListSize { get; set; } = -1;

    /// <summary>Vamana 的 RobustPrune alpha。小于 1 时使用默认值。</summary>
    public float Alpha { get; set; } = -1f;

    /// <summary>Vamana 的 BeamSearch 束宽。小于等于 0 时使用默认值。</summary>
    public int BeamWidth { get; set; } = -1;

    /// <summary>索引构建随机种子；<see cref="int.MinValue"/> 表示不指定。</summary>
    public int Seed { get; set; } = int.MinValue;

    internal DotVectorIndexOptions ToOptions()
        => new()
        {
            HnswM = PositiveOrNull(HnswM),
            EfConstruction = PositiveOrNull(EfConstruction),
            EfSearch = PositiveOrNull(EfSearch),
            NList = PositiveOrNull(NList),
            NProbe = PositiveOrNull(NProbe),
            MaxIterations = PositiveOrNull(MaxIterations),
            IvfPqM = PositiveOrNull(IvfPqM),
            NBits = PositiveOrNull(NBits),
            MaxDegree = PositiveOrNull(MaxDegree),
            SearchListSize = PositiveOrNull(SearchListSize),
            Alpha = Alpha >= 1f ? Alpha : null,
            BeamWidth = PositiveOrNull(BeamWidth),
            Seed = Seed == int.MinValue ? null : Seed,
        };

    private static int? PositiveOrNull(int value) => value > 0 ? value : null;
}
