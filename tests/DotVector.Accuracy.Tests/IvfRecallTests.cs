using DotVector.Api;
using DotVector.Compute;
using DotVector.Index.Ivf;
using DotVector.Model;

namespace DotVector.Accuracy.Tests;

/// <summary>
/// IVF 是近似（approximate）检索；M4 验收：
/// IVF-Flat Recall@10 ≥ 0.90；IVF-PQ Recall@10 ≥ 0.50（量化精度损失换取存储与速度）。
/// </summary>
public sealed class IvfRecallTests
{
    [Theory]
    [InlineData(Metric.L2, 0)]
    [InlineData(Metric.Cosine, 1)]
    [InlineData(Metric.DotProduct, 2)]
    [InlineData(Metric.InnerProduct, 3)]
    public void IvfFlatRecall_At10_AtLeast90Percent(Metric metric, int seed)
    {
        const int N = 1024;
        const int Dim = 64;
        const int TopK = 10;
        const int NumClusters = 16;

        // 生成聚类数据（Gaussian mixture）：IVF 在结构化数据上才能体现出剪枝优势。
        var vectors = GenerateClustered(N, Dim, NumClusters, seed);

        var options = new IvfOptions
        {
            NList = 16,
            NProbe = 6,   // 探查 ≈ 38% 簇即可达到 ≥ 0.90 召回
            MaxIterations = 25,
            Seed = 42,
        };

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("ivf-flat-recall", Dim, metric, options);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, vectors[i]));
        }

        var rng = new Random(seed + 1000);
        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) { query[j] = (float)(rng.NextDouble() * 2 - 1); }

        var truth = ComputeTruth(query, vectors, metric, TopK);
        var got = c.Search(query, TopK).Select(r => r.Key).ToHashSet();

        int hit = truth.Intersect(got).Count();
        double recall = hit / (double)TopK;
        Assert.True(recall >= 0.90,
            $"IVF-Flat Recall@{TopK} = {recall:F3}, 期望 ≥ 0.90（metric={metric}, seed={seed}）。");
    }

    [Theory]
    [InlineData(Metric.L2, 0)]
    [InlineData(Metric.L2, 1)]
    public void IvfPqRecall_At10_AtLeast50Percent(Metric metric, int seed)
    {
        const int N = 1024;
        const int Dim = 64;
        const int TopK = 10;
        const int NumClusters = 16;

        var vectors = GenerateClustered(N, Dim, NumClusters, seed);

        var options = new IvfPqOptions
        {
            NList = 16,
            NProbe = 8,
            M = 8,           // 64 / 8 = 8 维子空间
            NBits = 8,
            MaxIterations = 25,
            Seed = 42,
        };

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("ivf-pq-recall", Dim, metric, options);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, vectors[i]));
        }

        var rng = new Random(seed + 1000);
        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) { query[j] = (float)(rng.NextDouble() * 2 - 1); }

        var truth = ComputeTruth(query, vectors, metric, TopK);
        var got = c.Search(query, TopK).Select(r => r.Key).ToHashSet();

        int hit = truth.Intersect(got).Count();
        double recall = hit / (double)TopK;
        Assert.True(recall >= 0.50,
            $"IVF-PQ Recall@{TopK} = {recall:F3}, 期望 ≥ 0.50（metric={metric}, seed={seed}）。");
    }

    private static float[][] GenerateClustered(int n, int dim, int numClusters, int seed)
    {
        var rng = new Random(seed);
        // 1. 生成 K 个簇心，分布在 [-5, 5]^dim 上以保证可分性。
        var centers = new float[numClusters][];
        for (int k = 0; k < numClusters; k++)
        {
            var c = new float[dim];
            for (int j = 0; j < dim; j++) { c[j] = (float)(rng.NextDouble() * 10 - 5); }
            centers[k] = c;
        }
        // 2. 每个样本绕一个簇心做 σ=0.3 的高斯扰动（远小于簇间距离）。
        var vectors = new float[n][];
        for (int i = 0; i < n; i++)
        {
            int k = i % numClusters;
            var v = new float[dim];
            for (int j = 0; j < dim; j++)
            {
                // Box-Muller
                double u1 = 1.0 - rng.NextDouble();
                double u2 = 1.0 - rng.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                v[j] = centers[k][j] + (float)(0.3 * z);
            }
            vectors[i] = v;
        }
        return vectors;
    }

    private static HashSet<int> ComputeTruth(
        float[] query, float[][] vectors, Metric metric, int topK)
    {
        int n = vectors.Length;
        var scored = new (int Id, float Score)[n];
        for (int i = 0; i < n; i++)
        {
            scored[i] = (i, Distance.Compute(query, vectors[i], metric));
        }
        if (metric.IsLargerBetter())
        {
            Array.Sort(scored, (a, b) => b.Score.CompareTo(a.Score));
        }
        else
        {
            Array.Sort(scored, (a, b) => a.Score.CompareTo(b.Score));
        }
        return scored.Take(topK).Select(t => t.Id).ToHashSet();
    }
}
