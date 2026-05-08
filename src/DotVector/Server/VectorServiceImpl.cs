using System.Runtime.InteropServices;
using DotVector.Api;
using DotVector.Core.Protocol;
using DotVector.Grpc;
using DotVector.Query;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;

namespace DotVector.Server;

/// <summary>
/// gRPC <see cref="VectorService.VectorServiceBase"/> 实现，将 protobuf 消息转换为
/// <see cref="DotVector.Core.Protocol"/> DTO 并委托给本地 <see cref="LocalDotVectorClient"/>。
/// </summary>
internal sealed class VectorServiceImpl : VectorService.VectorServiceBase
{
    private readonly DotVectorDatabaseRegistry _registry;

    public VectorServiceImpl(DotVectorDatabaseRegistry registry)
    {
        _registry = registry;
    }

    public override async Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        bool ok = await GetClient(null).PingAsync(context.CancellationToken).ConfigureAwait(false);
        return new PingResponse { Ok = ok };
    }

    public override async Task<CreateCollectionResponse> CreateCollection(DotVector.Grpc.CreateCollectionRequest request, ServerCallContext context)
    {
        var dto = new DotVector.Core.Protocol.CreateCollectionRequest(request.Name, request.Dimensions, request.Metric);
        await GetClient(request.Selector).CreateCollectionAsync(dto, context.CancellationToken).ConfigureAwait(false);
        return new CreateCollectionResponse();
    }

    public override async Task<DeleteCollectionResponse> DeleteCollection(DeleteCollectionRequest request, ServerCallContext context)
    {
        await GetClient(request.Selector).DeleteCollectionAsync(request.Name, context.CancellationToken).ConfigureAwait(false);
        return new DeleteCollectionResponse();
    }

    public override async Task<ListCollectionsResponse> ListCollections(ListCollectionsRequest request, ServerCallContext context)
    {
        IReadOnlyList<DotVector.Core.Protocol.CollectionInfo> infos =
            await GetClient(request.Selector).ListCollectionsAsync(context.CancellationToken).ConfigureAwait(false);
        var resp = new ListCollectionsResponse();
        foreach (DotVector.Core.Protocol.CollectionInfo i in infos)
        {
            resp.Collections.Add(new DotVector.Grpc.CollectionInfo
            {
                Name = i.Name,
                Dimensions = i.Dimensions,
                Metric = i.Metric,
                RecordCount = i.RecordCount,
            });
        }
        return resp;
    }

    public override async Task<UpsertResponse> Upsert(UpsertRequest request, ServerCallContext context)
    {
        var list = new List<VectorUpsertRecord>(request.Records.Count);
        foreach (UpsertRecord r in request.Records)
        {
            var rec = new VectorUpsertRecord(r.Id, BytesToFloats(r.Vector))
            {
                Payload = MapFromProto(r.Payload),
            };
            list.Add(rec);
        }
        await GetClient(request.Selector).UpsertAsync(request.Collection, list, context.CancellationToken).ConfigureAwait(false);
        return new UpsertResponse { Count = list.Count };
    }

    public override async Task<DeleteResponse> Delete(DeleteRequest request, ServerCallContext context)
    {
        var ids = new List<string>(request.Ids);
        await GetClient(request.Selector).DeleteAsync(request.Collection, ids, context.CancellationToken).ConfigureAwait(false);
        return new DeleteResponse { Count = ids.Count };
    }

    public override async Task<SearchResponse> Search(SearchRequest request, ServerCallContext context)
    {
        var dto = new VectorSearchRequest(BytesToFloats(request.QueryVector), request.TopK)
        {
            IncludeVector = request.IncludeVector,
            Filter = request.Filter.IsEmpty ? null : FilterCodec.Decode(request.Filter.ToByteArray()),
        };
        IReadOnlyList<VectorSearchResult> hits =
            await GetClient(request.Selector).SearchAsync(request.Collection, dto, context.CancellationToken).ConfigureAwait(false);

        var resp = new SearchResponse();
        foreach (VectorSearchResult h in hits)
        {
            var sr = new ScoredRecord { Id = h.Id, Score = h.Score };
            if (h.Vector is not null) sr.Vector = FloatsToBytes(h.Vector);
            MapToProto(h.Payload, sr.Payload);
            resp.Hits.Add(sr);
        }
        return resp;
    }

    public override async Task<GetResponse> Get(GetRequest request, ServerCallContext context)
    {
        IReadOnlyList<VectorRecordDto> records =
            await GetClient(request.Selector).GetAsync(request.Collection, request.Ids, request.IncludeVector, context.CancellationToken)
                .ConfigureAwait(false);
        var resp = new GetResponse();
        foreach (VectorRecordDto r in records)
        {
            var pr = new DotVector.Grpc.VectorRecord { Id = r.Id };
            if (r.Vector is not null) pr.Vector = FloatsToBytes(r.Vector);
            MapToProto(r.Payload, pr.Payload);
            resp.Records.Add(pr);
        }
        return resp;
    }

    public override async Task<ScrollResponse> Scroll(ScrollRequest request, ServerCallContext context)
    {
        if (request.Filter.IsEmpty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Scroll 请求必须包含 filter 字段。"));
        }
        Filter filter = FilterCodec.Decode(request.Filter.ToByteArray());
        var dto = new VectorScrollRequest(filter, request.Top) { IncludeVector = request.IncludeVector };
        IReadOnlyList<VectorRecordDto> records =
            await GetClient(request.Selector).ScrollAsync(request.Collection, dto, context.CancellationToken).ConfigureAwait(false);
        var resp = new ScrollResponse();
        foreach (VectorRecordDto r in records)
        {
            var pr = new DotVector.Grpc.VectorRecord { Id = r.Id };
            if (r.Vector is not null) pr.Vector = FloatsToBytes(r.Vector);
            MapToProto(r.Payload, pr.Payload);
            resp.Records.Add(pr);
        }
        return resp;
    }

    // ---- 转换辅助 ----

    private LocalDotVectorClient GetClient(DatabaseSelector? selector)
        => _registry.GetClient(selector?.Database);

    internal static float[] BytesToFloats(ByteString bytes)
    {
        ReadOnlySpan<byte> src = bytes.Span;
        if ((src.Length & 3) != 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "向量字节长度必须是 4 的倍数 (float32)。"));
        }
        var floats = new float[src.Length / 4];
        Span<byte> dst = MemoryMarshal.AsBytes(floats.AsSpan());
        src.CopyTo(dst);
        return floats;
    }

    internal static ByteString FloatsToBytes(float[] floats)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(floats.AsSpan());
        return ByteString.CopyFrom(bytes);
    }

    private static IReadOnlyDictionary<string, object>? MapFromProto(MapField<string, ScalarValue> map)
    {
        if (map.Count == 0) return null;
        var d = new Dictionary<string, object>(map.Count, StringComparer.Ordinal);
        foreach (var kv in map)
        {
            object? boxed = ScalarToObject(kv.Value);
            if (boxed is not null) d[kv.Key] = boxed;
        }
        return d.Count == 0 ? null : d;
    }

    internal static void MapToProto(IReadOnlyDictionary<string, object>? source, MapField<string, ScalarValue> target)
    {
        if (source is null) return;
        foreach (var kv in source)
        {
            ScalarValue? sv = ObjectToScalar(kv.Value);
            if (sv is not null) target[kv.Key] = sv;
        }
    }

    internal static ScalarValue? ObjectToScalar(object value) => value switch
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

    internal static object? ScalarToObject(ScalarValue v) => v.ValueCase switch
    {
        ScalarValue.ValueOneofCase.BoolValue => v.BoolValue,
        ScalarValue.ValueOneofCase.IntValue => v.IntValue,
        ScalarValue.ValueOneofCase.DoubleValue => v.DoubleValue,
        ScalarValue.ValueOneofCase.StringValue => v.StringValue,
        _ => null,
    };
}
