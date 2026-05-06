namespace DotVector.Api;

/// <summary>
/// 由 <see cref="Collection{TKey}"/> 实现的非泛型契约，
/// 供 <see cref="VectorDatabase"/> 在不知道具体 <c>TKey</c> 的情况下
/// 触发持久化操作（Flush / Compact）。
/// </summary>
internal interface IPersistableCollection
{
    /// <summary>把当前内存索引快照刷成新 Segment 并旋转 WAL（M10）。</summary>
    void Flush();

    /// <summary>合并所有 Segment 为单个 Segment（M10）。</summary>
    void Compact();
}
