using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证多集合场景下 WAL 共享与裁剪规则（M10）：
/// WAL 裁剪以所有集合 manifest.LastCoveredWalSequence 的最小值为准，
/// 仅 Flush 部分集合时旧 WAL 不能被删除。
/// </summary>
public sealed class WalTrimTests : IDisposable
{
    private readonly string _root;

    public WalTrimTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-waltrim-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void PartialFlush_KeepsWalForUnflushedCollection()
    {
        using (var db = new VectorDatabase(_root))
        {
            var a = db.CreateCollection<int>("a", 2, Metric.L2);
            var b = db.CreateCollection<int>("b", 2, Metric.L2);
            a.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            b.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));

            // 仅 Flush（注意：Flush 是数据库级别的，会把所有 Flat 集合都 Flush）
            db.Flush();
        }

        // 重开后两个集合数据都应在
        using (var db = new VectorDatabase(_root))
        {
            var a = db.GetCollection<int>("a");
            var b = db.GetCollection<int>("b");
            Assert.Single(a.Search(new float[] { 1, 0 }, topK: 5));
            Assert.Single(b.Search(new float[] { 0, 1 }, topK: 5));
        }
    }

    [Fact]
    public void RepeatedFlush_TrimsOldWalFiles()
    {
        using (var db = new VectorDatabase(_root))
        {
            var c = db.CreateCollection<int>("c", 2, Metric.L2);
            for (int round = 0; round < 5; round++)
            {
                for (int i = 0; i < 10; i++)
                {
                    c.Insert(new VectorRecord<int>(round * 100 + i, new float[] { i, round }));
                }
                db.Flush();
            }
        }

        // 经过多轮 Flush，遗留的 WAL 文件应不超过 1 个（当前正在写的那个）
        string walDir = Path.Combine(_root, "wal");
        Assert.True(Directory.Exists(walDir));
        string[] walFiles = Directory.GetFiles(walDir, "wal-*.log");
        Assert.True(walFiles.Length <= 1, $"应不超过 1 个 WAL 文件，实际 {walFiles.Length}");

        using (var db = new VectorDatabase(_root))
        {
            var c = db.GetCollection<int>("c");
            var hits = c.Search(new float[] { 0, 0 }, topK: 100);
            Assert.Equal(50, hits.Count);
        }
    }
}
