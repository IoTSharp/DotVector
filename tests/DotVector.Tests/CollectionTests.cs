using DotVector.Api;
using DotVector.Model;

namespace DotVector.Tests;

public sealed class CollectionTests
{
    [Fact]
    public void CreateCollection_DuplicateName_Throws()
    {
        using var db = new VectorDatabase();
        db.CreateCollection<int>("c1", 4, Metric.L2);
        Assert.Throws<InvalidOperationException>(() => db.CreateCollection<int>("c1", 4, Metric.L2));
    }

    [Fact]
    public void GetCollection_WrongKeyType_Throws()
    {
        using var db = new VectorDatabase();
        db.CreateCollection<int>("c1", 4, Metric.L2);
        Assert.Throws<InvalidOperationException>(() => db.GetCollection<string>("c1"));
    }

    [Fact]
    public void GetCollection_Missing_Throws()
    {
        using var db = new VectorDatabase();
        Assert.Throws<KeyNotFoundException>(() => db.GetCollection<int>("ghost"));
    }

    [Fact]
    public void DropCollection_ReleasesResources()
    {
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("c1", 4, Metric.L2);
        Assert.True(db.DropCollection("c1"));
        Assert.False(db.DropCollection("c1"));
        Assert.Throws<ObjectDisposedException>(() => c.Insert(new VectorRecord<int>(1, new float[4])));
    }

    [Fact]
    public void Insert_Search_RoundTrip()
    {
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("docs", 3, Metric.L2);
        c.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f, 0f }));
        c.Insert(new VectorRecord<int>(2, new float[] { 0f, 1f, 0f }));
        c.Insert(new VectorRecord<int>(3, new float[] { 0f, 0f, 1f }));

        var results = c.Search(new float[] { 0.9f, 0.1f, 0f }, topK: 2);
        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Key);
    }

    [Fact]
    public void InsertBatch_AddsAll()
    {
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("docs", 2, Metric.L2);
        c.InsertBatch(new[]
        {
            new VectorRecord<int>(1, new float[] { 1f, 0f }),
            new VectorRecord<int>(2, new float[] { 0f, 1f }),
        });
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void Delete_RemovesRecord()
    {
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("docs", 2, Metric.L2);
        c.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));
        Assert.True(c.Delete(1));
        Assert.Equal(0, c.Count);
        Assert.False(c.Delete(1));
    }

    [Fact]
    public void Search_ConcurrentReads_ReturnIdenticalTop1()
    {
        using var db = new VectorDatabase();
        var c = db.CreateCollection<int>("docs", 16, Metric.L2);
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            var v = new float[16];
            for (int j = 0; j < 16; j++) { v[j] = (float)rng.NextDouble(); }
            c.Insert(new VectorRecord<int>(i, v));
        }

        var query = new float[16];
        for (int j = 0; j < 16; j++) { query[j] = (float)rng.NextDouble(); }

        int baseTop = c.Search(query, 1)[0].Key;

        Parallel.For(0, 1000, _ =>
        {
            int top = c.Search(query, 1)[0].Key;
            Assert.Equal(baseTop, top);
        });
    }
}
