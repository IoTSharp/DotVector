using DotVector.Query;

namespace DotVector.Core.Tests.Query;

/// <summary>
/// <see cref="Filter"/> AST 的单元测试（M6）。
/// </summary>
public sealed class FilterTests
{
    private static IReadOnlyDictionary<string, object?> P(params (string Key, object? Value)[] kv)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void Eq_Matches_When_Value_Equal()
    {
        var f = Filter.Eq("city", "BJ");
        Assert.True(f.Matches(P(("city", "BJ"))));
        Assert.False(f.Matches(P(("city", "SH"))));
        Assert.False(f.Matches(P(("other", "BJ"))));
        Assert.False(f.Matches(null));
    }

    [Fact]
    public void Eq_Null_Matches_Missing_Or_Null()
    {
        var f = Filter.Eq("x", null);
        Assert.True(f.Matches(null));
        Assert.True(f.Matches(P(("x", null))));
        Assert.True(f.Matches(P(("y", 1))));
        Assert.False(f.Matches(P(("x", 1))));
    }

    [Fact]
    public void Ne_Is_Inverse_Of_Eq()
    {
        var f = Filter.Ne("city", "BJ");
        Assert.False(f.Matches(P(("city", "BJ"))));
        Assert.True(f.Matches(P(("city", "SH"))));
        Assert.True(f.Matches(P(("other", "BJ"))));
    }

    [Fact]
    public void Range_Numeric_Inclusive()
    {
        var f = Filter.Range("age", min: 18, max: 65);
        Assert.True(f.Matches(P(("age", 18))));
        Assert.True(f.Matches(P(("age", 30))));
        Assert.True(f.Matches(P(("age", 65))));
        Assert.False(f.Matches(P(("age", 17))));
        Assert.False(f.Matches(P(("age", 66))));
        Assert.False(f.Matches(P(("age", null))));
        Assert.False(f.Matches(null));
    }

    [Fact]
    public void Range_Exclusive_Bounds()
    {
        var f = Filter.Range("age", min: 18, max: 65, minInclusive: false, maxInclusive: false);
        Assert.False(f.Matches(P(("age", 18))));
        Assert.False(f.Matches(P(("age", 65))));
        Assert.True(f.Matches(P(("age", 19))));
        Assert.True(f.Matches(P(("age", 64))));
    }

    [Fact]
    public void Range_OnlyMin_OrMax()
    {
        var fMin = Filter.Range("age", min: 18);
        Assert.True(fMin.Matches(P(("age", 18))));
        Assert.True(fMin.Matches(P(("age", 999))));
        Assert.False(fMin.Matches(P(("age", 17))));

        var fMax = Filter.Range("age", max: 18);
        Assert.True(fMax.Matches(P(("age", 18))));
        Assert.True(fMax.Matches(P(("age", -1))));
        Assert.False(fMax.Matches(P(("age", 19))));
    }

    [Fact]
    public void Range_Throws_When_Both_Null()
    {
        Assert.Throws<ArgumentException>(() => Filter.Range("x"));
    }

    [Fact]
    public void Range_Type_Mismatch_Returns_False()
    {
        // payload 字段是字符串，而 range 给的是数字 -> 不匹配（不抛）。
        var f = Filter.Range("age", min: 0, max: 100);
        Assert.False(f.Matches(P(("age", "thirty"))));
    }

    [Fact]
    public void Exists_And_Missing()
    {
        var ex = Filter.Exists("k");
        var mi = Filter.Missing("k");
        Assert.True(ex.Matches(P(("k", 1))));
        Assert.False(ex.Matches(P(("k", null))));
        Assert.False(ex.Matches(P(("other", 1))));
        Assert.False(ex.Matches(null));

        Assert.False(mi.Matches(P(("k", 1))));
        Assert.True(mi.Matches(P(("k", null))));
        Assert.True(mi.Matches(P(("other", 1))));
        Assert.True(mi.Matches(null));
    }

    [Fact]
    public void And_Or_Not_Composition()
    {
        var f = Filter.And(
            Filter.Eq("city", "BJ"),
            Filter.Or(
                Filter.Range("age", min: 18, max: 30),
                Filter.Eq("vip", true)),
            Filter.Not(Filter.Eq("banned", true)));

        Assert.True(f.Matches(P(("city", "BJ"), ("age", 25))));
        Assert.True(f.Matches(P(("city", "BJ"), ("age", 40), ("vip", true))));
        Assert.False(f.Matches(P(("city", "SH"), ("age", 25))));
        Assert.False(f.Matches(P(("city", "BJ"), ("age", 25), ("banned", true))));
    }

    [Fact]
    public void Factories_Reject_Invalid_Args()
    {
        Assert.Throws<ArgumentException>(() => Filter.Eq("", 1));
        Assert.Throws<ArgumentException>(() => Filter.And());
        Assert.Throws<ArgumentException>(() => Filter.Or());
        Assert.Throws<ArgumentNullException>(() => Filter.Not(null!));
    }
}
