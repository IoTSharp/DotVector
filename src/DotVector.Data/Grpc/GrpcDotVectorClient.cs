using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Grpc;
using DotVector.Query;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Net.Client;

namespace DotVector.Data.Grpc;

/// <summary>
/// 通过 gRPC 通道访问远端 DotVector 服务的 <see cref="IDotVectorClient"/> 实现。
/// </summary>
/// <remarks>
/// 本类型位于客户端 SDK，<b>禁止</b>直接引用 <c>DotVector</c>（服务端壳）程序集。
/// 协议契约通过共享 <c>protos/dotvector.proto</c> 在客户端 / 服务端各自生成。
/// </remarks>
public sealed class GrpcDotVectorClient : IDotVectorClient
{
#pragma warning disable CA2255
    /// <summary>
    /// 模块初始化：尽早开启明文 HTTP/2（h2c）支持开关。
    /// </summary>
    /// <remarks>
    /// SocketsHttpHandler 在首次实例化时缓存
    /// <c>System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport</c> 开关的值。
    /// 即使在 .NET 10 上，<see cref="HttpVersionPolicy.RequestVersionExact"/> + http:// 仍会
    /// 因该开关为 <c>false</c> 而抛出 "unable to establish HTTP/2 connection"。本类型作为
    /// gRPC 客户端 SDK 的入口，使用 <c>ModuleInitializer</c> 在程序集加载时即开启，
    /// 早于任何 HttpClient / GrpcChannel 创建。CA2255 仅是一般性建议，本场景必要且可控。
    /// </remarks>
    [ModuleInitializer]
    internal static void EnableHttp2Cleartext()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }
