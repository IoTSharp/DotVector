using System.Numerics.Tensors;
using DotVector.Model;

namespace DotVector.Compute;

/// <summary>
/// 向量距离函数实现，基于 <see cref="TensorPrimitives"/> 提供 SIMD 加速。
/// 在支持 AVX-512 的 x64 平台上自动使用 Vector512&lt;float&gt;，
/// 在 ARM64 上自动使用 NEON，其他平台退回 scalar 实现。
/// </summary>
/// <remarks>
/// TODO(M1): 补充完整的 L2 / Cosine / InnerProduct / Hamming / DotProduct 实现，
///           并添加 SIMD vs scalar 精度一致性测试（差 &lt; 1e-5）。
/// </remarks>
public static class Distance
{
    /// <summary>
    /// 计算两个向量的 L2（欧氏）距离平方。
    /// </summary>
    /// <param name="a">向量 A（长度须与 B 相同）。</param>
    /// <param name="b">向量 B。</param>
    /// <returns>L2 距离平方（非负值）。</returns>
    /// <exception cref="ArgumentException">当 <paramref name="a"/> 与 <paramref name="b"/> 长度不同时抛出。</exception>
    public static float L2Squared(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"向量维度不匹配：a.Length={a.Length}, b.Length={b.Length}。");
        }

        // TODO(M1): 使用 TensorPrimitives.Distance 或手动累加差值平方
        // 当前占位实现（scalar 回退）
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float diff = a[i] - b[i];
            sum += diff * diff;
        }

        return sum;
    }

    /// <summary>
    /// 计算两个向量的余弦距离（1 - 余弦相似度）。
    /// 向量须为非零向量，否则结果未定义。
    /// </summary>
    /// <param name="a">向量 A。</param>
    /// <param name="b">向量 B。</param>
    /// <returns>余弦距离，范围 [0, 2]。</returns>
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"向量维度不匹配：a.Length={a.Length}, b.Length={b.Length}。");
        }

        // TODO(M1): 使用 TensorPrimitives.CosineSimilarity，当前占位
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < float.Epsilon ? 1f : 1f - (dot / denom);
    }

    /// <summary>
    /// 计算两个向量的内积（点积）。
    /// </summary>
    /// <param name="a">向量 A。</param>
    /// <param name="b">向量 B。</param>
    /// <returns>内积值。</returns>
    public static float InnerProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"向量维度不匹配：a.Length={a.Length}, b.Length={b.Length}。");
        }

        // TODO(M1): 使用 TensorPrimitives.Dot
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary>
    /// 根据 <see cref="Metric"/> 枚举分派到对应距离函数。
    /// </summary>
    /// <param name="a">向量 A。</param>
    /// <param name="b">向量 B。</param>
    /// <param name="metric">度量类型。</param>
    /// <returns>距离或相似度值（语义取决于 metric，见 <see cref="Metric"/> 注释）。</returns>
    public static float Compute(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Metric metric)
        => metric switch
        {
            Metric.L2 => L2Squared(a, b),
            Metric.Cosine => Cosine(a, b),
            Metric.InnerProduct or Metric.DotProduct => InnerProduct(a, b),
            // TODO(M1): 实现 Hamming（二值向量 BitOperations.PopCount）
            _ => throw new NotSupportedException($"尚未支持的度量类型：{metric}。"),
        };
}
