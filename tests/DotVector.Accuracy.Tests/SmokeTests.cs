namespace DotVector.Accuracy.Tests;

/// <summary>
/// 召回率与精度测试骨架。
/// 将在 M3（HNSW）和 M4（IVF）实现后补充实际召回率测试。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证测试框架正常运行。
    /// </summary>
    [Fact]
    public void Smoke_AlwaysTrue()
    {
        Assert.True(true);
    }

    // TODO(M3): 添加 HNSW 召回率测试（Recall@10 >= 0.95，SIFT-1M 数据集）
    // [Fact]
    // public void HnswIndex_Recall10_OnSift1M_GreaterThan95Percent() { ... }

    // TODO(M4): 添加 IVF 召回率测试（Recall@10 >= 0.90，nprobe=10）
    // [Fact]
    // public void IvfFlatIndex_Recall10_OnSift1M_GreaterThan90Percent() { ... }

    // TODO(M1): 添加 SIMD vs scalar 精度一致性测试（差 < 1e-5）
    // [Fact]
    // public void Distance_L2_Simd_vs_Scalar_DiffLessThan1e5() { ... }
}
