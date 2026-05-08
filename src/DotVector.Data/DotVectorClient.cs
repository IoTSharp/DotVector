using System.Net;
using System.Net.Http;
using DotVector.Api;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Data.Grpc;
using Grpc.Net.Client;

namespace DotVector.Data;

/// <summary>
/// DotVector 的高层客户端门面。封装协议级 <see cref="IDotVectorClient"/>，
/// 提供 Qdrant / Pinecone / Chroma 风格的简洁 API，便于从其它向量数据库切换。
/// </summary>
/// <remarks>
/// <para>
/// 同时支持两套传输：
/// <list type="bullet">
///   <item><description><b>远程 gRPC</b>：<see cref="Connect(string)"/> /
///   <see cref="Connect(string, DotVectorClientOptions)"/></description></item>
///   <item><description><b>嵌入式（进程内）</b>：<see cref="Embedded(string)"/></description></item>
/// </list>
/// 二者通过同一个 <see cref="IDotVectorClient"/> 协议接口对接，所有上层 API 完全一致。
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
    // 工厂：远程 gRPC
    // -------------------------------------------------------------------------

    /// <summary>
    /// 通过 gRPC 连接远端 DotVector 服务器。
    /// </summary>
    /// <param name="endpoint">服务器地址，例如 <c>http://localhost:5180</c>。</param>
    public static DotVectorClient Connect(string endpoint)
        => Connect(endpoint, new DotVectorClientOptions());

    /// <summary>
    /// 通过 gRPC 连接远端 DotVector 服务器，并指定连接选项。
    /// </summary>
    /// <param name="endpoint">服务器地址，例如 <c>http://localhost:5180</c>。</param>
    /// <param name="options">连接选项（数据库 / 超时 / Handler 等）。</param>
    public static DotVectorClient Connect(string endpoint, DotVectorClientOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentNullException.ThrowIfNull(options);

        HttpMessageHandler handler = options.HttpHandler ?? new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            UseProxy = options.UseProxy,
            Proxy = null,
        };

        var channelOptions = new GrpcChannelOptions
        {
            HttpHandler = handler,
        };

        var channel = GrpcChannel.ForAddress(new Uri(endpoint), channelOptions);
        var grpc = new GrpcDotVectorClient(channel, options.Database);
        return new DotVectorClient(grpc, ownsInner: true);
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

    /// <summary>检查与服务端的连接是否正常。</summary>
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
