using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="VectorDatabase.Compact"/>（M10）：
/// 多次 Flush 产生多个 Segment，Compact 后合并为单个 Segment，且数据完整。
/// </summary>
public sealed class CompactionTests : IDisposable
{
    private readonly string _root;

    public CompactionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-compact-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void Compact_MergesMultipleSegmentsIntoOne()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            col.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));
            db.Flush();
            col.Insert(new VectorRecord<int>(3, new float[] { 1, 1 }));
            col.Insert(new VectorRecord<int>(4, new float[] { 2, 0 }));
            db.Flush();
            col.Insert(new VectorRecord<int>(5, new float[] { 0, 2 }));
            db.Flush();

            // Compact 之前应有 3 个 Segment
            string segDir = FindSegmentsDir(_root);
            Assert.Equal(3, Directory.GetDirectories(segDir).Length);

            db.Compact();

            // Compact 之后应只剩 1 个 Segment
            string[] segsAfter = Directory.GetDirectories(segDir);
            Assert.Single(segsAfter);
        }

        // 重新打开后数据完整
        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            var hits = col.Search(new float[] { 0, 0 }, topK: 10);
            Assert.Equal(5, hits.Count);
        }
    }

    private static string FindSegmentsDir(string root)
    {
        string colsDir = Path.Combine(root, "collections");
        string[] dirs = Directory.GetDirectories(colsDir);
        Assert.Single(dirs);
        return Path.Combine(dirs[0], "segments");
    }
}
