using DotVector.Format;
using DotVector.IO;
using DotVector.Storage;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="SegmentWriter"/> 写入与 <see cref="SegmentReader{TKey}"/>
/// 通过 <see cref="System.IO.MemoryMappedFiles.MemoryMappedFile"/> 读回的 round-trip（M10）。
/// </summary>
public sealed class MmapSegmentReaderTests : IDisposable
{
    private readonly string _dir;

    public MmapSegmentReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dotvec-mmap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void RoundTrip_PreservesKeysAndVectors()
    {
        const int n = 17;
        const int dim = 8;
        string segDir = Path.Combine(_dir, "seg-000001");

        var keys = new List<int>(n);
        float[] vecs = new float[n * dim];
        for (int i = 0; i < n; i++)
        {
            keys.Add(i * 7 + 1);
            for (int d = 0; d < dim; d++)
            {
                vecs[i * dim + d] = (i + 1) * 0.5f + d;
            }
        }

        var hdr = new SegmentHeader
        {
            SequenceNumber = 1,
            VectorCount = (uint)n,
            Dimensions = (uint)dim,
            Metric = (byte)Model.Metric.L2,
            CreatedAtUtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        SegmentWriter.Write(segDir, hdr, keys, vecs);

        using SegmentReader<int> reader = SegmentReader<int>.Open(segDir);
        Assert.Equal((uint)n, reader.Header.VectorCount);
        Assert.Equal((uint)dim, reader.Header.Dimensions);
        Assert.Equal(n, reader.Keys.Count);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(keys[i], reader.Keys[i]);
        }

        float[] read = reader.ReadAllVectors();
        Assert.Equal(vecs.Length, read.Length);
        for (int i = 0; i < vecs.Length; i++)
        {
            Assert.Equal(vecs[i], read[i]);
        }
    }
}
