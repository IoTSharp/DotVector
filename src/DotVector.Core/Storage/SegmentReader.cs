using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotVector.Exceptions;
using DotVector.Format;
using DotVector.IO;

namespace DotVector.Storage;

/// <summary>
/// 不可变 Segment 的读取器。负责：
/// <list type="bullet">
///   <item>解析 <c>seg.hdr</c>。</item>
///   <item>使用 <see cref="MemoryMappedFile"/> 把 <c>vectors.bin</c> 映射到内存，
///   并通过 <see cref="MemoryMappedViewAccessor.SafeMemoryMappedViewHandle"/> +
///   <see cref="System.Runtime.InteropServices.MemoryMarshal"/> 在不使用 <c>unsafe</c>
///   的前提下零拷贝读取向量数据。</item>
///   <item>解析 <c>keys.bin</c> 并加载键序列。</item>
/// </list>
/// </summary>
internal sealed class SegmentReader<TKey> : IDisposable where TKey : notnull
{
    private readonly MemoryMappedFile _vectorsMmf;
    private readonly MemoryMappedViewAccessor _vectorsAccessor;
    private readonly long _vectorBytes;

    /// <summary>Segment 头部信息。</summary>
    public SegmentHeader Header { get; }

    /// <summary>键序列（与向量行一一对应）。</summary>
    public IReadOnlyList<TKey> Keys { get; }

    /// <summary>每行对应的已编码 payload 字节序列；元素为 <see langword="null"/> 表示该行无 payload。
    /// 当 Segment 不含 <c>payload.bin</c> 时为 <see langword="null"/>。</summary>
    public IReadOnlyList<byte[]?>? EncodedPayloads { get; }

    private SegmentReader(
        SegmentHeader header,
        IReadOnlyList<TKey> keys,
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor accessor,
        long vectorBytes,
        IReadOnlyList<byte[]?>? encodedPayloads)
    {
        Header = header;
        Keys = keys;
        _vectorsMmf = mmf;
        _vectorsAccessor = accessor;
        _vectorBytes = vectorBytes;
        EncodedPayloads = encodedPayloads;
    }

    /// <summary>打开指定 Segment 目录。</summary>
    public static SegmentReader<TKey> Open(string segmentDirectory)
    {
        ArgumentNullException.ThrowIfNull(segmentDirectory);
        if (!Directory.Exists(segmentDirectory))
        {
            throw new DotVectorException($"Segment 目录不存在：{segmentDirectory}");
        }

        // 头部
        int headerSize = Unsafe.SizeOf<SegmentHeader>();
        byte[] headerBuf = File.ReadAllBytes(Path.Combine(segmentDirectory, "seg.hdr"));
        if (headerBuf.Length < headerSize)
        {
            throw new DotVectorException($"seg.hdr 损坏：长度 {headerBuf.Length} < {headerSize}。");
        }
        SegmentHeader header = MemoryMarshal.Read<SegmentHeader>(headerBuf);

        // 键
        byte[] keysBuf = File.ReadAllBytes(Path.Combine(segmentDirectory, "keys.bin"));
        List<TKey> keys = new((int)header.VectorCount);
        SpanReader keyReader = new(keysBuf);
        for (uint i = 0; i < header.VectorCount; i++)
        {
            keys.Add(KeyCodec.Read<TKey>(ref keyReader));
        }

        // 向量 mmap
        string vectorsPath = Path.Combine(segmentDirectory, "vectors.bin");
        FileInfo fi = new(vectorsPath);
        long expected = (long)header.VectorCount * header.Dimensions * sizeof(float);
        if (fi.Length != expected)
        {
            throw new DotVectorException(
                $"vectors.bin 长度 {fi.Length} 与预期 {expected} 不一致。");
        }

        // payload.bin（M11，可选）
        IReadOnlyList<byte[]?>? encodedPayloads = null;
        string payloadPath = Path.Combine(segmentDirectory, "payload.bin");
        if (File.Exists(payloadPath))
        {
            byte[] payloadBuf = File.ReadAllBytes(payloadPath);
            SpanReader pr = new(payloadBuf);
            uint count = pr.ReadUInt32();
            if (count != header.VectorCount)
            {
                throw new DotVectorException(
                    $"payload.bin 行数 {count} 与 VectorCount {header.VectorCount} 不一致。");
            }
            byte[]?[] arr = new byte[]?[count];
            for (int i = 0; i < count; i++)
            {
                uint len = pr.ReadUInt32();
                if (len == 0)
                {
                    arr[i] = null;
                }
                else
                {
                    arr[i] = pr.ReadBytes((int)len).ToArray();
                }
            }
            encodedPayloads = arr;
        }

        MemoryMappedFile mmf;
        MemoryMappedViewAccessor accessor;
        if (expected == 0)
        {
            // 空 Segment：跳过 mmap
            return new SegmentReader<TKey>(header, keys, null!, null!, 0, encodedPayloads);
        }

        mmf = MemoryMappedFile.CreateFromFile(
            vectorsPath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            access: MemoryMappedFileAccess.Read);
        accessor = mmf.CreateViewAccessor(0, expected, MemoryMappedFileAccess.Read);

        return new SegmentReader<TKey>(header, keys, mmf, accessor, expected, encodedPayloads);
    }

    /// <summary>读取指定行的向量到目标 span（长度须等于 Dimensions）。</summary>
    public void ReadVector(int row, Span<float> destination)
    {
        if (row < 0 || row >= Header.VectorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
        if (destination.Length != Header.Dimensions)
        {
            throw new DotVectorException(
                $"目标缓冲区长度 {destination.Length} 与维度 {Header.Dimensions} 不一致。");
        }

        long offset = (long)row * Header.Dimensions * sizeof(float);
        for (int i = 0; i < Header.Dimensions; i++)
        {
            destination[i] = _vectorsAccessor.ReadSingle(offset + i * sizeof(float));
        }
    }

    /// <summary>把整个 Segment 的向量数据复制到一个连续 float 数组（用于回放重建索引）。</summary>
    public float[] ReadAllVectors()
    {
        int total = (int)((long)Header.VectorCount * Header.Dimensions);
        float[] result = new float[total];
        if (total == 0) return result;
        // accessor 不支持 Span，因此循环复制。M5+ 可改造为基于 SafeBuffer.ReadArray 的批量复制。
        _vectorsAccessor.ReadArray(0, result, 0, total);
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _vectorsAccessor?.Dispose();
        _vectorsMmf?.Dispose();
    }
}
