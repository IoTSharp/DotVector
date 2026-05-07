using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 payload 在持久化存储中的端到端行为（M11）：
/// SetPayload → WAL 重启恢复、Flush → Segment payload.bin 重启恢复、Compaction 合并 payload。
/// </summary>
public sealed class PayloadPersistenceTests : IDisposable
{
    private readonly string _root;

    public PayloadPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-payload-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void SetPayload_RestoredFromWal_AfterReopen()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            col.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));
            col.SetPayload(1, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["category"] = "alpha",
                ["score"] = 9.5,
                ["active"] = true,
            });
            col.SetPayload(2, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["category"] = "beta",
            });
            // 不调用 Flush —— 应只通过 WAL 重放恢复
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            var p1 = col.GetPayload(1);
            Assert.NotNull(p1);
            Assert.Equal("alpha", p1!["category"]);
            Assert.Equal(9.5, (double)p1["score"]!);
            Assert.Equal(true, p1["active"]);

            var p2 = col.GetPayload(2);
            Assert.NotNull(p2);
            Assert.Equal("beta", p2!["category"]);
        }
    }

    [Fact]
    public void SetPayload_RestoredFromSegment_AfterFlushAndReopen()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            for (int i = 0; i < 10; i++)
            {
                col.Insert(new VectorRecord<int>(i, new float[] { i, 0 }));
                col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["idx"] = i,
                    ["name"] = $"item-{i}",
                });
            }
            db.Flush();
        }

        // payload.bin 应该写入了 Segment
        string colsDir = Path.Combine(_root, "collections");
        string colDir = Directory.GetDirectories(colsDir).Single();
        string segDir = Directory.GetDirectories(Path.Combine(colDir, "segments")).Single();
        Assert.True(File.Exists(Path.Combine(segDir, "payload.bin")));

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            for (int i = 0; i < 10; i++)
            {
                var p = col.GetPayload(i);
                Assert.NotNull(p);
                Assert.Equal((long)i, p!["idx"]);
                Assert.Equal($"item-{i}", p["name"]);
            }
        }
    }

    [Fact]
    public void SetPayload_EmptyClearsPayload_AndPersists()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            col.SetPayload(1, new Dictionary<string, object?> { ["a"] = 1 });
            col.SetPayload(1, null);
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            Assert.Null(col.GetPayload(1));
        }
    }

    [Fact]
    public void Compaction_PreservesPayloadAcrossSegments()
    {
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 2, Metric.L2);
            col.Insert(new VectorRecord<int>(1, new float[] { 1, 0 }));
            col.SetPayload(1, new Dictionary<string, object?> { ["k"] = "v1" });
            db.Flush();

            col.Insert(new VectorRecord<int>(2, new float[] { 0, 1 }));
            col.SetPayload(2, new Dictionary<string, object?> { ["k"] = "v2" });
            db.Flush();

            col.Compact();
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            Assert.Equal("v1", col.GetPayload(1)!["k"]);
            Assert.Equal("v2", col.GetPayload(2)!["k"]);
        }
    }
}
