using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="Collection{TKey}.TryGet"/> / <see cref="Collection{TKey}.GetMany"/>
/// 在持久化恢复后的行为（M7.1）。
/// </summary>
public sealed class CollectionTryGetTests : IDisposable
{
    private readonly string _root;

    public CollectionTryGetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-tryget-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void TryGet_ReturnsVectorAndPayload_FromMemory()
    {
        using var db = new VectorDatabase(_root);
        var col = db.CreateCollection<int>("c", 3, Metric.L2);
        col.Insert(new VectorRecord<int>(1, new float[] { 1f, 2f, 3f }));
        col.SetPayload(1, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = "alpha",
        });

        Assert.True(col.TryGet(1, out var record));
        Assert.NotNull(record);
        Assert.Equal(1, record!.Key);
        Assert.Equal(new float[] { 1f, 2f, 3f }, record.Vector);
        Assert.NotNull(record.Payload);
        Assert.Equal("alpha", record.Payload!["title"]);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForMissingKey()
    {
        using var db = new VectorDatabase(_root);
        var col = db.CreateCollection<int>("c", 2, Metric.L2);
        col.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));

        Assert.False(col.TryGet(99, out var record));
        Assert.Null(record);
    }

    [Fact]
    public void TryGet_RoundTripsAfterFlushCompactAndReopen()
    {
        // 写入并 Flush 到 Segment，然后 Compact 与 Dispose
        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("c", 3, Metric.L2);
            for (int i = 0; i < 8; i++)
            {
                col.Insert(new VectorRecord<int>(i, new float[] { i, i + 1, i + 2 }));
                col.SetPayload(i, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["idx"] = i,
                });
            }
            db.Flush();
            col.Compact();
        }

        // 重新打开后 TryGet 仍然能拿回向量和 payload
        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("c");
            for (int i = 0; i < 8; i++)
            {
                Assert.True(col.TryGet(i, out var record), $"TryGet({i}) 应返回 true");
                Assert.NotNull(record);
                Assert.Equal(i, record!.Key);
                Assert.Equal(new float[] { i, i + 1, i + 2 }, record.Vector);
                Assert.NotNull(record.Payload);
                Assert.Equal((long)i, Convert.ToInt64(record.Payload!["idx"]));
            }
        }
    }

    [Fact]
    public void GetMany_ReturnsRequestedRecords()
    {
        using var db = new VectorDatabase(_root);
        var col = db.CreateCollection<int>("c", 2, Metric.L2);
        col.Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));
        col.Insert(new VectorRecord<int>(2, new float[] { 0f, 1f }));
        col.Insert(new VectorRecord<int>(3, new float[] { 1f, 1f }));

        var keys = new[] { 1, 3, 99 };
        var records = col.GetMany(keys.AsSpan(), includeVectors: true);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Key == 1 && r.Vector.SequenceEqual(new float[] { 1f, 0f }));
        Assert.Contains(records, r => r.Key == 3 && r.Vector.SequenceEqual(new float[] { 1f, 1f }));
    }
}
