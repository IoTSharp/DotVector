namespace DotVector.Data;

/// <summary>
/// 距离度量类型。映射到协议层 <see cref="DotVector.Core.Protocol.CreateCollectionRequest.Metric"/> 字符串。
/// </summary>
public enum DistanceMetric
{
    /// <summary>余弦距离（默认）。值越小越相似，范围 [0, 2]。</summary>
    Cosine = 0,

    /// <summary>L2（欧氏）距离。值越小越相似。</summary>
    L2 = 1,

    /// <summary>内积。值越大越相似。</summary>
    InnerProduct = 2,

    /// <summary>点积（与 <see cref="InnerProduct"/> 等价，保留兼容）。</summary>
    DotProduct = 3,

    /// <summary>Hamming 距离（用于二值/位图向量）。</summary>
    Hamming = 4,
}

/// <summary>
/// <see cref="DistanceMetric"/> 与协议字符串之间的转换。
/// </summary>
internal static class DistanceMetricExtensions
{
    public static string ToWire(this DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => "Cosine",
        DistanceMetric.L2 => "L2",
        DistanceMetric.InnerProduct => "InnerProduct",
        DistanceMetric.DotProduct => "DotProduct",
        DistanceMetric.Hamming => "Hamming",
        _ => "Cosine",
    };

    public static DistanceMetric Parse(string? wire) => wire switch
    {
        "Cosine" or null or "" => DistanceMetric.Cosine,
        "L2" => DistanceMetric.L2,
        "InnerProduct" => DistanceMetric.InnerProduct,
        "DotProduct" => DistanceMetric.DotProduct,
        "Hamming" => DistanceMetric.Hamming,
        _ => DistanceMetric.Cosine,
    };
}
