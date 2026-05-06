using System.Runtime.InteropServices;
using DotVector.Compute;
using DotVector.Format;
using DotVector.Model;

namespace DotVector.Core.Tests;

/// <summary>
/// DotVector.Core 单元 smoke 测试。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证测试框架正常运行。
    /// </summary>
    [Fact]
    public void Smoke_AlwaysTrue()
    {
        Assert.True(true);
    }

    /// <summary>
    /// 验证 <see cref="FileHeader"/> 的 round-trip 序列化/反序列化。
    /// </summary>
    [Fact]
    public void FileHeader_RoundTrip_PreservesAllFields()
    {
        var header = new FileHeader
        {
            Version = 1,
            Dim = 384,
            DefaultMetric = (byte)Metric.Cosine,
        };

        // 写入
        Span<byte> buffer = stackalloc byte[Marshal.SizeOf<FileHeader>()];
        MemoryMarshal.Write(buffer, in header);

        // 读回
        var readBack = MemoryMarshal.Read<FileHeader>(buffer);

        Assert.Equal(header.Version, readBack.Version);
        Assert.Equal(header.Dim, readBack.Dim);
        Assert.Equal(header.DefaultMetric, readBack.DefaultMetric);
    }

    /// <summary>
    /// 验证 L2 距离的基本正确性：相同向量距离为 0。
    /// </summary>
    [Fact]
    public void Distance_L2Squared_SameVector_IsZero()
    {
        float[] v = [1f, 2f, 3f, 4f];
        float dist = Distance.L2Squared(v, v);
        Assert.Equal(0f, dist);
    }

    /// <summary>
    /// 验证 L2 距离：已知向量对的正确计算。
    /// </summary>
    [Fact]
    public void Distance_L2Squared_KnownVectors_IsCorrect()
    {
        // [1,0,0] 和 [0,1,0] 的 L2^2 = 1^2 + 1^2 = 2
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        float dist = Distance.L2Squared(a, b);
        Assert.Equal(2f, dist, precision: 5);
    }

    /// <summary>
    /// 验证余弦距离：正交向量余弦距离为 1。
    /// </summary>
    [Fact]
    public void Distance_Cosine_OrthogonalVectors_IsOne()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        float dist = Distance.Cosine(a, b);
        Assert.Equal(1f, dist, precision: 5);
    }

    /// <summary>
    /// 验证余弦距离：相同向量余弦距离为 0。
    /// </summary>
    [Fact]
    public void Distance_Cosine_SameVector_IsZero()
    {
        float[] v = [1f, 2f, 3f];
        float dist = Distance.Cosine(v, v);
        Assert.Equal(0f, dist, precision: 5);
    }

    /// <summary>
    /// 验证维度不匹配时 Distance 抛出正确异常。
    /// </summary>
    [Fact]
    public void Distance_MismatchedDimensions_ThrowsArgumentException()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];
        Assert.Throws<ArgumentException>(() => Distance.L2Squared(a, b));
    }

    /// <summary>
    /// 验证 <see cref="VectorRecord{TKey}"/> 构造后属性正确。
    /// </summary>
    [Fact]
    public void VectorRecord_Constructor_SetsProperties()
    {
        float[] vec = [1f, 2f, 3f];
        var record = new VectorRecord<string>("key-1", vec);

        Assert.Equal("key-1", record.Key);
        Assert.Same(vec, record.Vector);
        Assert.Null(record.Payload);
    }
}
