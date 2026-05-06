namespace DotVector.Core;

/// <summary>
/// 向量数据持久化存储的统一抽象接口。
/// </summary>
/// <remarks>
/// TODO(M5): 实现基于目录（.dvec/）的 DirectoryStorage。
/// </remarks>
public interface IStorage : IDisposable
{
    /// <summary>存储路径（内存存储返回 <see langword="null"/>）。</summary>
    string? Path { get; }

    /// <summary>
    /// 将一批向量数据写入新 Segment。
    /// </summary>
    /// <param name="vectors">向量数据（行优先，每行 dimensions 个 float）。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建 Segment 的序列号。</returns>
    ValueTask<ulong> WriteSegmentAsync(
        ReadOnlyMemory<float> vectors,
        int dimensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以只读 <see cref="ReadOnlyMemory{T}"/> 方式读取指定 Segment 的向量数据（零拷贝 mmap）。
    /// </summary>
    /// <param name="segmentSequence">Segment 序列号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<ReadOnlyMemory<float>> ReadSegmentAsync(
        ulong segmentSequence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行 WAL replay，恢复未 flush 的写操作（崩溃恢复）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask ReplayWalAsync(CancellationToken cancellationToken = default);
}
