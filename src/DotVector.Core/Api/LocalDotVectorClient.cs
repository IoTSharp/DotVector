using System.Globalization;
using DotVector.Api;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.IO;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Api;

/// <summary>
/// 直接包装 <see cref="VectorDatabase"/> 的进程内 <see cref="IDotVectorClient"/> 实现。
/// </summary>
/// <remarks>
/// 让宿主进程在不经过任何网络序列化的前提下使用 VectorData 适配层。
/// 通过运行时反射检测每个集合的 <c>TKey</c>，把字符串 ID 解析为 int / long / Guid / string 后分发到
/// <see cref="Collection{TKey}"/> 的强类型 API。
/// </remarks>
public sealed class LocalDotVectorClient : IDotVectorClient
{
    private readonly VectorDatabase _database;
    private readonly bool _ownsDatabase;

    /// <summary>
    /// 包装一个外部传入的 <see cref="VectorDatabase"/> 实例。
    /// </summary>
    /// <param name="database">已打开的向量数据库实例。</param>
    /// <param name="ownsDatabase">为 <see langword="true"/> 时由本客户端 <see cref="DisposeAsync"/> 时一并释放。</param>
    public LocalDotVectorClient(VectorDatabase database, bool ownsDatabase = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _ownsDatabase = ownsDatabase;
    }

    /// <summary>
    /// 打开指定 <c>.dvec/</c> 目录，并由本客户端持有该数据库实例。
    /// </summary>
    /// <param name="directoryPath">数据库目录路径。</param>
    public LocalDotVectorClient(string directoryPath)
        : this(new VectorDatabase(directoryPath), ownsDatabase: true)
    {
    }

    /// <summary>暴露被包装的 <see cref="VectorDatabase"/>，便于嵌入式宿主直接调用强类型 API。</summary>
    public VectorDatabase Database => _database;

