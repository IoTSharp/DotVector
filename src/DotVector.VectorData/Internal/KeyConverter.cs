using System.Globalization;

namespace DotVector.VectorData.Internal;

/// <summary>
/// 提供 <typeparamref name="TKey"/> 与协议层字符串 ID 之间的双向转换。
/// </summary>
/// <typeparam name="TKey">VectorData 集合的主键类型。</typeparam>
/// <remarks>
/// DotVector 协议层（<see cref="Core.Protocol.VectorUpsertRecord.Id"/>、
/// <see cref="Core.Protocol.VectorSearchResult.Id"/>）统一使用字符串 ID，
/// 而 <c>VectorStoreCollection&lt;TKey, TRecord&gt;</c> 允许任意键类型，
/// 因此适配层需要在边界进行 TKey ↔ string 的转换。
/// 当前 M7 支持：<see cref="string"/> / <see cref="int"/> / <see cref="long"/> /
/// <see cref="Guid"/>。
/// TODO(M7+): 支持自定义 <see cref="TypeConverter"/> / <c>IParsable&lt;T&gt;</c>。
/// </remarks>
internal static class KeyConverter<TKey>
    where TKey : notnull
{
    private static readonly Func<TKey, string> ToStringFn = CreateToString();
    private static readonly Func<string, TKey> FromStringFn = CreateFromString();

    /// <summary>把 <typeparamref name="TKey"/> 转换为协议层使用的字符串 ID。</summary>
    public static string ToProtocolId(TKey key) => ToStringFn(key);

    /// <summary>把协议层字符串 ID 解析回 <typeparamref name="TKey"/>。</summary>
    public static TKey FromProtocolId(string id) => FromStringFn(id);

    private static Func<TKey, string> CreateToString()
    {
        var t = typeof(TKey);
        if (t == typeof(string)) return k => (string)(object)k!;
        if (t == typeof(int)) return k => ((int)(object)k!).ToString(CultureInfo.InvariantCulture);
        if (t == typeof(long)) return k => ((long)(object)k!).ToString(CultureInfo.InvariantCulture);
        if (t == typeof(Guid)) return k => ((Guid)(object)k!).ToString("D", CultureInfo.InvariantCulture);
        return _ => throw new NotSupportedException(
            $"DotVector.VectorData 暂不支持键类型 {t.FullName}；请使用 string/int/long/Guid。");
    }

    private static Func<string, TKey> CreateFromString()
    {
        var t = typeof(TKey);
        if (t == typeof(string)) return s => (TKey)(object)s;
        if (t == typeof(int)) return s => (TKey)(object)int.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return s => (TKey)(object)long.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(Guid)) return s => (TKey)(object)Guid.Parse(s, CultureInfo.InvariantCulture);
        return _ => throw new NotSupportedException(
            $"DotVector.VectorData 暂不支持键类型 {t.FullName}；请使用 string/int/long/Guid。");
    }
}
