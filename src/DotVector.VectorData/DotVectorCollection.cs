using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.VectorData.Internal;
using Microsoft.Extensions.VectorData;

namespace DotVector.VectorData;

/// <summary>
/// DotVector 对 <see cref="VectorStoreCollection{TKey, TRecord}"/> 的实现。
/// 通过 <see cref="IDotVectorClient"/> 与本地嵌入式数据库通信。
/// </summary>
/// <typeparam name="TKey">主键类型，需为 <see cref="string"/> / <see cref="int"/> /
/// <see cref="long"/> / <see cref="Guid"/> 之一。</typeparam>
/// <typeparam name="TRecord">用户记录类型，需有公共无参构造器，
/// 并通过 <see cref="VectorStoreKeyAttribute"/> / <see cref="VectorStoreVectorAttribute"/> /
/// <see cref="VectorStoreDataAttribute"/> 标注字段。</typeparam>
/// <remarks>
/// <para>
/// <strong>M7.2 状态</strong>：已支持 <c>GetAsync(key)</c> / <c>GetAsync(keys)</c>、
/// <see cref="VectorSearchOptions{TRecord}.IncludeVectors"/> 与
/// LINQ Filter Expression 翻译（包括 <see cref="VectorSearchOptions{TRecord}.Filter"/>
/// 与 <c>GetAsync(filter, top, ...)</c>）。
/// </para>
/// <para>翻译规则与限制详见 <see cref="Internal.LinqFilterTranslator"/>。</para>
/// </remarks>
[RequiresUnreferencedCode("DotVectorCollection 通过反射访问 TRecord 的属性，可能被 trim 移除。")]
[RequiresDynamicCode("DotVectorCollection 通过反射访问 TRecord 的属性，AOT 下可能不可用。")]
public sealed class DotVectorCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly IDotVectorClient _client;
    private readonly DotVectorRecordMapper<TKey, TRecord> _mapper;
    private readonly VectorStoreCollectionMetadata _metadata;

    /// <summary>
    /// 初始化 <see cref="DotVectorCollection{TKey, TRecord}"/>。
    /// </summary>
    /// <param name="client">DotVector 协议客户端（不会被本类型 dispose）。</param>
    /// <param name="name">集合名称。</param>
    public DotVectorCollection(IDotVectorClient client, string name)
        : this(client, name, definition: null)
    {
    }

    /// <summary>
    /// 初始化 <see cref="DotVectorCollection{TKey, TRecord}"/>，可选地接受显式集合定义（M7.3）。
    /// </summary>
    /// <param name="client">DotVector 协议客户端（不会被本类型 dispose）。</param>
    /// <param name="name">集合名称。</param>
    /// <param name="definition">可选的 <see cref="VectorStoreCollectionDefinition"/>。
    /// 提供时按定义中的属性名映射 <typeparamref name="TRecord"/>，忽略 attribute 标注；
    /// 为 <see langword="null"/> 时回退到基于 attribute 的反射映射。</param>
    public DotVectorCollection(IDotVectorClient client, string name, VectorStoreCollectionDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _client = client;
        Name = name;
        _mapper = definition is null
            ? new DotVectorRecordMapper<TKey, TRecord>()
            : new DotVectorRecordMapper<TKey, TRecord>(definition);
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
        var metric = DistanceFunctionMapper.ToDotVectorMetric(_mapper.DistanceFunction);
        var req = new CreateCollectionRequest(Name, _mapper.Dimensions, metric);
        await _client.CreateCollectionAsync(req, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        await _client.DeleteCollectionAsync(Name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var dto = ToDto(record);
        await _client.UpsertAsync(Name, new[] { dto }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
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
    public override async Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var ids = new[] { KeyConverter<TKey>.ToProtocolId(key) };
        await _client.DeleteAsync(Name, ids, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ids = new List<string>();
        foreach (var k in keys)
        {
            if (k is null)
            {
                continue;
            }
            ids.Add(KeyConverter<TKey>.ToProtocolId(k));
        }
        if (ids.Count == 0)
        {
            return;
        }
        await _client.DeleteAsync(Name, ids, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool includeVectors = options?.IncludeVectors == true;
        var ids = new[] { KeyConverter<TKey>.ToProtocolId(key) };
        var dtos = await _client.GetAsync(Name, ids, includeVectors, cancellationToken).ConfigureAwait(false);
        if (dtos.Count == 0)
        {
            return null;
        }
        var dto = dtos[0];
        return _mapper.CreateRecord(dto.Id, dto.Payload, includeVectors ? dto.Vector : null);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
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
            ids.Add(KeyConverter<TKey>.ToProtocolId(k));
        }
        if (ids.Count == 0)
        {
            yield break;
        }
        var dtos = await _client.GetAsync(Name, ids, includeVectors, cancellationToken).ConfigureAwait(false);
        foreach (var dto in dtos)
        {
            yield return _mapper.CreateRecord(dto.Id, dto.Payload, includeVectors ? dto.Vector : null);
        }
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<TRecord> GetAsync(
        System.Linq.Expressions.Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        bool includeVectors = options?.IncludeVectors == true;
        var translated = LinqFilterTranslator.Translate(filter, _mapper);
        var req = new VectorScrollRequest(translated, top) { IncludeVector = includeVectors };
        var dtos = await _client.ScrollAsync(Name, req, cancellationToken).ConfigureAwait(false);
        foreach (var dto in dtos)
        {
            yield return _mapper.CreateRecord(dto.Id, dto.Payload, includeVectors ? dto.Vector : null);
        }
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);

        DotVector.Query.Filter? translatedFilter = null;
        if (options?.Filter is { } filterExpr)
        {
            translatedFilter = LinqFilterTranslator.Translate(filterExpr, _mapper);
        }

        bool includeVectors = options?.IncludeVectors == true;
        var query = ExtractQueryVector(searchValue);
        var req = new VectorSearchRequest(query, top)
        {
            IncludeVector = includeVectors,
            Filter = translatedFilter,
        };
        var hits = await _client.SearchAsync(Name, req, cancellationToken).ConfigureAwait(false);

        foreach (var hit in hits)
        {
            var record = _mapper.CreateRecord(hit.Id, hit.Payload, includeVectors ? hit.Vector : null);
            yield return new VectorSearchResult<TRecord>(record, hit.Score);
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



    private VectorUpsertRecord ToDto(TRecord record)
    {
        var key = _mapper.GetKey(record);
        var id = KeyConverter<TKey>.ToProtocolId(key);
        var vec = _mapper.GetVector(record);
        var payload = _mapper.GetPayload(record);
        return new VectorUpsertRecord(id, vec) { Payload = payload };
    }

    private static float[] ExtractQueryVector<TInput>(TInput value)
        where TInput : notnull
    {
        return value switch
        {
            float[] arr => arr,
            ReadOnlyMemory<float> rom => rom.ToArray(),
            Memory<float> m => m.ToArray(),
            IEnumerable<float> e => e.ToArray(),
            _ => throw new NotSupportedException(
                $"DotVector M7 仅支持 float[] / ReadOnlyMemory<float> 作为查询向量；收到 {typeof(TInput).Name}。" +
                " TODO(M7+): 配合 IEmbeddingGenerator 支持文本/图像查询。"),
        };
    }
}
