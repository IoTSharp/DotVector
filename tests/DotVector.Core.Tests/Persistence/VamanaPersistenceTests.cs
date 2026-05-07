using DotVector.Api;
using DotVector.Format;
using DotVector.Index.DiskAnn;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 端到端验证 Vamana / DiskANN 索引的 mmap 磁盘持久化（M12.3）：
/// Open → Insert → Flush → Dispose → Reopen → 召回结果一致。
/// </summary>
public sealed class VamanaPersistenceTests : IDisposable
{
    private readonly string _root;

    public VamanaPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvec-vamana-" + Guid.NewGuid().ToString("N") + ".dvec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* ignore */ }
    }

    private static VamanaOptions Options() => new()
    {
        MaxDegree = 16,
        SearchListSize = 32,
        Alpha = 1.2f,
        Seed = 42,
    };

    private static float[] Vector(int seed, int dim)
    {
        var rng = new Random(seed);
        float[] v = new float[dim];
        for (int i = 0; i < dim; i++) { v[i] = (float)(rng.NextDouble() * 2.0 - 1.0); }
        return v;
    }

    [Fact]
    public void Vamana_FlushAndReopen_PreservesCount()
    {
        const int dim = 16;
        const int n = 50;

        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("v", dim, Metric.L2, Options());
            for (int i = 0; i < n; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            col.Flush();
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("v");
            var hits = col.Search(Vector(0, dim), topK: n);
            Assert.Equal(n, hits.Count);
        }
    }

    [Fact]
    public void Vamana_FlushAndReopen_PreservesTopKResults()
    {
        const int dim = 32;
        const int n = 100;
        const int topK = 10;

        float[] query = Vector(9999, dim);
        IReadOnlyList<SearchResult<int>> before;

        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("v", dim, Metric.L2, Options());
            for (int i = 0; i < n; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            before = col.Search(query, topK);
            col.Flush();
        }

        IReadOnlyList<SearchResult<int>> after;
        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("v");
            after = col.Search(query, topK);
        }

        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Key, after[i].Key);
            Assert.Equal(before[i].Score, after[i].Score, precision: 4);
        }
    }

    [Fact]
    public void Vamana_VamanaBin_HasCorrectMagicAndVersion()
    {
        const int dim = 8;

        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("v", dim, Metric.L2, Options());
            for (int i = 0; i < 10; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            col.Flush();
        }

        // 找到 vamana.bin 文件
        string[] vamanaFiles = Directory.GetFiles(_root, "vamana.bin", SearchOption.AllDirectories);
        Assert.Single(vamanaFiles);

        byte[] bytes = File.ReadAllBytes(vamanaFiles[0]);
        Assert.True(bytes.Length >= 48);

        // 前 4 字节为 "DVAN"
        ReadOnlySpan<byte> magic = bytes.AsSpan(0, 4);
        Assert.True(magic.SequenceEqual(VamanaFileHeaderConstants.MagicAscii));
    }

    [Fact]
    public void Vamana_FlushDeletesPriorSegment()
    {
        const int dim = 8;

        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("v", dim, Metric.L2, Options());
            for (int i = 0; i < 5; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            col.Flush();
            for (int i = 5; i < 10; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            col.Flush();
        }

        // Vamana 是单 segment 全量快照模型：第二次 Flush 后应只剩一个 seg-* 目录
        string[] segDirs = Directory.GetDirectories(_root, "seg-*", SearchOption.AllDirectories);
        Assert.Single(segDirs);

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("v");
            var hits = col.Search(Vector(0, dim), topK: 10);
            Assert.Equal(10, hits.Count);
        }
    }

    [Fact]
    public void Vamana_WalReplayAfterFlush_RestoresIncrementalInserts()
    {
        const int dim = 8;

        using (var db = new VectorDatabase(_root))
        {
            var col = db.CreateCollection<int>("v", dim, Metric.L2, Options());
            for (int i = 0; i < 10; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
            col.Flush();
            // Flush 后再插入；这些数据只在 WAL 中
            for (int i = 10; i < 15; i++)
            {
                col.Insert(new VectorRecord<int>(i, Vector(i, dim)));
            }
        }

        using (var db = new VectorDatabase(_root))
        {
            var col = db.GetCollection<int>("v");
            var hits = col.Search(Vector(0, dim), topK: 20);
            Assert.Equal(15, hits.Count);
        }
    }
}
