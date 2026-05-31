using DotVector.Api;
using DotVector.Core;
using DotVector.Core.Protocol;

namespace DotVector.Data;

/// <summary>
/// DotVector 的高层客户端门面。封装协议级 <see cref="IDotVectorClient"/>，
/// 提供面向本地嵌入式数据库的简洁 API。
/// </summary>
/// <remarks>
/// <para>
/// DotVector 独立 Server / gRPC / Docker 服务端形态已经删除；需要服务端
/// endpoint 时应使用 SonnetDB。DotVector 本仓库只提供本地嵌入式访问。
/// </para>
/// <para>
/// 也可通过 <see cref="DotVectorClient(IDotVectorClient, bool)"/> 注入自定义传输实现
/// （例如测试用的 <c>InMemoryDotVectorClient</c>）。
/// </para>
/// </remarks>
public sealed class DotVectorClient : IAsyncDisposable
{
    private readonly IDotVectorClient _inner;
    private readonly bool _ownsInner;

    /// <summary>
    /// 使用调用者构造的 <see cref="IDotVectorClient"/> 协议实现。
    /// </summary>
    /// <param name="inner">底层协议客户端。</param>
    /// <param name="ownsInner">为 <see langword="true"/> 时由本实例 <see cref="DisposeAsync"/> 释放。</param>
    public DotVectorClient(IDotVectorClient inner, bool ownsInner = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _ownsInner = ownsInner;
    }

    /// <summary>底层协议客户端，便于直接调用低层 DTO API。</summary>
    public IDotVectorClient Protocol => _inner;

    // -------------------------------------------------------------------------
    // 工厂：已移除的远程服务端兼容入口
    // -------------------------------------------------------------------------

    /// <summary>
    /// DotVector 独立远程服务端已删除；请改用 <see cref="Embedded(string)"/> 或 SonnetDB 服务端。
    /// </summary>
    /// <param name="endpoint">旧版远程服务端地址。该参数仅为源兼容保留。</param>
    /// <exception cref="NotSupportedException">始终抛出，说明远程服务端模式已移除。</exception>
    public static DotVectorClient Connect(string endpoint)
        => Connect(endpoint, new DotVectorClientOptions());

    /// <summary>
    /// DotVector 独立远程服务端已删除；请改用 <see cref="Embedded(string)"/> 或 SonnetDB 服务端。
    /// </summary>
    /// <param name="endpoint">旧版远程服务端地址。该参数仅为源兼容保留。</param>
    /// <param name="options">旧版远程连接选项。该参数仅为源兼容保留。</param>
    /// <exception cref="NotSupportedException">始终抛出，说明远程服务端模式已移除。</exception>
    public static DotVectorClient Connect(string endpoint, DotVectorClientOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        throw new NotSupportedException(
            "DotVector remote server mode has been removed. Use DotVectorClient.Embedded(path) for local databases, or use SonnetDB when a service endpoint is required.");
    }

    // -------------------------------------------------------------------------
    // 工厂：嵌入式（进程内）
    // -------------------------------------------------------------------------

    /// <summary>
    /// 在进程内打开本地数据库目录，零网络、零序列化开销。
    /// </summary>
    /// <param name="dataDirectory">数据库目录（不存在则会被创建）。</param>
    public static DotVectorClient Embedded(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        var local = new LocalDotVectorClient(dataDirectory);
        return new DotVectorClient(local, ownsInner: true);
    }

    // -------------------------------------------------------------------------
    // 顶层操作
    // -------------------------------------------------------------------------

    /// <summary>检查本地客户端是否可用。</summary>
    public ValueTask<bool> PingAsync(CancellationToken ct = default)
        => _inner.PingAsync(ct);

    /// <summary>列出所有集合。</summary>
    public ValueTask<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken ct = default)
        => _inner.ListCollectionsAsync(ct);

    /// <summary>判定指定集合是否存在。</summary>
    public async ValueTask<bool> CollectionExistsAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        IReadOnlyList<CollectionInfo> all = await _inner.ListCollectionsAsync(ct).ConfigureAwait(false);
        for (int i = 0; i < all.Count; i++)
        {
            if (string.Equals(all[i].Name, name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// 创建集合并返回其句柄。若集合已存在将抛出异常；幂等场景请用 <see cref="EnsureCollectionAsync"/>。
    /// </summary>
    public async ValueTask<DotVectorClientCollection> CreateCollectionAsync(
        string name,
        int dimensions,
        DistanceMetric metric = DistanceMetric.Cosine,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        await _inner.CreateCollectionAsync(
            new CreateCollectionRequest(name, dimensions, metric.ToWire()),
            ct).ConfigureAwait(false);
        return new DotVectorClientCollection(_inner, name);
    }

    /// <summary>
    /// 若集合不存在则创建；存在则直接返回句柄（不校验维度 / 度量是否一致）。
    /// </summary>
    public async ValueTask<DotVectorClientCollection> EnsureCollectionAsync(
        string name,
        int dimensions,
        DistanceMetric metric = DistanceMetric.Cosine,
        CancellationToken ct = default)
    {
        if (await CollectionExistsAsync(name, ct).ConfigureAwait(false))
        {
            return new DotVectorClientCollection(_inner, name);
        }
        return await CreateCollectionAsync(name, dimensions, metric, ct).ConfigureAwait(false);
    }

    /// <summary>删除集合（不存在时由实现决定是否抛出）。</summary>
    public ValueTask DeleteCollectionAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _inner.DeleteCollectionAsync(name, ct);
    }

    /// <summary>
    /// 取得集合句柄（不发请求、不校验存在性）。
    /// </summary>
    public DotVectorClientCollection GetCollection(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new DotVectorClientCollection(_inner, name);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsInner)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
