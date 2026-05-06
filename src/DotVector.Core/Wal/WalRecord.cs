namespace DotVector.Wal;

/// <summary>
/// WAL 记录类型。
/// </summary>
internal enum WalRecordType : byte
{
    /// <summary>未指定。</summary>
    None = 0,
    /// <summary>插入或更新一条向量。</summary>
    Insert = 1,
    /// <summary>删除一条向量。</summary>
    Delete = 2,
}

/// <summary>
/// 反序列化后得到的 WAL 记录（与具体键类型解耦，由调用方按集合的 KeyType 解码）。
/// </summary>
internal readonly struct WalRecord
{
    /// <summary>记录类型。</summary>
    public WalRecordType Type { get; init; }

    /// <summary>记录所属的集合 GUID。</summary>
    public Guid CollectionId { get; init; }

    /// <summary>原始记录体（不含长度前缀和 CRC32），调用方按集合 KeyType 解析键与向量。</summary>
    public byte[] Body { get; init; }
}
