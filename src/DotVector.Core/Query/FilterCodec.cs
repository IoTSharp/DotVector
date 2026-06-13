using System.IO;
using System.Text;

namespace DotVector.Query;

/// <summary>
/// 把 <see cref="Filter"/> AST 与 byte[] 之间互转的内部编解码器。
/// </summary>
/// <remarks>
/// 用于持久化和本地协议层复用过滤条件，字节序统一 little-endian（<see cref="BinaryWriter"/> 默认）。
/// </remarks>
internal static class FilterCodec
{
    /// <summary>过滤节点类型 tag。</summary>
    internal enum Tag : byte
    {
        Eq = 1,
        Ne = 2,
        Range = 3,
        Exists = 4,
        Missing = 5,
        And = 6,
        Or = 7,
        Not = 8,
    }

    /// <summary>标量值类型 tag。</summary>
    internal enum ValueTag : byte
    {
        Null = 0,
        Bool = 1,
        Int64 = 2,
        Float64 = 3,
        String = 4,
    }

    /// <summary>把 <paramref name="filter"/> 编码为 byte[]。</summary>
    public static byte[] Encode(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            filter.WriteTo(w);
        }
        return ms.ToArray();
    }

    /// <summary>把 byte[] 解码回 <see cref="Filter"/>。</summary>
    public static Filter Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) throw new InvalidDataException("空的 Filter 字节流。");
        using var ms = new MemoryStream(bytes, writable: false);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return ReadFilter(r);
    }

    private static Filter ReadFilter(BinaryReader r)
    {
        Tag tag = (Tag)r.ReadByte();
        switch (tag)
        {
            case Tag.Eq:
                {
                    string field = r.ReadString();
                    object? value = ReadValue(r);
                    return Filter.Eq(field, value);
                }
            case Tag.Ne:
                {
                    string field = r.ReadString();
                    object? value = ReadValue(r);
                    return Filter.Ne(field, value);
                }
            case Tag.Range:
                {
                    string field = r.ReadString();
                    object? min = ReadValue(r);
                    object? max = ReadValue(r);
                    bool minInc = r.ReadBoolean();
                    bool maxInc = r.ReadBoolean();
                    return Filter.Range(field, (IComparable?)min, (IComparable?)max, minInc, maxInc);
                }
            case Tag.Exists:
                return Filter.Exists(r.ReadString());
            case Tag.Missing:
                return Filter.Missing(r.ReadString());
            case Tag.And:
                {
                    int n = r.ReadByte();
                    var arr = new Filter[n];
                    for (int i = 0; i < n; i++) arr[i] = ReadFilter(r);
                    return Filter.And(arr);
                }
            case Tag.Or:
                {
                    int n = r.ReadByte();
                    var arr = new Filter[n];
                    for (int i = 0; i < n; i++) arr[i] = ReadFilter(r);
                    return Filter.Or(arr);
                }
            case Tag.Not:
                return Filter.Not(ReadFilter(r));
            default:
                throw new InvalidDataException($"未知 Filter tag：{(byte)tag}");
        }
    }

    /// <summary>写入一个标量值（null / bool / 整数 / 浮点 / 字符串）。</summary>
    public static void WriteValue(BinaryWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.Write((byte)ValueTag.Null);
                break;
            case bool b:
                w.Write((byte)ValueTag.Bool);
                w.Write(b);
                break;
            case sbyte sb: w.Write((byte)ValueTag.Int64); w.Write((long)sb); break;
            case byte by: w.Write((byte)ValueTag.Int64); w.Write((long)by); break;
            case short s: w.Write((byte)ValueTag.Int64); w.Write((long)s); break;
            case ushort us: w.Write((byte)ValueTag.Int64); w.Write((long)us); break;
            case int i: w.Write((byte)ValueTag.Int64); w.Write((long)i); break;
            case uint ui: w.Write((byte)ValueTag.Int64); w.Write((long)ui); break;
            case long l: w.Write((byte)ValueTag.Int64); w.Write(l); break;
            case float f: w.Write((byte)ValueTag.Float64); w.Write((double)f); break;
            case double d: w.Write((byte)ValueTag.Float64); w.Write(d); break;
            case string str:
                w.Write((byte)ValueTag.String);
                w.Write(str);
                break;
            default:
                throw new NotSupportedException($"标量类型 {value.GetType().FullName} 不可序列化（仅支持 null/bool/整数/浮点/字符串）。");
        }
    }

    /// <summary>读取一个标量值。</summary>
    public static object? ReadValue(BinaryReader r)
    {
        ValueTag tag = (ValueTag)r.ReadByte();
        return tag switch
        {
            ValueTag.Null => null,
            ValueTag.Bool => r.ReadBoolean(),
            ValueTag.Int64 => r.ReadInt64(),
            ValueTag.Float64 => r.ReadDouble(),
            ValueTag.String => r.ReadString(),
            _ => throw new InvalidDataException($"未知 Value tag：{(byte)tag}"),
        };
    }
}
