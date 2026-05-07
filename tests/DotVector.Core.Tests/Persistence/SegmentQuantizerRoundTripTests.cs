using DotVector.Compression;
using DotVector.Format;
using DotVector.Storage;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 M13.5b：<see cref="SegmentWriter"/> 写入时把可选 <see cref="IVectorQuantizer"/> 序列化到
/// <c>quantizer.bin</c>，<see cref="SegmentReader{TKey}"/> 打开时按文件存在性反序列化恢复。
/// 不写量化器时不应生成 <c>quantizer.bin</c>，重读后 <see cref="SegmentReader{TKey}.Quantizer"/> 为 null。
/// </summary>
public sealed class SegmentQuantizerRoundTripTests : IDisposable
{
    private readonly string _dir;

    public SegmentQuantizerRoundTripTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dotvec-quant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void Write_WithoutQuantizer_DoesNotEmitQuantizerFile()
    {
        string segDir = Path.Combine(_dir, "seg-000001");
        var (hdr, keys, vecs) = MakeSegment(8, 16);

        SegmentWriter.Write(segDir, hdr, keys, vecs);

        Assert.False(File.Exists(Path.Combine(segDir, "quantizer.bin")));

        using SegmentReader<int> reader = SegmentReader<int>.Open(segDir);
        Assert.Null(reader.Quantizer);
    }

    [Fact]
    public void Write_WithSq8Quantizer_RoundTripsCodeBytesAndScores()
    {
        const int n = 16;
        const int dim = 8;
        string segDir = Path.Combine(_dir, "seg-000002");
        var (hdr, keys, vecs) = MakeSegment(dim, n);

        var sq = new ScalarQuantizer8(dim);
        sq.Train(vecs, n);

        SegmentWriter.Write(segDir, hdr, keys, vecs, payloads: null, quantizer: sq);

        Assert.True(File.Exists(Path.Combine(segDir, "quantizer.bin")));

        using SegmentReader<int> reader = SegmentReader<int>.Open(segDir);
        IVectorQuantizer? loaded = reader.Quantizer;
        Assert.NotNull(loaded);
        Assert.Equal(QuantizerKind.Sq8, loaded!.Kind);
        Assert.Equal(dim, loaded.Dimensions);
        Assert.Equal(dim, loaded.CodeBytes);
        Assert.True(loaded.IsTrained);

        // BuildScorer 在 round-trip 前后对相同 query/code 应给出一致打分。
        Span<float> query = stackalloc float[dim];
        for (int d = 0; d < dim; d++) { query[d] = 0.25f * d - 0.5f; }

        Span<byte> code0 = stackalloc byte[dim];
        Span<byte> code1 = stackalloc byte[dim];
        sq.Encode(vecs.AsSpan(0, dim), code0);
        loaded.Encode(vecs.AsSpan(0, dim), code1);
        Assert.True(code0.SequenceEqual(code1));

        IQuantizedScorer s0 = sq.BuildScorer(query);
        IQuantizedScorer s1 = loaded.BuildScorer(query);
        Assert.Equal(s0.Score(code0), s1.Score(code1), precision: 5);
    }

    private static (SegmentHeader Hdr, List<int> Keys, float[] Vectors) MakeSegment(int dim, int n)
    {
        var keys = new List<int>(n);
        float[] vecs = new float[n * dim];
        for (int i = 0; i < n; i++)
        {
            keys.Add(i + 1);
            for (int d = 0; d < dim; d++)
            {
                vecs[i * dim + d] = (i + 1) * 0.125f + d * 0.0625f;
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
        return (hdr, keys, vecs);
    }
}
