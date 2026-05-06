using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotVector.Format;

/// <summary>
/// DotVector 数据库目录（.dvec/）的根元数据头部。
/// 存储于 catalog.bin 文件开头，字节序 little-endian。
/// </summary>
/// <remarks>
/// Magic = "DOTVEC\0\0"（8 字节），Version = 1。
/// 修改此结构体布局时必须同步升级 <see cref="Version"/> 并更新 CHANGELOG。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FileHeader
{
    /// <summary>Magic 标识符，固定值 "DOTVEC\0\0"（8 字节 ASCII）。</summary>
    public Magic8 Magic;

    /// <summary>格式版本号，当前为 1。布局变更时必须递增。</summary>
    public uint Version;

    /// <summary>默认向量维度（单集合数据库时使用；多集合时各集合自行记录）。</summary>
    public uint Dim;

    /// <summary>默认距离度量类型，参见 <see cref="Model.Metric"/>。</summary>
    public byte DefaultMetric;

    /// <summary>保留字段，供未来扩展，必须填 0。</summary>
    public Reserved19 Reserved;
}

/// <summary>
/// 8 字节 inline 缓冲，用于存储 magic 标识符。
/// </summary>
/// <remarks>
/// TODO(M5): 使用 BinaryPrimitives 验证 magic 值。
/// </remarks>
[InlineArray(8)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Magic8
{
    private byte _e0;
}

/// <summary>
/// 19 字节保留缓冲，供 <see cref="FileHeader"/> 对齐与未来扩展。
/// </summary>
[InlineArray(19)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reserved19
{
    private byte _e0;
}

/// <summary>
/// 单个不可变 Segment 的头部元数据。
/// 存储于每个 segments/seg-{seq}/seg.hdr 文件。
/// 字节序 little-endian。
/// </summary>
/// <remarks>
/// TODO(M5): 实现 SegmentWriter / SegmentReader 时填充此结构体。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SegmentHeader
{
    /// <summary>Segment 序列号（单调递增）。</summary>
    public ulong SequenceNumber;

    /// <summary>本 Segment 存储的向量条数。</summary>
    public uint VectorCount;

    /// <summary>向量维度。</summary>
    public uint Dimensions;

    /// <summary>距离度量类型。</summary>
    public byte Metric;

    /// <summary>创建时间戳（Unix 秒，UTC）。</summary>
    public long CreatedAtUtcUnixSeconds;

    /// <summary>保留字段。</summary>
    public Reserved19 Reserved;
}
