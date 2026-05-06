namespace DotVector.Storage;

/// <summary>
/// 集合写入观察者：在 <see cref="Api.Collection{TKey}"/> 修改索引之前
/// 收到通知，用于将变更写入 WAL 等持久化层。
/// </summary>
/// <typeparam name="TKey">键类型。</typeparam>
internal interface IWriteSink<TKey> where TKey : notnull
{
    /// <summary>插入或更新一条记录前调用。</summary>
    void OnInsert(TKey key, ReadOnlySpan<float> vector);

    /// <summary>删除一条记录前调用。</summary>
    void OnDelete(TKey key);
}
