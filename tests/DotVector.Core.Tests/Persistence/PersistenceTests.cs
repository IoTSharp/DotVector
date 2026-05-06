using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 端到端验证 <see cref="VectorDatabase"/> 的目录持久化（M5）：
/// Open → Insert → Dispose → Reopen → 数据保持一致。
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _root;

    public PersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-db-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void OpenInsertReopen_PersistsCollectionAndVectors()
    {
        // 写入
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("vecs", dimensions: 4, metric: Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f, 0f, 0f }));
            col.Insert(new VectorRecord<int>(2, new float[] { 0f, 1f, 0f, 0f }));
            col.Insert(new VectorRecord<int>(3, new float[] { 0f, 0f, 1f, 0f }));
        }

        // 重新打开 → 应通过 catalog + WAL 重建
        using (var db = new VectorDatabase(_root))
        {
            Assert.Equal(1, db.CollectionCount);
            var col = db.GetCollection<int>("vecs");
            var hits = col.Search(new float[] { 1f, 0f, 0f, 0f }, topK: 3);
            Assert.Equal(3, hits.Count);
            Assert.Equal(1, hits[0].Key); // 最近的应该是自己
        }
    }

    [Fact]
    public void DeleteIsReplayed_AfterReopen()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));
            col.Insert(new VectorRecord<int>(2, new float[] { 0f, 1f }));
            col.Delete(1);
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            var hits = col.Search(new float[] { 1f, 0f }, topK: 5);
            Assert.Single(hits);
            Assert.Equal(2, hits[0].Key);
        }
    }

    [Fact]
    public void DropCollection_RemovesFromCatalogAcrossReopen()
    {
        using (var db = new VectorDatabase(_root))
        {
            db.CreateCollection<int>("a", 2, Metric.L2);
            db.CreateCollection<int>("b", 2, Metric.L2);
            Assert.True(db.DropCollection("a"));
        }

        using (var db = new VectorDatabase(_root))
        {
            Assert.Equal(1, db.CollectionCount);
            Assert.Throws<KeyNotFoundException>(() => db.GetCollection<int>("a"));
            Assert.NotNull(db.GetCollection<int>("b"));
        }
    }

    [Fact]
    public void MultipleKeyTypes_Coexist()
    {
        using (var db = new VectorDatabase(_root))
        {
            db.CreateCollection<int>("ints", 2, Metric.L2)
              .Insert(new VectorRecord<int>(7, new float[] { 1f, 1f }));
            db.CreateCollection<long>("longs", 2, Metric.L2)
              .Insert(new VectorRecord<long>(70_000_000_000L, new float[] { 2f, 2f }));
            db.CreateCollection<Guid>("guids", 2, Metric.L2)
              .Insert(new VectorRecord<Guid>(Guid.Parse("11111111-2222-3333-4444-555555555555"), new float[] { 3f, 3f }));
            db.CreateCollection<string>("strings", 2, Metric.L2)
              .Insert(new VectorRecord<string>("hello-世界", new float[] { 4f, 4f }));
        }

        using (var db = new VectorDatabase(_root))
        {
            Assert.Equal(4, db.CollectionCount);
            Assert.Equal(7, db.GetCollection<int>("ints").Search(new float[] { 1f, 1f }, 1)[0].Key);
            Assert.Equal(70_000_000_000L, db.GetCollection<long>("longs").Search(new float[] { 2f, 2f }, 1)[0].Key);
            Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"),
                db.GetCollection<Guid>("guids").Search(new float[] { 3f, 3f }, 1)[0].Key);
            Assert.Equal("hello-世界",
                db.GetCollection<string>("strings").Search(new float[] { 4f, 4f }, 1)[0].Key);
        }
    }

    [Fact]
    public void DirectoryStructure_IsCreated()
    {
        using (var db = new VectorDatabase(_root))
        {
            db.CreateCollection<int>("x", 2, Metric.L2);
        }

        Assert.True(Directory.Exists(_root));
        Assert.True(Directory.Exists(Path.Combine(_root, "wal")));
        Assert.True(Directory.Exists(Path.Combine(_root, "collections")));
        Assert.True(File.Exists(Path.Combine(_root, "catalog.bin")));
    }
}
