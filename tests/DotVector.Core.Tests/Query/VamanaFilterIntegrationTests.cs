using DotVector.Api;
using DotVector.Compute;
using DotVector.Index.DiskAnn;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Core.Tests.Query;

/// <summary>
/// M12.4 — Vamana 索引上的标量 pre-filter 端到端测试。
/// 覆盖：Eq、Range、And、空候选集、与 Flat 的一致性。
/// </summary>
public sealed class VamanaFilterIntegrationTests
{
    private static VamanaOptions DeterministicOptions()
        => new()
        {
            MaxDegree = 16,
            SearchListSize = 64,
            Alpha = 1.2f,
            BeamWidth = 4,
            Seed = 42,
        };

    [Fact]
    public void Search_OnVamana_WithEqFilter_ReturnsOnlyMatchingPayloads()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("vdocs", 2, Metric.L2, DeterministicOptions());

        for (int i = 1; i <= 30; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = (i % 2 == 0) ? "red" : "blue",
            });
        }

        var results = col.Search(new float[] { 0f, 0f }, topK: 5, filter: Filter.Eq("color", "red"));

        Assert.NotEmpty(results);
        foreach (var r in results)
        {
            Assert.Equal("red", r.Payload?["color"]);
            Assert.Equal(0, r.Key % 2);
        }
    }

    [Fact]
    public void Search_OnVamana_WithEmptyCandidateSet_ReturnsEmpty()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("vdocs", 2, Metric.L2, DeterministicOptions());
        for (int i = 1; i <= 10; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = "blue",
            });
        }

        var results = col.Search(new float[] { 0f, 0f }, topK: 5, filter: Filter.Eq("color", "red"));
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(Metric.L2)]
    [InlineData(Metric.Cosine)]
    public void Search_OnVamana_FilteredResults_MatchFlatBaseline(Metric metric)
    {
        const int N = 300;
        const int Dim = 16;
        const int TopK = 10;

        var rng = new Random(2025);
        var vectors = new float[N][];
        var tags = new string[N];
        for (int i = 0; i < N; i++)
        {
            var v = new float[Dim];
            for (int j = 0; j < Dim; j++) { v[j] = (float)(rng.NextDouble() * 2 - 1); }
            vectors[i] = v;
            tags[i] = (i % 3) switch { 0 => "A", 1 => "B", _ => "C" };
        }

        using var db = new VectorDatabase();
        var flat = db.CreateCollection<int>("flat", Dim, metric);
        var vamana = db.CreateCollection<int>("vamana", Dim, metric, DeterministicOptions());

        for (int i = 0; i < N; i++)
        {
            flat.Insert(new VectorRecord<int>(i, vectors[i]));
            vamana.Insert(new VectorRecord<int>(i, vectors[i]));
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["tag"] = tags[i] };
            flat.SetPayload(i, payload);
            vamana.SetPayload(i, payload);
        }

        var query = new float[Dim];
        for (int j = 0; j < Dim; j++) { query[j] = (float)(rng.NextDouble() * 2 - 1); }

        var filter = Filter.Eq("tag", "A");
        var flatResults = flat.Search(query, TopK, filter).Select(r => r.Key).ToHashSet();
        var vamanaResults = vamana.Search(query, TopK, filter).Select(r => r.Key).ToHashSet();

        // pre-filter 后两者都退化为候选集精确扫描，结果必须一致。
        Assert.Equal(flatResults, vamanaResults);
        Assert.Equal(TopK, vamanaResults.Count);
    }

    [Fact]
    public void Search_OnVamana_WithAndFilter_ResolvesViaPreFilter()
    {
        const int Dim = 8;
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("vdocs", Dim, Metric.L2, DeterministicOptions());

        var rng = new Random(11);
        for (int i = 0; i < 60; i++)
        {
            var v = new float[Dim];
            for (int j = 0; j < Dim; j++) { v[j] = (float)(rng.NextDouble() * 2 - 1); }
            col.Insert(new VectorRecord<int>(i, v));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = (i % 2 == 0) ? "red" : "blue",
                ["score"] = (double)i,
            });
        }

        var query = new float[Dim];
        var filter = Filter.And(
            Filter.Eq("color", "red"),
            Filter.Range("score", min: 10.0, max: 40.0, minInclusive: true, maxInclusive: true));

        var results = col.Search(query, topK: 5, filter: filter);

        Assert.NotEmpty(results);
        foreach (var r in results)
        {
            Assert.Equal("red", r.Payload?["color"]);
            double s = Convert.ToDouble(r.Payload?["score"]);
            Assert.InRange(s, 10.0, 40.0);
        }
    }
}
