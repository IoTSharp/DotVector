namespace DotVector.Native;

/// <summary>
/// C ABI / P-Invoke 连接器占位类型。
/// 将在 M9 中实现，通过 [UnmanagedCallersOnly] 暴露 C ABI 供其他语言调用。
/// </summary>
/// <remarks>
/// TODO(M9): 使用 [UnmanagedCallersOnly] 导出 dotvector_search / dotvector_insert 等 C 函数。
/// </remarks>
public static class NativePlaceholder
{
    /// <summary>
    /// 将在 M9 中导出为 C 函数 <c>dotvector_version</c>。
    /// </summary>
    public static string GetVersion() => "DotVector.Native M9 占位 — C ABI 将在 M9 实现。";
}
