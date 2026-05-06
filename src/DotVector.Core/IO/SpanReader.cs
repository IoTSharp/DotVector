using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DotVector.Exceptions;
using DotVector.Model;

namespace DotVector.IO;

/// <summary>
/// 基于 <see cref="System.Buffers.Binary.BinaryPrimitives"/> 与
/// <see cref="System.Runtime.InteropServices.MemoryMarshal"/> 的
/// little-endian 二进制数据读取器。
/// 不持有缓冲区所有权，使用前须确保底层内存生命周期有效。
/// </summary>
/// <remarks>
/// TODO(M5): 在 SegmentReader / WalReader 中使用此工具类。
/// </remarks>
public ref struct SpanReader
{
    private ReadOnlySpan<byte> _remaining;
    private readonly int _initialLength;

    /// <summary>
    /// 使用指定的字节 span 初始化 <see cref="SpanReader"/>。
    /// </summary>
    /// <param name="data">要读取的字节数据。</param>
    public SpanReader(ReadOnlySpan<byte> data)
    {
        _remaining = data;
        _initialLength = data.Length;
    }

    /// <summary>剩余未读字节数。</summary>
    public int Remaining => _remaining.Length;

    /// <summary>已读取的字节数（即当前位置）。</summary>
    public int Position => _initialLength - _remaining.Length;

    /// <summary>
    /// 读取一个 <see cref="uint"/>（little-endian）。
    /// </summary>
    /// <returns>读取到的值。</returns>
    /// <exception cref="DotVectorException">当剩余字节不足时抛出。</exception>
    public uint ReadUInt32()
    {
        if (_remaining.Length < sizeof(uint))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(uint)} 字节，剩余 {_remaining.Length} 字节。");
        }

        uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_remaining);
        _remaining = _remaining[sizeof(uint)..];
        return value;
    }

    /// <summary>
    /// 读取一个 <see cref="ulong"/>（little-endian）。
    /// </summary>
    public ulong ReadUInt64()
    {
        if (_remaining.Length < sizeof(ulong))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(ulong)} 字节，剩余 {_remaining.Length} 字节。");
        }

        ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(_remaining);
        _remaining = _remaining[sizeof(ulong)..];
        return value;
    }

    /// <summary>
    /// 读取指定字节数并返回 <see cref="ReadOnlySpan{T}"/>。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_remaining.Length < count)
        {
            throw new DotVectorException($"缓冲区不足：需要 {count} 字节，剩余 {_remaining.Length} 字节。");
        }

        ReadOnlySpan<byte> result = _remaining[..count];
        _remaining = _remaining[count..];
        return result;
    }

    /// <summary>读取一个字节。</summary>
    public byte ReadByte()
    {
        if (_remaining.Length < 1)
        {
            throw new DotVectorException("缓冲区不足：需要 1 字节。");
        }
        byte v = _remaining[0];
        _remaining = _remaining[1..];
        return v;
    }

    /// <summary>读取一个 <see cref="ushort"/>（little-endian）。</summary>
    public ushort ReadUInt16()
    {
        if (_remaining.Length < sizeof(ushort))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(ushort)} 字节。");
        }
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_remaining);
        _remaining = _remaining[sizeof(ushort)..];
        return v;
    }

    /// <summary>读取一个 <see cref="int"/>（little-endian）。</summary>
    public int ReadInt32()
    {
        if (_remaining.Length < sizeof(int))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(int)} 字节。");
        }
        int v = BinaryPrimitives.ReadInt32LittleEndian(_remaining);
        _remaining = _remaining[sizeof(int)..];
        return v;
    }

    /// <summary>读取一个 <see cref="long"/>（little-endian）。</summary>
    public long ReadInt64()
    {
        if (_remaining.Length < sizeof(long))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(long)} 字节。");
        }
        long v = BinaryPrimitives.ReadInt64LittleEndian(_remaining);
        _remaining = _remaining[sizeof(long)..];
        return v;
    }

    /// <summary>读取一个 <see cref="float"/>（little-endian）。</summary>
    public float ReadSingle()
    {
        if (_remaining.Length < sizeof(float))
        {
            throw new DotVectorException($"缓冲区不足：需要 {sizeof(float)} 字节。");
        }
        float v = BinaryPrimitives.ReadSingleLittleEndian(_remaining);
        _remaining = _remaining[sizeof(float)..];
        return v;
    }

    /// <summary>读取一个 <see cref="Guid"/>（按 .NET 标准 16 字节格式）。</summary>
    public Guid ReadGuid()
    {
        ReadOnlySpan<byte> bytes = ReadBytes(16);
        return new Guid(bytes);
    }
}

/// <summary>
/// 基于 <see cref="System.Buffers.Binary.BinaryPrimitives"/> 的
/// little-endian 二进制数据写入器。
/// </summary>
/// <remarks>
/// TODO(M5): 在 SegmentWriter / WalWriter 中使用此工具类。
/// </remarks>
public ref struct SpanWriter
{
    private Span<byte> _remaining;
    private int _written;

    /// <summary>
    /// 使用指定的目标 span 初始化 <see cref="SpanWriter"/>。
    /// </summary>
    /// <param name="destination">写入目标缓冲区。</param>
    public SpanWriter(Span<byte> destination)
    {
        _remaining = destination;
        _written = 0;
    }

    /// <summary>已写入的字节数。</summary>
    public int Written => _written;

    /// <summary>
    /// 写入一个 <see cref="uint"/>（little-endian）。
    /// </summary>
    /// <param name="value">要写入的值。</param>
    public void WriteUInt32(uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(uint)..];
        _written += sizeof(uint);
    }

    /// <summary>
    /// 写入一个 <see cref="ulong"/>（little-endian）。
    /// </summary>
    public void WriteUInt64(ulong value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(ulong)..];
        _written += sizeof(ulong);
    }

    /// <summary>写入一个字节。</summary>
    public void WriteByte(byte value)
    {
        _remaining[0] = value;
        _remaining = _remaining[1..];
        _written += 1;
    }

    /// <summary>写入一个 <see cref="ushort"/>（little-endian）。</summary>
    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(ushort)..];
        _written += sizeof(ushort);
    }

    /// <summary>写入一个 <see cref="int"/>（little-endian）。</summary>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(int)..];
        _written += sizeof(int);
    }

    /// <summary>写入一个 <see cref="long"/>（little-endian）。</summary>
    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(long)..];
        _written += sizeof(long);
    }

    /// <summary>写入一个 <see cref="float"/>（little-endian）。</summary>
    public void WriteSingle(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_remaining, value);
        _remaining = _remaining[sizeof(float)..];
        _written += sizeof(float);
    }

    /// <summary>写入一个 <see cref="Guid"/>（按 .NET 标准 16 字节格式）。</summary>
    public void WriteGuid(Guid value)
    {
        if (!value.TryWriteBytes(_remaining))
        {
            throw new DotVectorException("缓冲区不足：写入 Guid 需要 16 字节。");
        }
        _remaining = _remaining[16..];
        _written += 16;
    }

    /// <summary>写入指定字节序列。</summary>
    public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_remaining);
        _remaining = _remaining[bytes.Length..];
        _written += bytes.Length;
    }
}
