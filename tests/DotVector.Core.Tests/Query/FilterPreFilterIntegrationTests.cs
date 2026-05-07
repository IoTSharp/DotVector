using DotVector.Api;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Core.Tests.Query;

/// <summary>M11：标量 pre-filter 与 Collection.Search 端到端集成。</summary>
public class FilterPreFilterIntegrationTests
{
    [Fact]
    public void Search_WithEqFilter_ReturnsOnlyMatchingPayloads()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("docs", 2, Metric.L2);

        for (int i = 1; i <= 20; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = (i % 2 == 0) ? "red" : "blue",
            });
        }

        IReadOnlyList<SearchResult<int>> results = col.Search(
            new float[] { 0f, 0f },
            topK: 5,
            filter: Filter.Eq("color", "red"));

        Assert.NotEmpty(results);
        foreach (var r in results)
        {
            Assert.Equal("red", r.Payload?["color"]);
            Assert.Equal(0, r.Key % 2);
        }
    }

    [Fact]
    public void Search_WithRangeFilter_RestrictsCandidates()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("docs", 2, Metric.L2);

        for (int i = 1; i <= 20; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["score"] = (double)i,
            });
        }

        var results = col.Search(
            new float[] { 0f, 0f },
            topK: 10,
            filter: Filter.Range("score", min: 5.0, max: 10.0, minInclusive: true, maxInclusive: true));

        Assert.NotEmpty(results);
        foreach (var r in results)
        {
            double s = Convert.ToDouble(r.Payload!["score"]);
            Assert.InRange(s, 5.0, 10.0);
        }
    }

    [Fact]
    public void Search_WithAndFilter_IntersectsCandidates()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("docs", 2, Metric.L2);

        for (int i = 1; i <= 30; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = (i % 2 == 0) ? "red" : "blue",
                ["score"] = (double)i,
            });
        }

        var f = Filter.And(
            Filter.Eq("color", "red"),
            Filter.Range("score", min: 0.0, max: 10.0, minInclusive: true, maxInclusive: true));

        var results = col.Search(new float[] { 0f, 0f }, topK: 10, filter: f);

        Assert.NotEmpty(results);
        foreach (var r in results)
        {
            Assert.Equal("red", r.Payload?["color"]);
            double s = Convert.ToDouble(r.Payload!["score"]);
            Assert.InRange(s, 0.0, 10.0);
        }
    }

    [Fact]
    public void Search_WithOrFilter_FallsBackToPostFilter_StillCorrect()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("docs", 2, Metric.L2);

        for (int i = 1; i <= 20; i++)
        {
            col.Insert(new VectorRecord<int>(i, new float[] { i, 0f }));
            col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["color"] = (i % 2 == 0) ? "red" : "blue",
            });
        }

        // Or 不会被 ScalarIndex 下推；走 post-filter 回退路径，结果仍然正确。
        var f = Filter.Or(Filter.Eq("color", "red"), Filter.Eq("color", "blue"));
        var results = col.Search(new float[] { 0f, 0f }, topK: 5, filter: f);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void Search_AfterDeletePayload_RemovesFromIndex()
    {
        using var db = new VectorDatabase();
        var col = db.CreateCollection<int>("docs", 2, Metric.L2);

        col.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));
        col.SetPayload(1, new Dictionary<string, object?> { ["color"] = "red" });
        col.Insert(new VectorRecord<int>(2, new float[] { 2f, 0f }));
        col.SetPayload(2, new Dictionary<string, object?> { ["color"] = "red" });

        col.Delete(1);

        var results = col.Search(new float[] { 0f, 0f }, topK: 10, filter: Filter.Eq("color", "red"));
        Assert.Single(results);
        Assert.Equal(2, results[0].Key);
    }
}
