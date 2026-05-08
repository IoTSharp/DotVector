using Microsoft.Extensions.VectorData;

namespace DotVector.VectorData.Internal;

/// <summary>
/// 在 <c>Microsoft.Extensions.VectorData.DistanceFunction</c> 字符串常量
/// 与 DotVector 协议层 <see cref="Core.Protocol.CreateCollectionRequest.Metric"/> 字符串之间映射。
/// </summary>
internal static class DistanceFunctionMapper
{
    /// <summary>把 VectorData 的 <see cref="DistanceFunction"/> 常量映射为 DotVector Metric 字符串。</summary>
    /// <param name="distanceFunction">VectorData 距离函数常量；为空时回退到 Cosine。</param>
    public static string ToDotVectorMetric(string? distanceFunction)
    {
        if (string.IsNullOrEmpty(distanceFunction))
        {
            return "Cosine";
        }

        return distanceFunction switch
        {
            DistanceFunction.CosineDistance => "Cosine",
            DistanceFunction.CosineSimilarity => "Cosine",
            DistanceFunction.EuclideanDistance => "L2",
            DistanceFunction.EuclideanSquaredDistance => "L2",
            DistanceFunction.DotProductSimilarity => "DotProduct",
            DistanceFunction.NegativeDotProductSimilarity => "InnerProduct",
            DistanceFunction.HammingDistance => "Hamming",
            _ => throw new NotSupportedException(
                $"DotVector 暂不支持 DistanceFunction = '{distanceFunction}'。" +
                " 受支持的取值：CosineDistance / CosineSimilarity / EuclideanDistance / " +
                "EuclideanSquaredDistance / DotProductSimilarity / NegativeDotProductSimilarity / HammingDistance。"),
        };
    }
}
