using DotVector.Index.DiskAnn;
using DotVector.Index.Hnsw;
using DotVector.Index.Ivf;

namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 显式 schema 注册时使用的索引参数集合。
/// </summary>
/// <remarks>
/// 未设置的参数会使用对应 DotVector 索引的默认值。
/// </remarks>
public sealed class DotVectorIndexOptions
{
    /// <summary>HNSW 的 M 参数。</summary>
    public int? HnswM { get; init; }

    /// <summary>HNSW 的 efConstruction 参数。</summary>
    public int? EfConstruction { get; init; }

    /// <summary>HNSW 的 efSearch 参数。</summary>
    public int? EfSearch { get; init; }

    /// <summary>IVF 的倒排列表数量。</summary>
    public int? NList { get; init; }

    /// <summary>IVF 搜索时探测的列表数量。</summary>
    public int? NProbe { get; init; }

    /// <summary>K-Means 最大迭代次数。</summary>
    public int? MaxIterations { get; init; }

    /// <summary>IVF-PQ 的子空间数量。</summary>
    public int? IvfPqM { get; init; }

    /// <summary>IVF-PQ 的每个子量化器码本位数。</summary>
    public int? NBits { get; init; }

    /// <summary>Vamana 的最大邻居数。</summary>
    public int? MaxDegree { get; init; }

    /// <summary>Vamana 的候选列表大小。</summary>
    public int? SearchListSize { get; init; }

    /// <summary>Vamana 的 RobustPrune alpha。</summary>
    public float? Alpha { get; init; }

    /// <summary>Vamana 的 BeamSearch 束宽。</summary>
    public int? BeamWidth { get; init; }

    /// <summary>索引构建随机种子。</summary>
    public int? Seed { get; init; }

    internal HnswOptions ToHnswOptions()
    {
        var options = new HnswOptions
        {
            M = HnswM ?? HnswOptions.Default.M,
            EfConstruction = EfConstruction ?? HnswOptions.Default.EfConstruction,
            EfSearch = EfSearch ?? HnswOptions.Default.EfSearch,
            Seed = Seed,
        };
        options.Validate();
        return options;
    }

    internal IvfOptions ToIvfOptions()
    {
        var options = new IvfOptions
        {
            NList = NList ?? IvfOptions.Default.NList,
            NProbe = NProbe ?? IvfOptions.Default.NProbe,
            MaxIterations = MaxIterations ?? IvfOptions.Default.MaxIterations,
            Seed = Seed,
        };
        options.Validate();
        return options;
    }

    internal IvfPqOptions ToIvfPqOptions()
    {
        var options = new IvfPqOptions
        {
            NList = NList ?? IvfPqOptions.Default.NList,
            NProbe = NProbe ?? IvfPqOptions.Default.NProbe,
            MaxIterations = MaxIterations ?? IvfPqOptions.Default.MaxIterations,
            M = IvfPqM ?? IvfPqOptions.Default.M,
            NBits = NBits ?? IvfPqOptions.Default.NBits,
            Seed = Seed,
        };
        options.Validate();
        return options;
    }

    internal VamanaOptions ToVamanaOptions()
    {
        var options = new VamanaOptions
        {
            MaxDegree = MaxDegree ?? VamanaOptions.Default.MaxDegree,
            SearchListSize = SearchListSize ?? VamanaOptions.Default.SearchListSize,
            Alpha = Alpha ?? VamanaOptions.Default.Alpha,
            BeamWidth = BeamWidth ?? VamanaOptions.Default.BeamWidth,
            Seed = Seed,
        };
        options.Validate();
        return options;
    }
}
