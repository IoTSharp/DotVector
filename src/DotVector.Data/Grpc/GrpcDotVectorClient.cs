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
    private readonly GrpcChannel _channel;
    private readonly bool _ownsChannel;
    private readonly VectorService.VectorServiceClient _client;

    /// <summary>
    /// 通过远端服务地址（例如 <c>http://localhost:5180</c>）构建客户端。
    /// </summary>
    /// <param name="address">gRPC 服务端地址。</param>
    /// <param name="options">可选的 <see cref="GrpcChannelOptions"/>。</param>
    public GrpcDotVectorClient(Uri address, GrpcChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        _channel = options is null ? GrpcChannel.ForAddress(address) : GrpcChannel.ForAddress(address, options);
        _ownsChannel = true;
        _client = new VectorService.VectorServiceClient(_channel);
    }

    /// <summary>
    /// 使用调用者构造的 <see cref="GrpcChannel"/>（不被本类型释放）。
    /// </summary>
    public GrpcDotVectorClient(GrpcChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        _ownsChannel = false;
        _client = new VectorService.VectorServiceClient(_channel);
    }

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
            }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        await _client.DeleteCollectionAsync(new DeleteCollectionRequest { Name = collectionName }, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DotVector.Core.Protocol.CollectionInfo>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        ListCollectionsResponse resp = await _client.ListCollectionsAsync(new ListCollectionsRequest(), cancellationToken: cancellationToken);
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
        var req = new UpsertRequest { Collection = collectionName };
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
        var req = new DeleteRequest { Collection = collectionName };
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
        var req = new GetRequest { Collection = collectionName, IncludeVector = includeVector };
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
