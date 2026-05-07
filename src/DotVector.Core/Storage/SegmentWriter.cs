using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotVector.Compression;
using DotVector.Exceptions;
using DotVector.Format;
using DotVector.IO;
using DotVector.Model;

namespace DotVector.Storage;

/// <summary>
/// 不可变 Segment 的写入器。每个 Segment 对应一个目录
/// <c>segments/seg-{seq:D6}/</c>，包含：
/// <list type="bullet">
///   <item><c>seg.hdr</c>：<see cref="SegmentHeader"/> 二进制。</item>
///   <item><c>vectors.bin</c>：行优先 float32 向量数据。</item>
///   <item><c>keys.bin</c>：按 <see cref="KeyTypeCode"/> 序列化的键序列。</item>
/// </list>
/// 整个目录通过先写入 <c>{name}.tmp</c> 再 <see cref="Directory.Move"/>
/// 原子重命名，保证读端不会看到半写状态。
/// </summary>
internal static class SegmentWriter
{
    /// <summary>
    /// 将一组向量与键写入指定 Segment 目录（原子）。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <param name="segmentDirectory">目标 Segment 目录路径。</param>
    /// <param name="header">Segment 头部信息（dimensions/metric/sequence/createdAt 等）。</param>
    /// <param name="keys">键序列，长度须与 <paramref name="header"/>.VectorCount 一致。</param>
    /// <param name="vectors">行优先 float32 向量数据，长度须为 <c>VectorCount * Dimensions</c>。</param>
    public static void Write<TKey>(
        string segmentDirectory,
        SegmentHeader header,
        IReadOnlyList<TKey> keys,
        ReadOnlySpan<float> vectors) where TKey : notnull
        => Write(segmentDirectory, header, keys, vectors, payloads: null);

    /// <summary>
    /// 将一组向量、键与可选 payload 写入指定 Segment 目录（原子，M11）。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <param name="segmentDirectory">目标 Segment 目录路径。</param>
    /// <param name="header">Segment 头部信息。</param>
    /// <param name="keys">键序列，长度须与 <paramref name="header"/>.VectorCount 一致。</param>
    /// <param name="vectors">行优先 float32 向量数据。</param>
    /// <param name="payloads">每行对应的已编码 payload 字节序列；元素为 <see langword="null"/> 表示该行无 payload。
    /// 当本参数本身为 <see langword="null"/> 或所有元素均为 <see langword="null"/> 时，
    /// 不写出 <c>payload.bin</c>。</param>
    public static void Write<TKey>(
        string segmentDirectory,
        SegmentHeader header,
        IReadOnlyList<TKey> keys,
        ReadOnlySpan<float> vectors,
        IReadOnlyList<byte[]?>? payloads) where TKey : notnull
        => Write(segmentDirectory, header, keys, vectors, payloads, quantizer: null);

    /// <summary>
    /// 将一组向量、键、可选 payload 与可选量化器写入指定 Segment 目录（原子，M13.5b）。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <param name="segmentDirectory">目标 Segment 目录路径。</param>
    /// <param name="header">Segment 头部信息。</param>
    /// <param name="keys">键序列。</param>
    /// <param name="vectors">行优先 float32 向量数据。</param>
    /// <param name="payloads">每行已编码 payload，可空。</param>
    /// <param name="quantizer">已训练的量化器；为 <see langword="null"/> 时不写出 <c>quantizer.bin</c>，
    /// 读端凭文件存在与否区分新旧 Segment（向后兼容，未升级 FileHeader.Version）。</param>
    public static void Write<TKey>(
        string segmentDirectory,
        SegmentHeader header,
        IReadOnlyList<TKey> keys,
        ReadOnlySpan<float> vectors,
        IReadOnlyList<byte[]?>? payloads,
        IVectorQuantizer? quantizer) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(segmentDirectory);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count != header.VectorCount)
        {
            throw new DotVectorException(
                $"键数量 {keys.Count} 与头部 VectorCount {header.VectorCount} 不一致。");
        }
        long expectedFloats = (long)header.VectorCount * header.Dimensions;
        if (vectors.Length != expectedFloats)
        {
            throw new DotVectorException(
                $"向量字节数 {vectors.Length} 不等于预期 {expectedFloats}。");
        }
        if (payloads is not null && payloads.Count != keys.Count)
        {
            throw new DotVectorException(
                $"payloads 长度 {payloads.Count} 与 keys 长度 {keys.Count} 不一致。");
        }

        string parent = Path.GetDirectoryName(segmentDirectory)
            ?? throw new DotVectorException($"无法确定父目录：{segmentDirectory}");
        Directory.CreateDirectory(parent);

        string tmpDir = segmentDirectory + ".tmp";
        if (Directory.Exists(tmpDir))
        {
            Directory.Delete(tmpDir, recursive: true);
        }
        Directory.CreateDirectory(tmpDir);

        // seg.hdr
        int headerSize = Unsafe.SizeOf<SegmentHeader>();
        byte[] headerBuf = new byte[headerSize];
        MemoryMarshal.Write<SegmentHeader>(headerBuf, in header);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg.hdr"), headerBuf);

        // vectors.bin
        using (FileStream fs = new(Path.Combine(tmpDir, "vectors.bin"),
            FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(vectors);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }

        // keys.bin
        using (FileStream fs = new(Path.Combine(tmpDir, "keys.bin"),
            FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // 先估算并分配缓冲区
            int total = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                total += KeyCodec.ComputeSize(keys[i]);
            }
            byte[] buf = new byte[total];
            SpanWriter w = new(buf);
            for (int i = 0; i < keys.Count; i++)
            {
                KeyCodec.Write(ref w, keys[i]);
            }
            fs.Write(buf, 0, w.Written);
            fs.Flush(flushToDisk: true);
        }

        // payload.bin（M11，可选）
        bool hasAnyPayload = false;
        if (payloads is not null)
        {
            for (int i = 0; i < payloads.Count; i++)
            {
                if (payloads[i] is { Length: > 0 })
                {
                    hasAnyPayload = true;
                    break;
                }
            }
        }
        if (hasAnyPayload)
        {
            // 格式：u32 count + repeated{ u32 len + bytes(len) }；len=0 表示该行无 payload。
            int total = sizeof(uint);
            for (int i = 0; i < payloads!.Count; i++)
            {
                total += sizeof(uint);
                byte[]? p = payloads[i];
                if (p is not null) { total += p.Length; }
            }
            byte[] payloadBuf = new byte[total];
            SpanWriter pw = new(payloadBuf);
            pw.WriteUInt32((uint)payloads.Count);
            for (int i = 0; i < payloads.Count; i++)
            {
                byte[]? p = payloads[i];
                int len = p?.Length ?? 0;
                pw.WriteUInt32((uint)len);
                if (len > 0) { pw.WriteBytes(p!); }
            }
            using FileStream fs = new(Path.Combine(tmpDir, "payload.bin"),
                FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(payloadBuf, 0, pw.Written);
            fs.Flush(flushToDisk: true);
        }

        // quantizer.bin（M13.5b，可选）
        if (quantizer is not null)
        {
            using FileStream fs = new(Path.Combine(tmpDir, "quantizer.bin"),
                FileMode.Create, FileAccess.Write, FileShare.None);
            QuantizerSerializer.Write(quantizer, fs);
            fs.Flush(flushToDisk: true);
        }

        // 原子重命名
        if (Directory.Exists(segmentDirectory))
        {
            Directory.Delete(segmentDirectory, recursive: true);
        }
        Directory.Move(tmpDir, segmentDirectory);
    }
}
