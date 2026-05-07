using DotVector.Query;
using DotVector.Storage;

namespace DotVector.Core.Tests.Index;

public class ScalarIndexTests
{
    private static IReadOnlyDictionary<string, object?> Payload(params (string K, object? V)[] kvs)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in kvs) d[k] = v;
        return d;
    }

    [Fact]
    public void Eq_String_ReturnsMatchingKeys()
    {
        var idx = new ScalarIndex<int>();
        idx.Update(1, null, Payload(("color", "red")));
        idx.Update(2, null, Payload(("color", "blue")));
        idx.Update(3, null, Payload(("color", "red")));

        Assert.True(idx.TryResolveCandidates(Filter.Eq("color", "red"), out var c));
        Assert.NotNull(c);
        Assert.Equal(new[] { 1, 3 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Eq_Long_NormalizedToDouble()
    {
        var idx = new ScalarIndex<int>();
        idx.Update(1, null, Payload(("age", 30L)));
        idx.Update(2, null, Payload(("age", 25L)));

        Assert.True(idx.TryResolveCandidates(Filter.Eq("age", 30L), out var c));
        Assert.Equal(new[] { 1 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Eq_Bool_BothBuckets()
    {
        var idx = new ScalarIndex<int>();
        idx.Update(1, null, Payload(("active", true)));
        idx.Update(2, null, Payload(("active", false)));
        idx.Update(3, null, Payload(("active", true)));

        Assert.True(idx.TryResolveCandidates(Filter.Eq("active", true), out var c));
        Assert.Equal(new[] { 1, 3 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Range_Numeric_InclusiveBounds()
    {
        var idx = new ScalarIndex<int>();
        for (int i = 1; i <= 10; i++) idx.Update(i, null, Payload(("score", (double)i)));

        Assert.True(idx.TryResolveCandidates(Filter.Range("score", 3.0, 7.0, true, true), out var c));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Range_Numeric_HalfOpenBounds()
    {
        var idx = new ScalarIndex<int>();
        for (int i = 1; i <= 10; i++) idx.Update(i, null, Payload(("score", (double)i)));

        Assert.True(idx.TryResolveCandidates(Filter.Range("score", 3.0, 7.0, false, false), out var c));
        Assert.Equal(new[] { 4, 5, 6 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void And_EqAndRange_Intersects()
    {
        var idx = new ScalarIndex<int>();
        idx.Update(1, null, Payload(("color", "red"), ("score", 5.0)));
        idx.Update(2, null, Payload(("color", "red"), ("score", 50.0)));
        idx.Update(3, null, Payload(("color", "blue"), ("score", 5.0)));

        Filter f = Filter.And(Filter.Eq("color", "red"), Filter.Range("score", 0.0, 10.0, true, true));
        Assert.True(idx.TryResolveCandidates(f, out var c));
        Assert.Equal(new[] { 1 }, c!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Or_NotSupported_ReturnsFalse()
    {
        var idx = new ScalarIndex<int>();
        idx.Update(1, null, Payload(("color", "red")));

        Assert.False(idx.TryResolveCandidates(Filter.Or(Filter.Eq("color", "red"), Filter.Eq("color", "blue")), out var c));
        Assert.Null(c);
    }

    [Fact]
    public void Update_OverwritesOldValueBucket()
    {
        var idx = new ScalarIndex<int>();
        var oldP = Payload(("color", "red"));
        idx.Update(1, null, oldP);

        var newP = Payload(("color", "blue"));
        idx.Update(1, oldP, newP);

        Assert.True(idx.TryResolveCandidates(Filter.Eq("color", "red"), out var red));
        Assert.Empty(red!);
        Assert.True(idx.TryResolveCandidates(Filter.Eq("color", "blue"), out var blue));
        Assert.Equal(new[] { 1 }, blue!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Remove_ClearsKeyFromAllBuckets()
    {
        var idx = new ScalarIndex<int>();
        var p = Payload(("color", "red"), ("score", 5.0));
        idx.Update(1, null, p);
        idx.Remove(1, p);

        Assert.True(idx.TryResolveCandidates(Filter.Eq("color", "red"), out var c));
        Assert.Empty(c!);
    }
}
