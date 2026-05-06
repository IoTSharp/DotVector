using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using DotVector.Exceptions;
using DotVector.Format;
using DotVector.IO;
using DotVector.Model;

namespace DotVector.Catalog;

/// <summary>
/// 集合在 catalog.bin 中的完整描述，包含名称、键类型、索引算法等信息。
/// </summary>
internal sealed class CatalogEntry
{
    /// <summary>集合的稳定 GUID，决定其在磁盘上的目录名。</summary>
    public required Guid CollectionId { get; init; }

    /// <summary>集合名（用户可见的逻辑名）。</summary>
    public required string Name { get; init; }

    /// <summary>向量维度。</summary>
    public required int Dimensions { get; init; }

    /// <summary>键类型代码。</summary>
    public required KeyTypeCode KeyType { get; init; }

    /// <summary>索引算法类型。</summary>
    public required IndexKind IndexKind { get; init; }

    /// <summary>距离度量。</summary>
    public required Metric Metric { get; init; }
}

/// <summary>
/// catalog.bin 的读写工具：
/// <para>
/// 文件布局：<c>FileHeader (36 字节) + u32 集合数 + (CollectionHeader + name UTF-8 字节)*</c>
/// </para>
/// 写入采用 <c>catalog.bin.tmp</c> + <see cref="File.Move(string, string, bool)"/> 原子替换。
/// </summary>
internal static class CatalogStore
{
    /// <summary>当前 catalog 格式版本号。</summary>
    public const uint CurrentVersion = 1;

    /// <summary>Magic 标识符："DOTVEC\0\0"。</summary>
    public static ReadOnlySpan<byte> MagicBytes => "DOTVEC\0\0"u8;

    /// <summary>
    /// 将集合元数据原子地写入 catalog.bin。
    /// </summary>
    public static void Write(string catalogPath, IReadOnlyList<CatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(catalogPath);
        ArgumentNullException.ThrowIfNull(entries);

        // 计算总字节数
        int headerSize = Unsafe.SizeOf<FileHeader>();
        int collectionHeaderSize = Unsafe.SizeOf<CollectionHeader>();
        int total = headerSize + sizeof(uint);
        byte[][] nameBytes = new byte[entries.Count][];
        for (int i = 0; i < entries.Count; i++)
        {
            CatalogEntry e = entries[i];
            byte[] nb = Encoding.UTF8.GetBytes(e.Name);
            if (nb.Length > byte.MaxValue)
            {
                throw new DotVectorException($"集合名 '{e.Name}' 过长：{nb.Length} 字节（最长 255）。");
            }
            nameBytes[i] = nb;
            total += collectionHeaderSize + nb.Length;
        }

        byte[] buffer = new byte[total];
        Span<byte> span = buffer;

        // FileHeader
        FileHeader fh = default;
        MagicBytes.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.As<Magic8, byte>(ref fh.Magic), 8));
        fh.Version = CurrentVersion;
        fh.Dim = 0;
        fh.DefaultMetric = 0;
        MemoryMarshal.Write(span, in fh);
        int offset = headerSize;

        // 集合数
        SpanWriter writer = new(span[offset..]);
        writer.WriteUInt32((uint)entries.Count);
        offset += writer.Written;

        // 各集合头部 + 名称
        for (int i = 0; i < entries.Count; i++)
        {
            CatalogEntry e = entries[i];
            CollectionHeader ch = default;
            ch.CollectionId = e.CollectionId;
            ch.Dimensions = (uint)e.Dimensions;
            ch.KeyTypeCode = (uint)e.KeyType;
            ch.IndexKind = (uint)e.IndexKind;
            ch.Metric = (byte)e.Metric;
            ch.NameLength = (byte)nameBytes[i].Length;
            MemoryMarshal.Write(span[offset..], in ch);
            offset += collectionHeaderSize;
            nameBytes[i].CopyTo(span[offset..]);
            offset += nameBytes[i].Length;
        }

        // 原子写入：tmp + Move
        string tmpPath = catalogPath + ".tmp";
        // 确保目录存在
        string? dir = Path.GetDirectoryName(catalogPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        using (FileStream fs = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(buffer, 0, buffer.Length);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmpPath, catalogPath, overwrite: true);
    }

    /// <summary>
    /// 从 catalog.bin 读取集合元数据。文件不存在时返回空列表。
    /// </summary>
    public static IReadOnlyList<CatalogEntry> Read(string catalogPath)
    {
        ArgumentNullException.ThrowIfNull(catalogPath);
        if (!File.Exists(catalogPath))
        {
            return Array.Empty<CatalogEntry>();
        }

        byte[] buffer = File.ReadAllBytes(catalogPath);
        ReadOnlySpan<byte> span = buffer;

        int headerSize = Unsafe.SizeOf<FileHeader>();
        if (span.Length < headerSize + sizeof(uint))
        {
            throw new DotVectorException($"catalog.bin 损坏：长度 {span.Length} 小于头部最小尺寸。");
        }

        FileHeader fh = MemoryMarshal.Read<FileHeader>(span);
        ReadOnlySpan<byte> magic = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Magic8, byte>(ref fh.Magic), 8);
        if (!magic.SequenceEqual(MagicBytes))
        {
            throw new DotVectorException("catalog.bin 损坏：Magic 不匹配。");
        }
        if (fh.Version != CurrentVersion)
        {
            throw new DotVectorException(
                $"不支持的 catalog 格式版本：{fh.Version}（期望 {CurrentVersion}）。");
        }

        int offset = headerSize;
        SpanReader reader = new(span[offset..]);
        uint count = reader.ReadUInt32();
        offset += sizeof(uint);

        int collectionHeaderSize = Unsafe.SizeOf<CollectionHeader>();
        List<CatalogEntry> result = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (span.Length < offset + collectionHeaderSize)
            {
                throw new DotVectorException("catalog.bin 损坏：集合头部超出文件范围。");
            }
            CollectionHeader ch = MemoryMarshal.Read<CollectionHeader>(span[offset..]);
            offset += collectionHeaderSize;
            if (span.Length < offset + ch.NameLength)
            {
                throw new DotVectorException("catalog.bin 损坏：集合名超出文件范围。");
            }
            string name = Encoding.UTF8.GetString(span.Slice(offset, ch.NameLength));
            offset += ch.NameLength;

            result.Add(new CatalogEntry
            {
                CollectionId = ch.CollectionId,
                Name = name,
                Dimensions = (int)ch.Dimensions,
                KeyType = (KeyTypeCode)ch.KeyTypeCode,
                IndexKind = (IndexKind)ch.IndexKind,
                Metric = (Metric)ch.Metric,
            });
        }

        return result;
    }
}
