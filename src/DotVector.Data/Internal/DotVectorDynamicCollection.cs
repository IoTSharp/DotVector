using System.Globalization;
using System.Runtime.CompilerServices;
using DotVector.Core;
using DotVector.Core.Protocol;
using Microsoft.Extensions.VectorData;

namespace DotVector.Data.Internal;

/// <summary>
/// 基于 <see cref="VectorStoreCollectionDefinition"/> 的动态集合实现（M7.3）。
/// 记录类型为 <see cref="Dictionary{TKey, TValue}"/>（<see cref="string"/> →
/// <see cref="object"/>?），按定义中的属性名访问字段。
/// </summary>
/// <remarks>
/// <para>
/// 主键允许为任意 <see cref="object"/>，会通过
/// <see cref="Convert.ToString(object?, IFormatProvider?)"/>
/// 转换为协议层 ID 字符串；查询返回时同样以字符串形式回填到字典的 Key 字段。
/// </para>
/// <para>
/// LINQ Filter 翻译当前未实现：动态字典记录无强类型表达式语义。
/// TODO(M7+): 接入参数化或字符串 DSL 过滤。
/// </para>
/// </remarks>
internal sealed class DotVectorDynamicCollection
    : VectorStoreCollection<object, Dictionary<string, object?>>
{
    private readonly IDotVectorClient _client;
    private readonly VectorStoreCollectionMetadata _metadata;

    private readonly string _keyName;
    private readonly string _vectorName;
    // storage name -> dictionary key (definition property name)
    private readonly Dictionary<string, string> _storageToProperty;
    // dictionary key -> storage name
    private readonly Dictionary<string, string> _propertyToStorage;
    private readonly int _dimensions;
    private readonly string? _distanceFunction;

    public DotVectorDynamicCollection(
        IDotVectorClient client,
        string name,
        VectorStoreCollectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(definition);

        _client = client;
        Name = name;

        string? keyName = null;
        string? vectorName = null;
        int dims = 0;
        string? distance = null;
        var s2p = new Dictionary<string, string>(StringComparer.Ordinal);
        var p2s = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var p in definition.Properties)
        {
            switch (p)
            {
                case VectorStoreKeyProperty:
                    if (keyName is not null)
                    {
                        throw new InvalidOperationException(
                            "VectorStoreCollectionDefinition 中存在多个 Key 属性，DotVector.Data 仅支持单主键。");
                    }
                    keyName = p.Name;
                    break;

                case VectorStoreVectorProperty vp:
                    if (vectorName is not null)
                    {
                        throw new InvalidOperationException(
                            "VectorStoreCollectionDefinition 中存在多个 Vector 属性，DotVector.Data 仅支持单向量字段。");
                    }
                    vectorName = p.Name;
                    dims = vp.Dimensions;
                    distance = vp.DistanceFunction;
                    break;

                case VectorStoreDataProperty dp:
                    var storage = string.IsNullOrEmpty(dp.StorageName) ? dp.Name : dp.StorageName;
                    s2p[storage] = dp.Name;
                    p2s[dp.Name] = storage;
                    break;
            }
        }

        if (keyName is null)
        {
            throw new InvalidOperationException(
                "VectorStoreCollectionDefinition 必须包含一个 VectorStoreKeyProperty。");
        }
        if (vectorName is null)
        {
            throw new InvalidOperationException(
                "VectorStoreCollectionDefinition 必须包含一个 VectorStoreVectorProperty。");
        }

        _keyName = keyName;
        _vectorName = vectorName;
        _dimensions = dims;
        _distanceFunction = distance;
        _storageToProperty = s2p;
        _propertyToStorage = p2s;

        _metadata = new VectorStoreCollectionMetadata
        {
            VectorStoreSystemName = "dotvector",
            CollectionName = name,
        };
    }

    /// <inheritdoc/>
    public override string Name { get; }

    /// <inheritdoc/>
    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var infos = await _client.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < infos.Count; i++)
        {
            if (string.Equals(infos[i].Name, Name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var metric = DistanceFunctionMapper.ToDotVectorMetric(_distanceFunction);
        var req = new CreateCollectionRequest(Name, _dimensions, metric);
        await _client.CreateCollectionAsync(req, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        await _client.DeleteCollectionAsync(Name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task UpsertAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var dto = ToDto(record);
        await _client.UpsertAsync(Name, new[] { dto }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task UpsertAsync(IEnumerable<Dictionary<string, object?>> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        var list = new List<VectorUpsertRecord>();
        foreach (var r in records)
        {
            list.Add(ToDto(r));
        }
        if (list.Count == 0)
        {
            return;
        }
        await _client.UpsertAsync(Name, list, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(object key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _client.DeleteAsync(Name, new[] { ToProtocolId(key) }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(IEnumerable<object> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ids = new List<string>();
        foreach (var k in keys)
        {
            if (k is null)
            {
                continue;
            }
            ids.Add(ToProtocolId(k));
        }
        if (ids.Count == 0)
        {
            return;
        }
        await _client.DeleteAsync(Name, ids, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<Dictionary<string, object?>?> GetAsync(
        object key,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool includeVectors = options?.IncludeVectors == true;
        var dtos = await _client.GetAsync(Name, new[] { ToProtocolId(key) }, includeVectors, cancellationToken).ConfigureAwait(false);
        if (dtos.Count == 0)
        {
            return null;
        }
        return FromDto(dtos[0], includeVectors);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
        IEnumerable<object> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        bool includeVectors = options?.IncludeVectors == true;
        var ids = new List<string>();
        foreach (var k in keys)
        {
            if (k is null)
            {
                continue;
            }
            ids.Add(ToProtocolId(k));
        }
        if (ids.Count == 0)
        {
            yield break;
        }
        var dtos = await _client.GetAsync(Name, ids, includeVectors, cancellationToken).ConfigureAwait(false);
        foreach (var dto in dtos)
        {
            yield return FromDto(dto, includeVectors);
        }
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
        System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<Dictionary<string, object?>>? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "DotVectorDynamicCollection 不支持基于 LINQ 表达式的过滤；动态字典记录缺少强类型语义。" +
            " TODO(M7+): 接入字符串/参数化 DSL 过滤。");

    /// <inheritdoc/>
    public override async IAsyncEnumerable<VectorSearchResult<Dictionary<string, object?>>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<Dictionary<string, object?>>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);

        if (options?.Filter is not null)
        {
            throw new NotSupportedException(
                "DotVectorDynamicCollection 不支持基于 LINQ 表达式的 Filter；" +
                " TODO(M7+): 接入字符串/参数化 DSL 过滤。");
        }

        bool includeVectors = options?.IncludeVectors == true;
        var query = ExtractQueryVector(searchValue);
        var req = new VectorSearchRequest(query, top) { IncludeVector = includeVectors };
        var hits = await _client.SearchAsync(Name, req, cancellationToken).ConfigureAwait(false);
        foreach (var hit in hits)
        {
            var rec = FromDto(new VectorRecordDto(hit.Id) { Vector = hit.Vector, Payload = hit.Payload }, includeVectors);
            yield return new VectorSearchResult<Dictionary<string, object?>>(rec, hit.Score);
        }
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(VectorStoreCollectionMetadata))
        {
            return _metadata;
        }
        return null;
    }

    private VectorUpsertRecord ToDto(Dictionary<string, object?> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.TryGetValue(_keyName, out var keyObj) || keyObj is null)
        {
            throw new InvalidOperationException(
                $"动态记录缺少键字段 '{_keyName}'。");
        }
        var id = ToProtocolId(keyObj);

        if (!record.TryGetValue(_vectorName, out var vecObj) || vecObj is null)
        {
            throw new InvalidOperationException(
                $"动态记录缺少向量字段 '{_vectorName}'。");
        }
        var vec = ExtractQueryVector(vecObj);

        Dictionary<string, object>? payload = null;
        foreach (var (propName, storageName) in _propertyToStorage)
        {
            if (record.TryGetValue(propName, out var v) && v is not null)
            {
                payload ??= new Dictionary<string, object>(StringComparer.Ordinal);
                payload[storageName] = v;
            }
        }
        return new VectorUpsertRecord(id, vec) { Payload = payload };
    }

    private Dictionary<string, object?> FromDto(VectorRecordDto dto, bool includeVectors)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [_keyName] = dto.Id,
        };
        if (includeVectors && dto.Vector is { } v)
        {
            dict[_vectorName] = v;
        }
        if (dto.Payload is { } payload)
        {
            foreach (var (storageName, value) in payload)
            {
                if (_storageToProperty.TryGetValue(storageName, out var propName))
                {
                    dict[propName] = value;
                }
                else
                {
                    dict[storageName] = value;
                }
            }
        }
        return dict;
    }

    private static string ToProtocolId(object key)
    {
        return key switch
        {
            string s => s,
            Guid g => g.ToString("D", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(key, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("无法将动态记录的键转换为字符串 ID。"),
        };
    }

    private static float[] ExtractQueryVector(object value)
    {
        return value switch
        {
            float[] arr => arr,
            ReadOnlyMemory<float> rom => rom.ToArray(),
            Memory<float> m => m.ToArray(),
            IEnumerable<float> e => e.ToArray(),
            _ => throw new NotSupportedException(
                $"DotVector M7 仅支持 float[] / ReadOnlyMemory<float> 作为向量；收到 {value.GetType().Name}。"),
        };
    }
}
