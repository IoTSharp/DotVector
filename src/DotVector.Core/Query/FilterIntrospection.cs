namespace DotVector.Query;

/// <summary>
/// 内部用于把 <see cref="Filter"/> 节点投影成轻量视图，
/// 让 <c>ScalarIndex</c> 等下游组件无需 visit 私有子类即可下推过滤条件（M11）。
/// </summary>
internal static class FilterIntrospection
{
    internal sealed record EqualsView(string Field, object Value);

    internal sealed record RangeView(
        string Field,
        IComparable? Min,
        IComparable? Max,
        bool MinInclusive,
        bool MaxInclusive);

    internal sealed record AndView(IReadOnlyList<Filter> Children);
}
