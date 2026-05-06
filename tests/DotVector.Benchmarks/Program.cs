using BenchmarkDotNet.Running;

namespace DotVector.Benchmarks;

/// <summary>
/// DotVector BenchmarkDotNet 基准测试入口。
/// 将在 M8 中添加完整的基准测试套件（L2/Cosine 吞吐量、KNN 延迟、内存占用等）。
/// </summary>
internal static class Program
{
    /// <summary>基准测试入口。</summary>
    /// <remarks>
    /// TODO(M8): 添加 DistanceBenchmark、FlatIndexBenchmark、HnswBenchmark、IvfBenchmark。
    /// TODO(M8): 添加对照 Qdrant / Milvus / pgvector 的对比基准。
    /// </remarks>
    internal static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
