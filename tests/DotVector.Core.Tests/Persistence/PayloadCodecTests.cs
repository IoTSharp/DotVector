using DotVector.Exceptions;
using DotVector.Storage;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="PayloadCodec"/>（M11）：
/// TLV 编码/解码 round-trip、类型归一化、字段名长度限制、不支持类型抛出异常。
/// </summary>
public sealed class PayloadCodecTests
{
    [Fact]
    public void Encode_Decode_RoundTrip_PreservesAllSupportedTypes()
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nullField"] = null,
            ["boolTrue"] = true,
            ["boolFalse"] = false,
            ["int32"] = 42,
            ["int64"] = 9_000_000_000L,
            ["double"] = 3.14159,
            ["string"] = "hello, 世界",
            ["empty"] = string.Empty,
        };

        byte[] encoded = PayloadCodec.Encode(input);
        Assert.Equal(encoded.Length, PayloadCodec.ComputeSize(input));

        Dictionary<string, object?> decoded = PayloadCodec.Decode(encoded);
        Assert.Equal(input.Count, decoded.Count);
        Assert.Null(decoded["nullField"]);
        Assert.Equal(true, decoded["boolTrue"]);
        Assert.Equal(false, decoded["boolFalse"]);
        Assert.Equal(42L, decoded["int32"]);
        Assert.Equal(9_000_000_000L, decoded["int64"]);
        Assert.Equal(3.14159, (double)decoded["double"]!);
        Assert.Equal("hello, 世界", decoded["string"]);
        Assert.Equal(string.Empty, decoded["empty"]);
    }

    [Fact]
    public void Encode_NormalizesIntegerTypesToLong()
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["b"] = (byte)1,
            ["sb"] = (sbyte)-2,
            ["s"] = (short)3,
            ["us"] = (ushort)4,
            ["i"] = 5,
            ["ui"] = 6u,
            ["l"] = 7L,
        };

        Dictionary<string, object?> decoded = PayloadCodec.Decode(PayloadCodec.Encode(input));
        Assert.All(decoded.Values, v => Assert.IsType<long>(v));
    }

    [Fact]
    public void Encode_FloatNormalizesToDouble()
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["f"] = 1.5f,
        };
        Dictionary<string, object?> decoded = PayloadCodec.Decode(PayloadCodec.Encode(input));
        Assert.Equal(1.5, (double)decoded["f"]!, 6);
    }

    [Fact]
    public void Encode_EmptyDictionary_ProducesValidEncoding()
    {
        var input = new Dictionary<string, object?>();
        byte[] encoded = PayloadCodec.Encode(input);
        Dictionary<string, object?> decoded = PayloadCodec.Decode(encoded);
        Assert.Empty(decoded);
    }

    [Fact]
    public void Encode_UnsupportedType_ThrowsNotSupported()
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bad"] = new object(),
        };
        Assert.Throws<NotSupportedException>(() => PayloadCodec.Encode(input));
    }

    [Fact]
    public void Encode_FieldNameTooLong_Throws()
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [new string('x', 70_000)] = 1,
        };
        Assert.Throws<DotVectorException>(() => PayloadCodec.Encode(input));
    }
}
