using DotVector.Api;
using DotVector.Compute;
using DotVector.Model;

namespace DotVector.Accuracy.Tests;

/// <summary>
/// FlatIndex 是精确（exact）检索；Recall@K 必为 1.0。
/// </summary>
public sealed class FlatRecallTests
{
    [Theory]
    [InlineData(Metric.L2, 0)]
    [InlineData(Metric.Cosine, 1)]
    [InlineData(Metric.DotProduct, 2)]
    [InlineData(Metric.InnerProduct, 3)]
    public void FlatRecall_At10_IsOne(Metric metric, int seed)
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

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("recall", Dim, metric);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, vectors[i]));
        }

        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) { query[j] = (float)(rng.NextDouble() * 2 - 1); }

        // 直接用 Distance 计算 ground truth。
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
        Assert.Equal(1.0, recall);
    }
}
