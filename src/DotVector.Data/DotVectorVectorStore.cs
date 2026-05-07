using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DotVector.Core;
using DotVector.Data.Internal;
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
        return new DotVectorCollection<TKey, TRecord>(_client, name, definition);
    }

    /// <inheritdoc/>
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(definition);
        return new DotVectorDynamicCollection(_client, name, definition);
    }

    /// <inheritdoc/>
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var infos = await _client.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < infos.Count; i++)
        {
            if (string.Equals(infos[i].Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await _client.DeleteCollectionAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var infos = await _client.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var info in infos)
        {
            yield return info.Name;
        }
    }

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
