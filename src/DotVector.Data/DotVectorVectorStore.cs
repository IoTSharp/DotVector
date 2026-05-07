using System.Diagnostics.CodeAnalysis;
using DotVector.Core;
using Microsoft.Extensions.VectorData;

namespace DotVector.Data;

/// <summary>
/// DotVector 对 <see cref="VectorStore"/> 的实现。
/// 通过单一 <see cref="IDotVectorClient"/> 暴露任意数量的
/// <see cref="DotVectorCollection{TKey, TRecord}"/>。
/// </summary>
/// <remarks>
/// 与 <c>DotVector</c>（服务端壳）解耦：本类型仅依赖 <see cref="IDotVectorClient"/>，
/// 因此既可承载嵌入式 <c>LocalDotVectorClient</c>，也可承载远程 <c>GrpcDotVectorClient</c>（M9）。
/// </remarks>
public sealed class DotVectorVectorStore : VectorStore
{
    private readonly IDotVectorClient _client;
    private readonly bool _ownsClient;
    private readonly VectorStoreMetadata _metadata;

    /// <summary>
    /// 初始化 <see cref="DotVectorVectorStore"/>。
    /// </summary>
    /// <param name="client">DotVector 协议客户端。</param>
    /// <param name="ownsClient">为 <c>true</c> 时本对象 <see cref="Dispose"/>
    /// 会一并释放 <paramref name="client"/>。</param>
    public DotVectorVectorStore(IDotVectorClient client, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _ownsClient = ownsClient;
        _metadata = new VectorStoreMetadata
        {
            VectorStoreSystemName = "dotvector",
        };
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode("DotVectorCollection 通过反射访问 TRecord 的属性。")]
    [RequiresDynamicCode("DotVectorCollection 通过反射访问 TRecord 的属性。")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition? definition = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (definition is not null)
        {
            // TODO(M7+): 支持基于 VectorStoreCollectionDefinition 的动态 schema。
            throw new NotSupportedException(
                "DotVector M7 仅支持基于属性标注的 TRecord 映射，不支持显式 VectorStoreCollectionDefinition。" +
                " TODO(M7+): 接入 Definition 驱动的映射。");
        }
        return new DotVectorCollection<TKey, TRecord>(_client, name);
    }

    /// <inheritdoc/>
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
        => throw new NotSupportedException(
            "DotVector M7 不支持 GetDynamicCollection。TODO(M7+): 后续版本接入。");

    /// <inheritdoc/>
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        // TODO(M7+): IDotVectorClient 增加 ListCollections 后改为查询。
        return await _client.PingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await _client.DeleteCollectionAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<string> ListCollectionNamesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "DotVector M7 不支持 ListCollectionNamesAsync。TODO(M7+): IDotVectorClient 增加 ListCollections 后实现。");

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType == typeof(VectorStoreMetadata))
        {
            return _metadata;
        }
        return null;
    }

}
