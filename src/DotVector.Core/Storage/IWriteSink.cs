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

    /// <summary>设置或清空一条记录的标量 payload（M11）。</summary>
    /// <param name="key">记录主键。</param>
    /// <param name="encodedPayload">已通过 <see cref="PayloadCodec"/> 编码的 payload 字节序列；
    /// 长度为 0 表示清空 payload。</param>
    void OnPayload(TKey key, ReadOnlySpan<byte> encodedPayload);
}
