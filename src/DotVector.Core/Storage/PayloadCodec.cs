using System.Buffers.Binary;
using System.Text;
using DotVector.Exceptions;

namespace DotVector.Storage;

/// <summary>
/// Payload 的 TLV 风格自描述二进制编解码器（M11）。
/// </summary>
/// <remarks>
/// <para>
/// 不引入 Newtonsoft.Json / MessagePack 等第三方依赖，
/// 使用 <see cref="BinaryPrimitives"/> + <see cref="Encoding.UTF8"/> 手写实现。
/// 所有多字节字段一律 little-endian。
/// </para>
/// <para>
/// 顶层格式：
/// <c>u32 fieldCount + repeated{ u16 nameByteLen + name(UTF-8) + u8 valueTypeCode + value }</c>
/// </para>
/// <para>
/// 值类型编码：
/// <list type="bullet">
///   <item><c>0 = Null</c>：无 value 字节。</item>
///   <item><c>1 = Bool</c>：u8（0 / 1）。</item>
///   <item><c>2 = Long</c>：i64 little-endian。</item>
///   <item><c>3 = Double</c>：f64 little-endian。</item>
///   <item><c>4 = String</c>：u32 byteLen + UTF-8 字节。</item>
/// </list>
/// </para>
/// <para>
/// Encode 阶段会做类型归一化：
/// <c>byte / sbyte / short / ushort / int / uint / long</c> → Long；
/// <c>float</c> → Double；
/// 其它类型抛出 <see cref="NotSupportedException"/>。
/// </para>
/// </remarks>
internal static class PayloadCodec
{
    /// <summary>Null 值类型码。</summary>
    public const byte TypeNull = 0;
    /// <summary>Bool 值类型码。</summary>
    public const byte TypeBool = 1;
    /// <summary>Long 值类型码。</summary>
    public const byte TypeLong = 2;
    /// <summary>Double 值类型码。</summary>
    public const byte TypeDouble = 3;
    /// <summary>String 值类型码。</summary>
    public const byte TypeString = 4;

    /// <summary>计算编码后的字节数。</summary>
    public static int ComputeSize(IReadOnlyDictionary<string, object?> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        int total = 4; // u32 fieldCount
        foreach (KeyValuePair<string, object?> kv in payload)
        {
            int nameBytes = Encoding.UTF8.GetByteCount(kv.Key);
            if (nameBytes > ushort.MaxValue)
            {
                throw new DotVectorException(
                    $"payload 字段名过长：{nameBytes} 字节（最长 {ushort.MaxValue} 字节）。");
            }
            total += 2 + nameBytes + ValueByteSize(kv.Value);
        }
        return total;
    }

