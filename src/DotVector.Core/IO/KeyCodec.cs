using System.Text;
using DotVector.Exceptions;

namespace DotVector.IO;

/// <summary>
/// 持久化层支持的键类型枚举。值会随键序列化一起写入二进制格式，
/// 不得随意调整。
/// </summary>
public enum KeyTypeCode : byte
{
    /// <summary>未指定。</summary>
    None = 0,
    /// <summary><see cref="int"/>（4 字节，little-endian）。</summary>
    Int32 = 1,
    /// <summary><see cref="long"/>（8 字节，little-endian）。</summary>
    Int64 = 2,
    /// <summary><see cref="Guid"/>（16 字节）。</summary>
    Guid = 3,
    /// <summary>UTF-8 字符串（u16 长度前缀 + 字节，最长 65535 字节）。</summary>
    String = 4,
}

/// <summary>
/// 持久化层在 WAL / catalog 中读写键值的工具类。
/// 仅支持 <c>int / long / Guid / string</c> 四种键类型，与
/// <see cref="KeyTypeCode"/> 一一对应。
/// </summary>
internal static class KeyCodec
{
    /// <summary>获取 <typeparamref name="TKey"/> 对应的 <see cref="KeyTypeCode"/>。</summary>
    public static KeyTypeCode GetCode<TKey>() where TKey : notnull
    {
        if (typeof(TKey) == typeof(int)) return KeyTypeCode.Int32;
        if (typeof(TKey) == typeof(long)) return KeyTypeCode.Int64;
        if (typeof(TKey) == typeof(Guid)) return KeyTypeCode.Guid;
        if (typeof(TKey) == typeof(string)) return KeyTypeCode.String;
        throw new DotVectorException($"不支持的键类型：{typeof(TKey).FullName}（仅支持 int / long / Guid / string）。");
    }

    /// <summary>将键写入指定写入器（不含类型标签，仅写入键体）。</summary>
    public static void Write<TKey>(scoped ref SpanWriter writer, TKey key) where TKey : notnull
    {
        switch (GetCode<TKey>())
        {
            case KeyTypeCode.Int32:
                writer.WriteInt32((int)(object)key);
                break;
            case KeyTypeCode.Int64:
                writer.WriteInt64((long)(object)key);
                break;
            case KeyTypeCode.Guid:
                writer.WriteGuid((Guid)(object)key);
                break;
            case KeyTypeCode.String:
                {
                    string s = (string)(object)key;
                    int byteCount = Encoding.UTF8.GetByteCount(s);
                    if (byteCount > ushort.MaxValue)
                    {
                        throw new DotVectorException($"字符串键过长：{byteCount} 字节（最长 {ushort.MaxValue} 字节）。");
                    }
                    writer.WriteUInt16((ushort)byteCount);
                    if (byteCount <= 256)
                    {
                        Span<byte> tmp = stackalloc byte[byteCount];
                        Encoding.UTF8.GetBytes(s, tmp);
                        writer.WriteBytes(tmp);
                    }
                    else
                    {
                        byte[] tmp = new byte[byteCount];
                        Encoding.UTF8.GetBytes(s, tmp);
                        writer.WriteBytes(tmp);
                    }
                    break;
                }
            default:
                throw new DotVectorException("不支持的键类型。");
        }
    }

    /// <summary>从指定读取器读取一个 <typeparamref name="TKey"/> 键。</summary>
    public static TKey Read<TKey>(ref SpanReader reader) where TKey : notnull
    {
        switch (GetCode<TKey>())
        {
            case KeyTypeCode.Int32:
                return (TKey)(object)reader.ReadInt32();
            case KeyTypeCode.Int64:
                return (TKey)(object)reader.ReadInt64();
            case KeyTypeCode.Guid:
                return (TKey)(object)reader.ReadGuid();
            case KeyTypeCode.String:
                {
                    ushort len = reader.ReadUInt16();
                    ReadOnlySpan<byte> bytes = reader.ReadBytes(len);
                    return (TKey)(object)Encoding.UTF8.GetString(bytes);
                }
            default:
                throw new DotVectorException("不支持的键类型。");
        }
    }

    /// <summary>计算键的固定字节数（仅类型确定时；string 返回 -1，需要按内容计算）。</summary>
    public static int FixedSize(KeyTypeCode code) => code switch
    {
        KeyTypeCode.Int32 => 4,
        KeyTypeCode.Int64 => 8,
        KeyTypeCode.Guid => 16,
        KeyTypeCode.String => -1,
        _ => throw new DotVectorException("不支持的键类型。"),
    };

    /// <summary>计算指定键的实际字节数（含字符串的长度前缀 2 字节）。</summary>
    public static int ComputeSize<TKey>(TKey key) where TKey : notnull
    {
        KeyTypeCode code = GetCode<TKey>();
        if (code != KeyTypeCode.String) return FixedSize(code);
        return 2 + Encoding.UTF8.GetByteCount((string)(object)key);
    }
}
