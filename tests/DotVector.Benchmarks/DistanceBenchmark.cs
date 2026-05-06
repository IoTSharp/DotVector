using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using DotVector.Compute;

namespace DotVector.Benchmarks;

/// <summary>
/// 距离函数基准测试，对比 SIMD（<see cref="Distance"/>）与 scalar 参考实现，
/// 覆盖嵌入模型常见维度：128 / 384 / 1536 / 4096。
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class DistanceBenchmark
{
    private float[] _a = [];
    private float[] _b = [];
    private byte[] _bitsA = [];
    private byte[] _bitsB = [];

    /// <summary>向量维度。</summary>
    [Params(128, 384, 1536, 4096)]
    public int Dim { get; set; }

    /// <summary>初始化随机数据。</summary>
    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _a = new float[Dim];
        _b = new float[Dim];
        for (int i = 0; i < Dim; i++)
        {
            _a[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            _b[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        int byteLen = Math.Max(1, Dim / 8);
        _bitsA = new byte[byteLen];
        _bitsB = new byte[byteLen];
        rng.NextBytes(_bitsA);
        rng.NextBytes(_bitsB);
    }

    /// <summary>L2 距离平方（SIMD）。</summary>
    [Benchmark(Baseline = true)]
    public float L2Squared_Simd() => Distance.L2Squared(_a, _b);

    /// <summary>L2 距离平方（scalar 参考）。</summary>
    [Benchmark]
    public float L2Squared_Scalar() => Distance.L2SquaredScalar(_a, _b);

    /// <summary>余弦距离（SIMD）。</summary>
    [Benchmark]
    public float Cosine_Simd() => Distance.Cosine(_a, _b);

    /// <summary>余弦距离（scalar 参考）。</summary>
    [Benchmark]
    public float Cosine_Scalar() => Distance.CosineScalar(_a, _b);

    /// <summary>内积（SIMD）。</summary>
    [Benchmark]
    public float InnerProduct_Simd() => Distance.InnerProduct(_a, _b);

    /// <summary>内积（scalar 参考）。</summary>
    [Benchmark]
    public float InnerProduct_Scalar() => Distance.InnerProductScalar(_a, _b);

    /// <summary>汉明距离（PopCount）。</summary>
    [Benchmark]
    public int Hamming_PopCount() => Distance.Hamming(_bitsA, _bitsB);

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(5));
        }
    }
}
