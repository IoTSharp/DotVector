using DotVector.Api;
using DotVector.Compute;
using DotVector.Index.DiskAnn;
using DotVector.Model;

namespace DotVector.Accuracy.Tests;

/// <summary>
/// Vamana / DiskANN 是近似（approximate）检索；M12.2 验收要求 Recall@10 ≥ 0.92。
/// </summary>
public sealed class VamanaRecallTests
{
    [Theory]
    [InlineData(Metric.L2, 0)]
    [InlineData(Metric.Cosine, 1)]
    [InlineData(Metric.DotProduct, 2)]
    [InlineData(Metric.InnerProduct, 3)]
    public void VamanaRecall_At10_AtLeast92Percent(Metric metric, int seed)
    {
        const int N = 1000;
        const int Dim = 64;
        const int TopK = 10;

        var rng = new Random(seed);
        var vectors = new float[N][];
        for (int i = 0; i < N; i++)
        {
            var v = new float[Dim];
            for (int j = 0; j < Dim; j++) { v[j] = (float)(rng.NextDouble() * 2 - 1); }
            vectors[i] = v;
        }

        var options = new VamanaOptions
        {
            MaxDegree = 32,
            SearchListSize = 100,
            Alpha = 1.2f,
            BeamWidth = 4,
            Seed = 42,
        };

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("recall", Dim, metric, options);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, vectors[i]));
        }

        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) { query[j] = (float)(rng.NextDouble() * 2 - 1); }

        // Ground truth：暴力 Distance 计算。
        var scored = new (int Id, float Score)[N];
        for (int i = 0; i < N; i++)
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
        var truthTop = scored.Take(TopK).Select(t => t.Id).ToHashSet();

        var results = c.Search(query, TopK);
        var got = results.Select(r => r.Key).ToHashSet();

        int hit = truthTop.Intersect(got).Count();
        double recall = hit / (double)TopK;
        Assert.True(recall >= 0.92, $"Recall@{TopK} = {recall:F3}, 期望 ≥ 0.92（metric={metric}, seed={seed}）。");
    }
}