#pragma warning restore CA2255

    private readonly GrpcChannel _channel;
    private readonly bool _ownsChannel;
    private readonly VectorService.VectorServiceClient _client;
    private readonly string? _databaseName;

    /// <summary>
    /// 通过远端服务地址（例如 <c>http://localhost:5180</c>）构建客户端。
    /// </summary>
    /// <param name="address">gRPC 服务端地址。</param>
    /// <param name="options">可选的 <see cref="GrpcChannelOptions"/>。</param>
    public GrpcDotVectorClient(Uri address, GrpcChannelOptions? options = null)
        : this(address, databaseName: null, options)
    {
    }

    /// <summary>
    /// 通过远端服务地址与数据库名称构建客户端。
    /// </summary>
    /// <param name="address">gRPC 服务端地址。</param>
    /// <param name="databaseName">服务端数据库名称；当前协议暂未传输该值，保留用于后续多数据库扩展。</param>
    /// <param name="options">可选的 <see cref="GrpcChannelOptions"/>。</param>
    public GrpcDotVectorClient(Uri address, string? databaseName, GrpcChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (options is null)
        {
            // 显式构造 SocketsHttpHandler：
            // 1. EnableMultipleHttp2Connections 提升并发；
            // 2. 关闭代理 —— 开发机/CI 的系统代理（HTTP_PROXY、HTTPS_PROXY 或 PAC）
            //    常会把本地 loopback 请求也劫持，导致 gRPC 子通道连接失败
            //    （表现为 "Unable to get subchannel from HttpRequestMessage"）。
            //    DotVector 的 gRPC 流量通常是点对点（本机或同 VPC），不需要代理。
            options = new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    UseProxy = false,
                    Proxy = null,
                },
            };
        }

        _channel = GrpcChannel.ForAddress(address, options);
        _ownsChannel = true;
        _client = new VectorService.VectorServiceClient(_channel);
        _databaseName = NormalizeDatabaseName(databaseName);
    }

    /// <summary>
    /// 使用调用者构造的 <see cref="GrpcChannel"/>（不被本类型释放）。
    /// </summary>
    public GrpcDotVectorClient(GrpcChannel channel)
        : this(channel, databaseName: null)
    {
    }

    /// <summary>
    /// 使用调用者构造的 <see cref="GrpcChannel"/> 与数据库名称（通道不被本类型释放）。
    /// </summary>
    /// <param name="channel">调用者持有的 gRPC 通道。</param>
    /// <param name="databaseName">服务端数据库名称；当前协议暂未传输该值，保留用于后续多数据库扩展。</param>
    public GrpcDotVectorClient(GrpcChannel channel, string? databaseName)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        _ownsChannel = false;
        _client = new VectorService.VectorServiceClient(_channel);
        _databaseName = NormalizeDatabaseName(databaseName);
    }

    /// <summary>
    /// 返回此客户端绑定的服务端数据库名称。为空表示服务端默认数据库。
    /// </summary>
    public string? DatabaseName => _databaseName;

    /// <inheritdoc />
    public async ValueTask<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        PingResponse resp = await _client.PingAsync(new PingRequest(), cancellationToken: cancellationToken);
        return resp.Ok;
    }

    /// <inheritdoc />
    public async ValueTask CreateCollectionAsync(DotVector.Core.Protocol.CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _client.CreateCollectionAsync(
            new DotVector.Grpc.CreateCollectionRequest
            {
                Name = request.Name,
                Dimensions = request.Dimensions,
                Metric = request.Metric,
                Selector = BuildSelector(),
            }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        await _client.DeleteCollectionAsync(
            new DeleteCollectionRequest { Name = collectionName, Selector = BuildSelector() },
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DotVector.Core.Protocol.CollectionInfo>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        ListCollectionsResponse resp = await _client.ListCollectionsAsync(
            new ListCollectionsRequest { Selector = BuildSelector() },
            cancellationToken: cancellationToken);
        var list = new List<DotVector.Core.Protocol.CollectionInfo>(resp.Collections.Count);
        foreach (DotVector.Grpc.CollectionInfo c in resp.Collections)
        {
            list.Add(new DotVector.Core.Protocol.CollectionInfo(c.Name, c.Dimensions, c.Metric, c.RecordCount));
        }
        return list;
    }

    /// <inheritdoc />
    public async ValueTask UpsertAsync(string collectionName, IReadOnlyList<VectorUpsertRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(records);
        var req = new UpsertRequest { Collection = collectionName, Selector = BuildSelector() };
        foreach (VectorUpsertRecord r in records)
        {
            var ur = new UpsertRecord { Id = r.Id, Vector = FloatsToBytes(r.Vector) };
            WritePayload(r.Payload, ur.Payload);
            req.Records.Add(ur);
        }
        await _client.UpsertAsync(req, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string collectionName, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(ids);
        var req = new DeleteRequest { Collection = collectionName, Selector = BuildSelector() };
        req.Ids.AddRange(ids);
        await _client.DeleteAsync(req, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VectorSearchResult>> SearchAsync(string collectionName, VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(request);
        var req = new SearchRequest
        {
            Collection = collectionName,
            QueryVector = FloatsToBytes(request.QueryVector),
            TopK = request.TopK,
            IncludeVector = request.IncludeVector,
            Filter = request.Filter is null ? ByteString.Empty : ByteString.CopyFrom(FilterCodec.Encode(request.Filter)),
            Selector = BuildSelector(),
        };
        SearchResponse resp = await _client.SearchAsync(req, cancellationToken: cancellationToken);
        var list = new List<VectorSearchResult>(resp.Hits.Count);
        foreach (ScoredRecord h in resp.Hits)
        {
            var r = new VectorSearchResult(h.Id, h.Score)
            {
                Vector = h.Vector.IsEmpty ? null : BytesToFloats(h.Vector),
                Payload = ReadPayload(h.Payload),
            };
            list.Add(r);
        }
        return list;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VectorRecordDto>> GetAsync(string collectionName, IReadOnlyList<string> ids, bool includeVector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(ids);
        var req = new GetRequest { Collection = collectionName, IncludeVector = includeVector, Selector = BuildSelector() };
        req.Ids.AddRange(ids);
        GetResponse resp = await _client.GetAsync(req, cancellationToken: cancellationToken);
        return MapRecords(resp.Records);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VectorRecordDto>> ScrollAsync(string collectionName, VectorScrollRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(request);
        var req = new ScrollRequest
        {
            Collection = collectionName,
            Top = request.Top,
            IncludeVector = request.IncludeVector,
            Filter = ByteString.CopyFrom(FilterCodec.Encode(request.Filter)),
            Selector = BuildSelector(),
        };
        ScrollResponse resp = await _client.ScrollAsync(req, cancellationToken: cancellationToken);
        return MapRecords(resp.Records);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsChannel) _channel.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---- helpers ----

    private DatabaseSelector BuildSelector()
        => new() { Database = _databaseName ?? string.Empty };

    private static string? NormalizeDatabaseName(string? databaseName)
        => string.IsNullOrWhiteSpace(databaseName) ? null : databaseName.Trim();

    private static IReadOnlyList<VectorRecordDto> MapRecords(RepeatedField<DotVector.Grpc.VectorRecord> records)
    {
        var list = new List<VectorRecordDto>(records.Count);
        foreach (DotVector.Grpc.VectorRecord r in records)
        {
            list.Add(new VectorRecordDto(r.Id)
            {
                Vector = r.Vector.IsEmpty ? null : BytesToFloats(r.Vector),
                Payload = ReadPayload(r.Payload),
            });
        }
        return list;
    }

    private static ByteString FloatsToBytes(float[] floats)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(floats.AsSpan());
        return ByteString.CopyFrom(bytes);
    }

    private static float[] BytesToFloats(ByteString bytes)
    {
        ReadOnlySpan<byte> src = bytes.Span;
        var floats = new float[src.Length / 4];
        Span<byte> dst = MemoryMarshal.AsBytes(floats.AsSpan());
        src.CopyTo(dst);
        return floats;
    }

    private static void WritePayload(IReadOnlyDictionary<string, object>? source, MapField<string, ScalarValue> target)
    {
        if (source is null) return;
        foreach (var kv in source)
        {
            ScalarValue? sv = kv.Value switch
            {
                bool b => new ScalarValue { BoolValue = b },
                sbyte i => new ScalarValue { IntValue = i },
                byte i => new ScalarValue { IntValue = i },
                short i => new ScalarValue { IntValue = i },
                ushort i => new ScalarValue { IntValue = i },
                int i => new ScalarValue { IntValue = i },
                uint i => new ScalarValue { IntValue = i },
                long i => new ScalarValue { IntValue = i },
                float f => new ScalarValue { DoubleValue = f },
                double d => new ScalarValue { DoubleValue = d },
                string s => new ScalarValue { StringValue = s },
                _ => null,
            };
            if (sv is not null) target[kv.Key] = sv;
        }
    }

    private static IReadOnlyDictionary<string, object>? ReadPayload(MapField<string, ScalarValue> map)
    {
        if (map.Count == 0) return null;
        var d = new Dictionary<string, object>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
        {
            object? v = kv.Value.ValueCase switch
            {
                ScalarValue.ValueOneofCase.BoolValue => kv.Value.BoolValue,
                ScalarValue.ValueOneofCase.IntValue => kv.Value.IntValue,
                ScalarValue.ValueOneofCase.DoubleValue => kv.Value.DoubleValue,
                ScalarValue.ValueOneofCase.StringValue => (object)kv.Value.StringValue,
                _ => null,
            };
            if (v is not null) d[kv.Key] = v;
        }
        return d.Count == 0 ? null : d;
    }
}
