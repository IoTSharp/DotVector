using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotVector.Format;

/// <summary>
/// 集合（Collection）的固定二进制头部，存储于 <c>catalog.bin</c> 中。
/// 其后紧跟长度为 <see cref="NameLength"/> 的 UTF-8 名称字节。
/// 字节序 little-endian。固定大小 64 字节。
/// </summary>
/// <remarks>
/// 修改此结构体布局必须同步升级 <see cref="FileHeader.Version"/> 并更新 CHANGELOG。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CollectionHeader
{
    /// <summary>集合的全局唯一标识，同时作为目录名（<c>collections/{guid}/</c>）。</summary>
    public Guid CollectionId;

    /// <summary>向量维度。</summary>
    public uint Dimensions;

    /// <summary>键类型代码，参见 <see cref="IO.KeyTypeCode"/>。</summary>
    public uint KeyTypeCode;

    /// <summary>索引算法类型，参见 <see cref="Model.IndexKind"/>。</summary>
    public uint IndexKind;

    /// <summary>距离度量类型，参见 <see cref="Model.Metric"/>。</summary>
    public byte Metric;

    /// <summary>名称长度（UTF-8 字节数，最长 255）。</summary>
    public byte NameLength;

    /// <summary>保留字段（u16）。</summary>
    public ushort Reserved0;

    /// <summary>保留字段（u32）。</summary>
    public uint Reserved1;

    /// <summary>保留字段（28 字节）。</summary>
    public Reserved28 Reserved2;
}

/// <summary>
/// 28 字节保留缓冲，供 <see cref="CollectionHeader"/> 对齐与未来扩展。
/// </summary>
[InlineArray(28)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reserved28
{
    private byte _e0;
}
