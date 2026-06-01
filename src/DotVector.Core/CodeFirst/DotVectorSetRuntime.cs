using DotVector.Api;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.CodeFirst;

internal abstract class DotVectorSetRuntime<TEntity>
    where TEntity : class
{
    public abstract string SetName { get; }

    public abstract IReadOnlyList<string> VectorFields { get; }

    public abstract void Insert(TEntity entity, string? vectorFieldName);

    public abstract IReadOnlyList<DotVectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int topK,
        string? vectorFieldName,
        Filter? filter);

    public abstract bool Delete(object key, string? vectorFieldName);

    public abstract long Count(string? vectorFieldName);
}

internal sealed class DotVectorSetRuntime<TEntity, TKey> : DotVectorSetRuntime<TEntity>
    where TEntity : class
    where TKey : notnull
{
    private readonly VectorDatabase _database;
    private readonly DotVectorEntitySchema<TEntity, TKey> _schema;
    private readonly Dictionary<string, Collection<TKey>> _collections;
    private readonly Dictionary<string, DotVectorVectorFieldMetadata> _vectors;
    private readonly Dictionary<string, string> _collectionNames;
    private readonly string[] _vectorFields;

    public DotVectorSetRuntime(
        VectorDatabase database,
        DotVectorEntitySchema<TEntity, TKey> schema,
        string setName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrEmpty(setName);
        _database = database;
        _schema = schema;
        SetName = setName;
        _collections = new Dictionary<string, Collection<TKey>>(StringComparer.Ordinal);
        _vectors = new Dictionary<string, DotVectorVectorFieldMetadata>(StringComparer.Ordinal);
        _collectionNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (DotVectorVectorFieldMetadata vector in schema.Vectors)
        {
            Collection<TKey> collection = EnsureCollection(vector);
            _vectors.Add(vector.Name, vector);
            _collections.Add(vector.Name, collection);
            _collectionNames.Add(vector.Name, schema.ResolveCollectionName(vector, setName));
        }

        _vectorFields = schema.Vectors.Select(static v => v.Name).ToArray();
    }

    public override string SetName { get; }

    public override IReadOnlyList<string> VectorFields => _vectorFields;

    public override void Insert(TEntity entity, string? vectorFieldName)
    {
        if (vectorFieldName is null)
        {
            IReadOnlyDictionary<string, object?>? payload = _schema.Accessors.PayloadGetter(entity);
            foreach (DotVectorVectorFieldMetadata vector in _schema.Vectors)
            {
                InsertOne(entity, vector, payload);
            }
            return;
        }

        DotVectorVectorFieldMetadata single = GetVector(vectorFieldName);
        InsertOne(entity, single, _schema.Accessors.PayloadGetter(entity));
    }

    public override IReadOnlyList<DotVectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int topK,
        string? vectorFieldName,
        Filter? filter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        DotVectorVectorFieldMetadata vector = _schema.GetVector(vectorFieldName);
        Collection<TKey> collection = _collections[vector.Name];
        IReadOnlyList<SearchResult<TKey>> hits = collection.Search(query, topK, filter);
        var results = new List<DotVectorSearchResult>(hits.Count);
        foreach (SearchResult<TKey> hit in hits)
        {
            results.Add(new DotVectorSearchResult(hit.Key, hit.Score, vector.Name)
            {
                Payload = hit.Payload,
            });
        }
        return results;
    }

    public override bool Delete(object key, string? vectorFieldName)
    {
        if (key is not TKey typedKey)
        {
            throw new ArgumentException(
                $"主键类型 {key.GetType().FullName} 与实体 {typeof(TEntity).FullName} 的 TKey={typeof(TKey).FullName} 不一致。",
                nameof(key));
        }

        if (vectorFieldName is null)
        {
            bool removed = false;
            foreach (Collection<TKey> collection in _collections.Values)
            {
                removed |= collection.Delete(typedKey);
            }
            return removed;
        }

        return _collections[GetVector(vectorFieldName).Name].Delete(typedKey);
    }

    public override long Count(string? vectorFieldName)
        => _collections[_schema.GetVector(vectorFieldName).Name].Count;

    private void InsertOne(
        TEntity entity,
        DotVectorVectorFieldMetadata vector,
        IReadOnlyDictionary<string, object?>? payload)
    {
        TKey key = _schema.Accessors.KeyGetter(entity);
        ReadOnlyMemory<float> memory = _schema.Accessors.GetVectorGetter(vector.Name)(entity);
        if (memory.Length != vector.Dimensions)
        {
            throw new ArgumentException(
                $"实体 {typeof(TEntity).FullName} 的向量字段 '{vector.Name}' 维度不匹配：期望 {vector.Dimensions}，实际 {memory.Length}。",
                nameof(entity));
        }

        float[] vectorArray = memory.ToArray();
        var record = new VectorRecord<TKey>(key, vectorArray);
        Dictionary<string, object>? payloadForRecord = NormalizePayload(payload);
        if (payloadForRecord is not null)
        {
            record = new VectorRecord<TKey>(key, vectorArray)
            {
                Payload = payloadForRecord,
            };
        }
        _collections[vector.Name].Insert(record);
    }

    private DotVectorVectorFieldMetadata GetVector(string vectorFieldName)
    {
        if (_vectors.TryGetValue(vectorFieldName, out DotVectorVectorFieldMetadata? vector))
        {
            return vector;
        }

        throw new KeyNotFoundException(
            $"实体 {typeof(TEntity).FullName} 未注册名为 '{vectorFieldName}' 的向量字段。");
    }

    private Collection<TKey> EnsureCollection(DotVectorVectorFieldMetadata vector)
    {
        string collectionName = _schema.ResolveCollectionName(vector, SetName);
        Collection<TKey> collection = _database.HasCollection(collectionName)
            ? _database.GetCollection<TKey>(collectionName)
            : CreateCollection(collectionName, vector);

        if (collection.Dimensions != vector.Dimensions
            || collection.Metric != vector.Metric
            || collection.IndexKind != vector.IndexKind)
        {
            throw new InvalidOperationException(
                $"集合 '{collectionName}' 的元数据与实体 {typeof(TEntity).FullName}.{vector.Name} 不一致。");
        }

        return collection;
    }

    private Collection<TKey> CreateCollection(string collectionName, DotVectorVectorFieldMetadata vector)
        => vector.IndexKind switch
        {
            IndexKind.Flat => _database.CreateCollection<TKey>(collectionName, vector.Dimensions, vector.Metric),
            IndexKind.Hnsw => _database.CreateCollection<TKey>(
                collectionName,
                vector.Dimensions,
                vector.Metric,
                IndexKind.Hnsw,
                vector.IndexOptions.ToHnswOptions()),
            IndexKind.IvfFlat => _database.CreateCollection<TKey>(
                collectionName,
                vector.Dimensions,
                vector.Metric,
                vector.IndexOptions.ToIvfOptions()),
            IndexKind.IvfPq => _database.CreateCollection<TKey>(
                collectionName,
                vector.Dimensions,
                vector.Metric,
                vector.IndexOptions.ToIvfPqOptions()),
            IndexKind.Vamana => _database.CreateCollection<TKey>(
                collectionName,
                vector.Dimensions,
                vector.Metric,
                vector.IndexOptions.ToVamanaOptions()),
            _ => throw new ArgumentOutOfRangeException(nameof(vector), vector.IndexKind, "不支持的索引类型。"),
        };

    private static Dictionary<string, object>? NormalizePayload(IReadOnlyDictionary<string, object?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(payload.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kv in payload)
        {
            if (kv.Value is not null)
            {
                result[kv.Key] = kv.Value;
            }
        }
        return result.Count == 0 ? null : result;
    }
}
