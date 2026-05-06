using DotVector.Api;
using DotVector.Compute;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Accuracy.Tests;

/// <summary>
/// 带过滤的召回率测试（M6）。
/// 在 FlatIndex 上验证：过滤后的 Top-K 与"先取真值再过滤"完全一致（精确检索）。
/// </summary>
public sealed class FilteredRecallTests
{
    [Theory]
    [InlineData(Metric.L2)]
    [InlineData(Metric.Cosine)]
    public void Filtered_Search_Matches_Ground_Truth(Metric metric)
    {
        const int N = 1000;
        const int Dim = 32;
        const int TopK = 10;

        var rng = new Random(42);
        var vectors = new float[N][];
        var tags = new string[N];
        for (int i = 0; i < N; i++)
        {
            var v = new float[Dim];
            for (int j = 0; j < Dim; j++) v[j] = (float)(rng.NextDouble() * 2 - 1);
            vectors[i] = v;
            tags[i] = (i % 3 == 0) ? "A" : (i % 3 == 1) ? "B" : "C";
        }

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("frecall", Dim, metric);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, vectors[i])
            {
                Payload = new Dictionary<string, object> { ["tag"] = tags[i] },
            });
        }

        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) query[j] = (float)(rng.NextDouble() * 2 - 1);

        // ground truth: 全量计算后过滤 tag == "A"
        var scored = new List<(int Id, float Score)>(N);
        for (int i = 0; i < N; i++)
        {
            if (tags[i] == "A")
            {
                scored.Add((i, Distance.Compute(query, vectors[i], metric)));
            }
        }
        scored.Sort((a, b) => metric.IsLargerBetter()
            ? b.Score.CompareTo(a.Score)
            : a.Score.CompareTo(b.Score));
        var truthTop = scored.Take(TopK).Select(t => t.Id).ToHashSet();

        var results = c.Search(query, TopK, Filter.Eq("tag", "A"));
        var got = results.Select(r => r.Key).ToHashSet();

        int hit = truthTop.Intersect(got).Count();
        double recall = hit / (double)truthTop.Count;
        // FlatIndex 是精确检索，期望召回率 = 1.0；远高于 ROADMAP 要求的 < 5% 偏差。
        Assert.Equal(1.0, recall);
        Assert.All(results, r => Assert.Equal("A", r.Payload!["tag"]));
    }
}
