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

    /// <summary>
    /// 使用指定的字节 span 初始化 <see cref="SpanReader"/>。
    /// </summary>
    /// <param name="data">要读取的字节数据。</param>
    public SpanReader(ReadOnlySpan<byte> data)
    {
        _remaining = data;
    }

    /// <summary>剩余未读字节数。</summary>
    public int Remaining => _remaining.Length;

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
}
