using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证崩溃恢复场景（M10）：遗留的 <c>seg-XXXXXX.tmp</c> 目录应在打开时被忽略。
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly string _root;

    public CrashRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-crash-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void LeftoverTmpSegment_IsIgnoredOnOpen()
    {
        using (var db = new VectorDatabase(_root))
        {
            var c = db.CreateCollection<int>("c", 2, Metric.L2);
            c.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            c.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));
            db.Flush();
        }

        // 找到该集合的 segments 目录，注入一个 .tmp 残留
        string segsDir = FindSegmentsDir(_root);
        string tmpSeg = Path.Combine(segsDir, "seg-999999.tmp");
        Directory.CreateDirectory(tmpSeg);
        File.WriteAllBytes(Path.Combine(tmpSeg, "seg.hdr"), new byte[16]);
        File.WriteAllBytes(Path.Combine(tmpSeg, "vectors.bin"), new byte[8]);

        // 应能正常打开，且数据完整
        using (var db = new VectorDatabase(_root))
        {
            var c = db.GetCollection<int>("c");
            var hits = c.Search(new float[] { 0, 0 }, topK: 5);
            Assert.Equal(2, hits.Count);
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