    /// <inheritdoc/>
    public ValueTask<bool> PingAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    /// <inheritdoc/>
    public ValueTask CreateCollectionAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Metric metric = ParseMetric(request.Metric);
        // 协议层默认主键类型 = string；若要使用整型/Guid 主键，请直接通过
        // <see cref="Database"/>.CreateCollection<TKey>(...) 创建集合后再用本客户端访问。
        _database.CreateCollection<string>(request.Name, request.Dimensions, metric);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        _database.DropCollection(collectionName);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IDisposable> snapshot = _database.EnumerateCollections();
        var infos = new List<CollectionInfo>(snapshot.Count);
        foreach (IDisposable c in snapshot)
        {
            CollectionMeta m = ReadMeta(c);
            infos.Add(new CollectionInfo(m.Name, m.Dimensions, MetricToString(m.Metric), m.Count));
        }
        return ValueTask.FromResult<IReadOnlyList<CollectionInfo>>(infos);
    }

    /// <inheritdoc/>
    public ValueTask UpsertAsync(string collectionName, IReadOnlyList<VectorUpsertRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return ValueTask.CompletedTask;

        IDisposable col = GetCollectionOrThrow(collectionName);
        switch (DetectKeyType(col))
        {
            case KeyTypeCode.Int32:
                UpsertTyped<int>(col, records, ParseInt);
                break;
            case KeyTypeCode.Int64:
                UpsertTyped<long>(col, records, ParseLong);
                break;
            case KeyTypeCode.Guid:
                UpsertTyped<Guid>(col, records, ParseGuid);
                break;
            case KeyTypeCode.String:
                UpsertTyped<string>(col, records, static id => id);
                break;
            default:
                throw new InvalidOperationException($"集合 '{collectionName}' 使用了不支持的主键类型。");
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(string collectionName, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return ValueTask.CompletedTask;

        IDisposable col = GetCollectionOrThrow(collectionName);
        switch (DetectKeyType(col))
        {
            case KeyTypeCode.Int32:
                DeleteTyped<int>(col, ids, ParseInt);
                break;
            case KeyTypeCode.Int64:
                DeleteTyped<long>(col, ids, ParseLong);
                break;
            case KeyTypeCode.Guid:
                DeleteTyped<Guid>(col, ids, ParseGuid);
                break;
            case KeyTypeCode.String:
                DeleteTyped<string>(col, ids, static id => id);
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<VectorSearchResult>> SearchAsync(string collectionName, VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(request);

        IDisposable col = GetCollectionOrThrow(collectionName);
        IReadOnlyList<VectorSearchResult> result = DetectKeyType(col) switch
        {
            KeyTypeCode.Int32 => SearchTyped<int>(col, request),
            KeyTypeCode.Int64 => SearchTyped<long>(col, request),
            KeyTypeCode.Guid => SearchTyped<Guid>(col, request),
            KeyTypeCode.String => SearchTyped<string>(col, request),
            _ => Array.Empty<VectorSearchResult>(),
        };
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<VectorRecordDto>> GetAsync(
        string collectionName,
        IReadOnlyList<string> ids,
        bool includeVector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return ValueTask.FromResult<IReadOnlyList<VectorRecordDto>>(Array.Empty<VectorRecordDto>());

        IDisposable col = GetCollectionOrThrow(collectionName);
        IReadOnlyList<VectorRecordDto> result = DetectKeyType(col) switch
        {
            KeyTypeCode.Int32 => GetTyped<int>(col, ids, includeVector, ParseInt),
            KeyTypeCode.Int64 => GetTyped<long>(col, ids, includeVector, ParseLong),
            KeyTypeCode.Guid => GetTyped<Guid>(col, ids, includeVector, ParseGuid),
            KeyTypeCode.String => GetTyped<string>(col, ids, includeVector, static id => id),
            _ => Array.Empty<VectorRecordDto>(),
        };
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<VectorRecordDto>> ScrollAsync(
        string collectionName,
        VectorScrollRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(request);

        IDisposable col = GetCollectionOrThrow(collectionName);
        IReadOnlyList<VectorRecordDto> result = DetectKeyType(col) switch
        {
            KeyTypeCode.Int32 => ScrollTyped<int>(col, request),
            KeyTypeCode.Int64 => ScrollTyped<long>(col, request),
            KeyTypeCode.Guid => ScrollTyped<Guid>(col, request),
            KeyTypeCode.String => ScrollTyped<string>(col, request),
            _ => Array.Empty<VectorRecordDto>(),
        };
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_ownsDatabase)
        {
            _database.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    // ---- 类型分派辅助 ----

    private IDisposable GetCollectionOrThrow(string name)
    {
        if (!_database.TryGetUntyped(name, out IDisposable col))
        {
            throw new KeyNotFoundException($"集合 '{name}' 不存在。");
        }
        return col;
    }

    private static KeyTypeCode DetectKeyType(IDisposable collection)
    {
        Type t = collection.GetType();
        if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(Collection<>))
        {
            throw new InvalidOperationException($"对象 {t.FullName} 不是 Collection<TKey>。");
        }
        Type key = t.GetGenericArguments()[0];
        if (key == typeof(int)) return KeyTypeCode.Int32;
        if (key == typeof(long)) return KeyTypeCode.Int64;
        if (key == typeof(Guid)) return KeyTypeCode.Guid;
        if (key == typeof(string)) return KeyTypeCode.String;
        throw new InvalidOperationException($"不支持的主键类型 {key.FullName}。");
    }

    private readonly record struct CollectionMeta(string Name, int Dimensions, Metric Metric, long Count);

    private static CollectionMeta ReadMeta(IDisposable col) => DetectKeyType(col) switch
    {
        KeyTypeCode.Int32 => MetaOf<int>(col),
        KeyTypeCode.Int64 => MetaOf<long>(col),
        KeyTypeCode.Guid => MetaOf<Guid>(col),
        KeyTypeCode.String => MetaOf<string>(col),
        _ => throw new InvalidOperationException(),
    };

    private static CollectionMeta MetaOf<TKey>(IDisposable col) where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        return new CollectionMeta(c.Name, c.Dimensions, c.Metric, c.Count);
    }

    // ---- typed implementations ----

    private static void UpsertTyped<TKey>(IDisposable col, IReadOnlyList<VectorUpsertRecord> records, Func<string, TKey> parse)
        where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        var batch = new List<VectorRecord<TKey>>(records.Count);
        foreach (VectorUpsertRecord r in records)
        {
            TKey key = parse(r.Id);
            var rec = new VectorRecord<TKey>(key, r.Vector);
            if (r.Payload is { Count: > 0 })
            {
                var dict = new Dictionary<string, object>(r.Payload.Count, StringComparer.Ordinal);
                foreach (var kv in r.Payload)
                {
                    dict[kv.Key] = kv.Value;
                }
                rec = new VectorRecord<TKey>(key, r.Vector) { Payload = dict };
            }
            batch.Add(rec);
        }
        c.InsertBatch(batch);
    }

    private static void DeleteTyped<TKey>(IDisposable col, IReadOnlyList<string> ids, Func<string, TKey> parse)
        where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        foreach (string id in ids)
        {
            c.Delete(parse(id));
        }
    }

    private static IReadOnlyList<VectorSearchResult> SearchTyped<TKey>(IDisposable col, VectorSearchRequest request)
        where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        IReadOnlyList<SearchResult<TKey>> hits = c.Search(request.QueryVector, request.TopK, request.Filter);
        var result = new List<VectorSearchResult>(hits.Count);
        foreach (SearchResult<TKey> hit in hits)
        {
            string id = FormatKey(hit.Key);
            var dto = new VectorSearchResult(id, hit.Score)
            {
                Payload = NonNullPayload(hit.Payload),
            };
            if (request.IncludeVector && c.TryGet(hit.Key, out VectorRecord<TKey>? full) && full is not null)
            {
                dto = new VectorSearchResult(id, hit.Score)
                {
                    Payload = dto.Payload,
                    Vector = full.Vector,
                };
            }
            result.Add(dto);
        }
        return result;
    }

    private static IReadOnlyList<VectorRecordDto> GetTyped<TKey>(
        IDisposable col,
        IReadOnlyList<string> ids,
        bool includeVector,
        Func<string, TKey> parse) where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        var keys = new TKey[ids.Count];
        for (int i = 0; i < ids.Count; i++) keys[i] = parse(ids[i]);
        IReadOnlyList<VectorRecord<TKey>> hits = c.GetMany(keys, includeVector);
        var result = new List<VectorRecordDto>(hits.Count);
        foreach (VectorRecord<TKey> rec in hits)
        {
            var dto = new VectorRecordDto(FormatKey(rec.Key))
            {
                Vector = includeVector ? rec.Vector : null,
                Payload = rec.Payload is null ? null : (IReadOnlyDictionary<string, object>)rec.Payload,
            };
            result.Add(dto);
        }
        return result;
    }

    private static IReadOnlyList<VectorRecordDto> ScrollTyped<TKey>(IDisposable col, VectorScrollRequest request)
        where TKey : notnull
    {
        var c = (Collection<TKey>)col;
        // 使用 Search 路径下推过滤是不可行的（需要全量扫描）。
        // M9 简化实现：用一个零向量做 Search(topK=request.Top, filter)，对 Cosine/InnerProduct 而言
        // 顺序无意义但满足 "按存储顺序前 N 条带过滤" 的语义；
        // 对持久化集合（FlatIndex）会过取所有候选，足以覆盖 M9 的 Scroll 需求。
        float[] zero = new float[c.Dimensions];
        IReadOnlyList<SearchResult<TKey>> hits = c.Search(zero, request.Top, request.Filter);
        var result = new List<VectorRecordDto>(hits.Count);
        foreach (SearchResult<TKey> hit in hits)
        {
            float[]? vec = null;
            if (request.IncludeVector && c.TryGet(hit.Key, out VectorRecord<TKey>? full) && full is not null)
            {
                vec = full.Vector;
            }
            result.Add(new VectorRecordDto(FormatKey(hit.Key))
            {
                Vector = vec,
                Payload = NonNullPayload(hit.Payload),
            });
        }
        return result;
    }

    // ---- value helpers ----

    private static int ParseInt(string id) => int.Parse(id, CultureInfo.InvariantCulture);
    private static long ParseLong(string id) => long.Parse(id, CultureInfo.InvariantCulture);
    private static Guid ParseGuid(string id) => Guid.Parse(id, CultureInfo.InvariantCulture);

    private static string FormatKey<TKey>(TKey key) where TKey : notnull
        => key switch
        {
            string s => s,
            Guid g => g.ToString("D", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => key.ToString() ?? string.Empty,
        };

    private static IReadOnlyDictionary<string, object>? NonNullPayload(IReadOnlyDictionary<string, object?>? payload)
    {
        if (payload is null || payload.Count == 0) return null;
        var d = new Dictionary<string, object>(payload.Count, StringComparer.Ordinal);
        foreach (var kv in payload)
        {
            if (kv.Value is not null) d[kv.Key] = kv.Value;
        }
        return d.Count == 0 ? null : d;
    }

    private static Metric ParseMetric(string metric)
    {
        if (Enum.TryParse(metric, ignoreCase: true, out Metric m)) return m;
        throw new ArgumentException($"未知的距离度量字符串：'{metric}'。", nameof(metric));
    }

    private static string MetricToString(Metric metric) => metric.ToString();
}
