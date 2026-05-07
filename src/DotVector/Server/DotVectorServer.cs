using DotVector.Api;
using DotVector.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotVector.Server;

/// <summary>
/// DotVector 服务端宿主：在一个进程内托管一个或多个嵌入式 <see cref="VectorDatabase"/> 实例，
/// 并通过 gRPC（HTTP/2）暴露 <see cref="VectorService"/> 端点。
/// </summary>
public static class DotVectorServer
{
    /// <summary>
    /// 使用指定数据目录与端口构建 gRPC 宿主 <see cref="WebApplication"/>。
    /// </summary>
    /// <param name="dataDirectory">数据库根目录，对应一个 <c>.dvec</c> 实例。</param>
    /// <param name="port">gRPC 监听端口，默认 5180。</param>
    /// <param name="args">可选的命令行参数（透传给 <see cref="WebApplication.CreateBuilder(string[])"/>）。</param>
    /// <returns>已配置但尚未启动的 <see cref="WebApplication"/>。调用者负责 <c>RunAsync</c> / <c>StartAsync</c>。</returns>
    public static WebApplication Build(string dataDirectory, int port = 5180, string[]? args = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args ?? []);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http2);
        });

        var database = new VectorDatabase(dataDirectory);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(sp => new LocalDotVectorClient(sp.GetRequiredService<VectorDatabase>(), ownsDatabase: false));
        builder.Services.AddSingleton<VectorServiceImpl>();
        builder.Services.AddGrpc();

        WebApplication app = builder.Build();
        app.MapGrpcService<VectorServiceImpl>();
        app.Lifetime.ApplicationStopped.Register(() => database.Dispose());
        return app;
    }
}
