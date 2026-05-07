using DotVector.Api;
using DotVector.Format;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="VectorDatabase.Flush"/>（M10）：
/// Flush 把 MemTable 落盘成新 Segment，并旋转/裁剪 WAL；重新打开后数据保持一致。
/// </summary>
public sealed class SegmentFlushTests : IDisposable
{
    private readonly string _root;

    public SegmentFlushTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-flush-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void Flush_CreatesSegmentAndTrimsWal()
    {
        Guid collectionId;
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 4, Metric.L2);
            for (int i = 0; i < 100; i++)
            {
                col.Insert(new VectorRecord<int>(i, new float[] { i, 0, 0, 0 }));
            }
            db.Flush();
            collectionId = GetCollectionId(_root, "c");
        }

        // segments/seg-000001/ 应该存在
        string segDir = Path.Combine(_root, "collections", collectionId.ToString("N"), "segments");
        Assert.True(Directory.Exists(segDir));
        string[] segs = Directory.GetDirectories(segDir);
        Assert.Single(segs);
        Assert.True(File.Exists(Path.Combine(segs[0], "seg.hdr")));
        Assert.True(File.Exists(Path.Combine(segs[0], "vectors.bin")));
        Assert.True(File.Exists(Path.Combine(segs[0], "keys.bin")));

        // manifest.bin 已写入
        string manifest = Path.Combine(_root, "collections", collectionId.ToString("N"), "manifest.bin");
        Assert.True(File.Exists(manifest));

        // 重新打开应能搜到全部数据
        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            var hits = col.Search(new float[] { 50, 0, 0, 0 }, topK: 1);
            Assert.Single(hits);
            Assert.Equal(50, hits[0].Key);
        }
    }

    [Fact]
    public void FlushThenInsertMore_ReopenRestoresBoth()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            col.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));
            db.Flush();
            // Flush 之后再写，应进入新的 WAL
            col.Insert(new VectorRecord<int>(3, new float[] { 1, 1 }));
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            var hits = col.Search(new float[] { 0, 0 }, topK: 5);
            Assert.Equal(3, hits.Count);
        }
    }

    private static Guid GetCollectionId(string root, string name)
    {
        // catalog.bin 不方便直接解析；通过目录名穷举最简单。
        string colsDir = Path.Combine(root, "collections");
        string[] dirs = Directory.GetDirectories(colsDir);
        Assert.Single(dirs);
        return Guid.ParseExact(Path.GetFileName(dirs[0]), "N");
    }
}
