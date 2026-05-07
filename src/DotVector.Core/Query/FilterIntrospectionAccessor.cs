namespace DotVector.Query;

/// <summary>
/// 在程序集内部把 <see cref="Filter.GetIntrospection"/>（protected internal-ish）
/// 暴露给 <c>DotVector.Storage</c> 命名空间下的索引组件。
/// </summary>
internal static class FilterIntrospectionAccessor
{
    internal static object? View(Filter filter) => filter.GetIntrospection();
}