    /// <summary>把 payload 字典编码为字节数组。</summary>
    public static byte[] Encode(IReadOnlyDictionary<string, object?> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        int size = ComputeSize(payload);
        byte[] buffer = new byte[size];
        Span<byte> span = buffer;
        int offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), (uint)payload.Count);
        offset += 4;
        foreach (KeyValuePair<string, object?> kv in payload)
        {
            int nameBytes = Encoding.UTF8.GetByteCount(kv.Key);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)nameBytes);
            offset += 2;
            Encoding.UTF8.GetBytes(kv.Key, span.Slice(offset, nameBytes));
            offset += nameBytes;
            offset += WriteValue(span[offset..], kv.Value);
        }
        if (offset != size)
        {
            throw new DotVectorException(
                $"PayloadCodec.Encode 写入长度不匹配：实际 {offset}，预期 {size}。");
        }
        return buffer;
    }

    /// <summary>把字节数组解码回 payload 字典。</summary>
    public static Dictionary<string, object?> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new DotVectorException($"PayloadCodec.Decode 输入过短：{data.Length} 字节。");
        }
        uint fieldCount = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        int offset = 4;
        var result = new Dictionary<string, object?>((int)fieldCount, StringComparer.Ordinal);
        for (uint i = 0; i < fieldCount; i++)
        {
            if (data.Length - offset < 2)
            {
                throw new DotVectorException("PayloadCodec.Decode：字段名长度截断。");
            }
            ushort nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
            if (data.Length - offset < nameBytes + 1)
            {
                throw new DotVectorException("PayloadCodec.Decode：字段名 / 类型码截断。");
            }
            string name = Encoding.UTF8.GetString(data.Slice(offset, nameBytes));
            offset += nameBytes;
            byte typeCode = data[offset];
            offset += 1;
            object? value = ReadValue(data, ref offset, typeCode);
            result[name] = value;
        }
        return result;
    }

    private static int ValueByteSize(object? value)
    {
        // 1 字节类型码 + 值字节数
        switch (value)
        {
            case null:
                return 1;
            case bool:
                return 1 + 1;
            case byte or sbyte or short or ushort or int or uint or long:
                return 1 + 8;
            case float or double:
                return 1 + 8;
            case string s:
                {
                    int n = Encoding.UTF8.GetByteCount(s);
                    return 1 + 4 + n;
                }
            default:
                throw new NotSupportedException(
                    $"PayloadCodec 不支持的值类型：{value.GetType().FullName}（仅支持 null / bool / 整数 / 浮点 / string）。");
        }
    }

    private static int WriteValue(Span<byte> dest, object? value)
    {
        switch (value)
        {
            case null:
                dest[0] = TypeNull;
                return 1;
            case bool b:
                dest[0] = TypeBool;
                dest[1] = (byte)(b ? 1 : 0);
                return 2;
            case byte u8:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), u8);
                return 9;
            case sbyte i8:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), i8);
                return 9;
            case short i16:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), i16);
                return 9;
            case ushort u16:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), u16);
                return 9;
            case int i32:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), i32);
                return 9;
            case uint u32:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), u32);
                return 9;
            case long i64:
                dest[0] = TypeLong;
                BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(1, 8), i64);
                return 9;
            case float f32:
                dest[0] = TypeDouble;
                BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(1, 8), f32);
                return 9;
            case double f64:
                dest[0] = TypeDouble;
                BinaryPrimitives.WriteDoubleLittleEndian(dest.Slice(1, 8), f64);
                return 9;
            case string s:
                {
                    dest[0] = TypeString;
                    int n = Encoding.UTF8.GetByteCount(s);
                    BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(1, 4), (uint)n);
                    Encoding.UTF8.GetBytes(s, dest.Slice(5, n));
                    return 5 + n;
                }
            default:
                throw new NotSupportedException(
                    $"PayloadCodec 不支持的值类型：{value.GetType().FullName}。");
        }
    }

    private static object? ReadValue(ReadOnlySpan<byte> data, ref int offset, byte typeCode)
    {
        switch (typeCode)
        {
            case TypeNull:
                return null;
            case TypeBool:
                if (data.Length - offset < 1)
                {
                    throw new DotVectorException("PayloadCodec.Decode：bool 截断。");
                }
                bool b = data[offset] != 0;
                offset += 1;
                return b;
            case TypeLong:
                if (data.Length - offset < 8)
                {
                    throw new DotVectorException("PayloadCodec.Decode：long 截断。");
                }
                long lv = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
                offset += 8;
                return lv;
            case TypeDouble:
                if (data.Length - offset < 8)
                {
                    throw new DotVectorException("PayloadCodec.Decode：double 截断。");
                }
                double dv = BinaryPrimitives.ReadDoubleLittleEndian(data.Slice(offset, 8));
                offset += 8;
                return dv;
            case TypeString:
                if (data.Length - offset < 4)
                {
                    throw new DotVectorException("PayloadCodec.Decode：string 长度截断。");
                }
                uint n = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                offset += 4;
                if (data.Length - offset < (int)n)
                {
                    throw new DotVectorException("PayloadCodec.Decode：string 字节截断。");
                }
                string sv = Encoding.UTF8.GetString(data.Slice(offset, (int)n));
                offset += (int)n;
                return sv;
            default:
                throw new DotVectorException(
                    $"PayloadCodec.Decode：未知值类型码 {typeCode}。");
        }
    }
}
