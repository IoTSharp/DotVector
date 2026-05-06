using DotVector.Api;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Core.Tests.Query;

/// <summary>
/// <see cref="Collection{TKey}.Search(System.ReadOnlySpan{float}, int, Filter?)"/> 端到端测试（M6）。
/// </summary>
public sealed class FilteredSearchTests
{
    private static float[] Vec(int dim, int seed)
    {
        var rng = new Random(seed);
        var v = new float[dim];
        for (int i = 0; i < dim; i++) v[i] = (float)(rng.NextDouble() * 2 - 1);
        return v;
    }

    [Fact]
    public void Filter_Narrows_Results_Correctly()
    {
        const int N = 200;
        const int Dim = 16;

        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("filtered", Dim, Metric.L2);
        for (int i = 0; i < N; i++)
        {
            c.Insert(new VectorRecord<int>(i, Vec(Dim, i))
            {
                Payload = new Dictionary<string, object>
                {
                    ["city"] = (i % 2 == 0) ? "BJ" : "SH",
                    ["age"] = 18 + (i % 50),
                },
            });
        }

        var query = Vec(Dim, 999);

        // 无过滤
        var all = c.Search(query, 10);
        Assert.Equal(10, all.Count);

        // city == BJ
        var bj = c.Search(query, 10, Filter.Eq("city", "BJ"));
        Assert.Equal(10, bj.Count);
        Assert.All(bj, r => Assert.Equal("BJ", r.Payload!["city"]));

        // age in [25, 35]
        var ageRange = c.Search(query, 10, Filter.Range("age", 25, 35));
        Assert.All(ageRange, r =>
        {
            int age = (int)r.Payload!["age"]!;
            Assert.InRange(age, 25, 35);
        });

        // 复合 AND
        var both = c.Search(query, 10,
            Filter.And(Filter.Eq("city", "BJ"), Filter.Range("age", 25, 35)));
        Assert.All(both, r =>
        {
            Assert.Equal("BJ", r.Payload!["city"]);
            int age = (int)r.Payload!["age"]!;
            Assert.InRange(age, 25, 35);
        });
    }

    [Fact]
    public void Filter_With_No_Match_Returns_Empty()
    {
        const int Dim = 8;
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("empty", Dim, Metric.L2);
        for (int i = 0; i < 50; i++)
        {
            c.Insert(new VectorRecord<int>(i, Vec(Dim, i))
            {
                Payload = new Dictionary<string, object> { ["tag"] = "A" },
            });
        }

        var results = c.Search(Vec(Dim, 1), 10, Filter.Eq("tag", "Z"));
        Assert.Empty(results);
    }

    [Fact]
    public void Delete_Removes_Payload()
    {
        const int Dim = 4;
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("del", Dim, Metric.L2);
        c.Insert(new VectorRecord<int>(1, Vec(Dim, 1))
        {
            Payload = new Dictionary<string, object> { ["x"] = 1 },
        });
        Assert.NotNull(c.GetPayload(1));
        Assert.True(c.Delete(1));
        Assert.Null(c.GetPayload(1));
    }

    [Fact]
    public void InsertBatch_Stores_Payload()
    {
        const int Dim = 4;
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("batch", Dim, Metric.L2);
        var batch = new List<VectorRecord<int>>();
        for (int i = 0; i < 10; i++)
        {
            batch.Add(new VectorRecord<int>(i, Vec(Dim, i))
            {
                Payload = new Dictionary<string, object> { ["i"] = i },
            });
        }
        c.InsertBatch(batch);
        for (int i = 0; i < 10; i++)
        {
            var p = c.GetPayload(i);
            Assert.NotNull(p);
            Assert.Equal(i, (int)p!["i"]!);
        }
    }

    [Fact]
    public void Search_Without_Payload_Has_Null_Payload()
    {
        const int Dim = 4;
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("nopayload", Dim, Metric.L2);
        for (int i = 0; i < 10; i++)
        {
            c.Insert(new VectorRecord<int>(i, Vec(Dim, i)));
        }
        var r = c.Search(Vec(Dim, 0), 3);
        Assert.All(r, x => Assert.Null(x.Payload));
    }
}
