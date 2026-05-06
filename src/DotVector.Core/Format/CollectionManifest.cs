using System.Runtime.InteropServices;

namespace DotVector.Format;

/// <summary>
/// 单个集合的清单（manifest.bin）头部数据。
/// 存储于 <c>collections/{id:N}/manifest.bin</c>，字节序 little-endian。
/// </summary>
/// <remarks>
/// 修改此结构体布局时必须同步升级 <see cref="Version"/> 并更新 CHANGELOG。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CollectionManifest
{
    /// <summary>Magic 标识符，固定值 "DVCOLMFT"（8 字节 ASCII）。</summary>
    public Magic8 Magic;

    /// <summary>清单格式版本号，当前为 1。</summary>
    public uint Version;

    /// <summary>下一个 Segment 的分配序列号（从 1 开始）。</summary>
    public ulong NextSegmentSequence;

    /// <summary>
    /// 已被 Segment 完整覆盖的最大 WAL 序列号；
    /// 即 WAL 序列号 ≤ 此值的所有记录都已被 flush 到 Segment 中，
    /// 可安全裁剪。0 表示尚未发生过 flush。
    /// </summary>
    public ulong LastCoveredWalSequence;
}
