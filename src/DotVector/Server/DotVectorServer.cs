using DotVector.Api;
using DotVector.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Cryptography.X509Certificates;

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
    /// <param name="port">gRPC 监听端口，默认 5180；传 0 时由 OS 分配。</param>
    /// <param name="args">可选的命令行参数（透传给 <see cref="WebApplication.CreateBuilder(string[])"/>）。</param>
    /// <param name="loopbackOnly">是否仅监听本地回环地址；默认 <c>false</c>，监听任意地址。</param>
    /// <param name="httpsCertificate">
    /// 可选的 HTTPS 证书。
    /// 提供时端点以 HTTPS（HTTP/2 over TLS，通过 ALPN 协商）暴露 gRPC；
    /// 为 <c>null</c> 时使用 HTTP/2 cleartext (h2c) prior-knowledge。
    /// .NET 10 的 <c>HttpClient</c> 在某些组合下对 h2c 客户端校验过严，集成测试推荐传入自签证书。
    /// </param>
    /// <returns>已配置但尚未启动的 <see cref="WebApplication"/>。调用者负责 <c>RunAsync</c> / <c>StartAsync</c>。</returns>
    public static WebApplication Build(string dataDirectory, int port = 5180, string[]? args = null, bool loopbackOnly = false, X509Certificate2? httpsCertificate = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(port);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args ?? []);

        // 清空默认 URLs，避免 ASPNETCORE_URLS 之类环境变量再额外监听一组 HTTP/1.1 端点。
        builder.WebHost.UseUrls();
        builder.WebHost.ConfigureKestrel(options =>
        {
            // EndpointDefaults.Protocols=Http2，覆盖 Web SDK 可能注入的 Http1AndHttp2 默认。
            options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http2);

            void ConfigureListen(ListenOptions listen)
            {
                listen.Protocols = HttpProtocols.Http2;
                if (httpsCertificate is not null)
                {
                    listen.UseHttps(httpsCertificate);
                }
            }

            if (loopbackOnly)
            {
                options.Listen(IPAddress.Loopback, port, ConfigureListen);
            }
            else
            {
                options.ListenAnyIP(port, ConfigureListen);
            }
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
