using DotVector.Model;

namespace DotVector.Api;

/// <summary>
/// DotVector 嵌入式向量数据库的顶层入口。
/// 管理多个向量集合（Collection），支持进程内嵌入式运行。
/// </summary>
/// <remarks>
/// TODO(M2): 实现基于 FlatIndex 的内存向量数据库。
/// TODO(M5): 实现基于目录（.dvec/）的持久化存储。
/// </remarks>
public sealed class VectorDatabase : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 创建一个纯内存的 <see cref="VectorDatabase"/> 实例。
    /// 数据不会持久化，进程退出后丢失。
    /// </summary>
    public VectorDatabase()
    {
    }

    /// <summary>
    /// 创建或打开指定路径的持久化向量数据库目录（.dvec/）。
    /// </summary>
    /// <param name="directoryPath">数据库目录路径，例如 "my-db.dvec"。</param>
    /// <remarks>
    /// TODO(M5): 实现目录持久化逻辑（catalog.bin + WAL + Segment mmap）。
    /// </remarks>
    public VectorDatabase(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        // TODO(M5): 打开或创建目录，读取 catalog.bin，replay WAL
    }

    /// <summary>
    /// 创建新的向量集合。
    /// </summary>
    /// <typeparam name="TKey">记录主键类型。</typeparam>
    /// <param name="name">集合名称，在同一数据库内唯一。</param>
    /// <param name="dimensions">向量维度（如 384 / 768 / 1536）。</param>
    /// <param name="metric">距离度量类型，默认为 <see cref="Metric.Cosine"/>。</param>
    /// <returns>新建的集合实例。</returns>
    /// <remarks>
    /// TODO(M2): 实现内存集合创建逻辑。
    /// </remarks>
    public Collection<TKey> CreateCollection<TKey>(
        string name,
        int dimensions,
        Metric metric = Metric.Cosine)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);

        // TODO(M2): 创建并注册集合
        return new Collection<TKey>(name, dimensions, metric);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            // TODO(M5): 释放 mmap 句柄，flush MemTable
            _disposed = true;
        }
    }
}
