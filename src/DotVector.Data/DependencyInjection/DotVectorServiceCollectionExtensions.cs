using DotVector.Core;
using DotVector.Data;
using Microsoft.Extensions.VectorData;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DotVector.Data 的依赖注入扩展方法。
/// </summary>
public static class DotVectorServiceCollectionExtensions
{
    /// <summary>
    /// 将 <see cref="DotVectorVectorStore"/> 注册为单例 <see cref="VectorStore"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="clientFactory">从 <see cref="IServiceProvider"/> 解析 <see cref="IDotVectorClient"/> 的工厂。</param>
    /// <returns>原 <paramref name="services"/>，便于链式调用。</returns>
    /// <remarks>
    /// 由调用方负责 <see cref="IDotVectorClient"/> 的生命周期；本扩展不会自动 dispose。
    /// </remarks>
    public static IServiceCollection AddDotVectorVectorStore(
        this IServiceCollection services,
        Func<IServiceProvider, IDotVectorClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.AddSingleton<VectorStore>(sp => new DotVectorVectorStore(clientFactory(sp)));
        return services;
    }
}
