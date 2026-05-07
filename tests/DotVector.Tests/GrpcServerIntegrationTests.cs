using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Data.Grpc;
using DotVector.Server;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace DotVector.Tests;

/// <summary>
/// M9：DotVectorServer + GrpcDotVectorClient 端到端集成测试。
/// </summary>
/// <remarks>
/// 测试通道使用一次性的自签名证书走 HTTPS（HTTP/2 over TLS，ALPN h2），
/// 避免对宿主 dev-cert 的依赖以及 .NET 10 上 h2c prior-knowledge 在某些组合下
/// 的握手不稳定。生产部署默认仍是 h2c（参见 <see cref="DotVectorServer.Build"/>）。
/// </remarks>
public sealed class GrpcServerIntegrationTests : IAsyncLifetime
{
    private string _dataDir = string.Empty;
    private WebApplication? _app;
    private string _endpoint = string.Empty;
    private X509Certificate2? _certificate;

    public async Task InitializeAsync()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "dotvector-grpc-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);

        _certificate = CreateSelfSignedCertificate();
        _app = DotVectorServer.Build(_dataDir, port: 0, loopbackOnly: true, httpsCertificate: _certificate);
        await _app.StartAsync().ConfigureAwait(false);

        IServerAddressesFeature addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("无法获取服务器地址。");
        Uri parsed = new(addresses.Addresses.First());
        _endpoint = $"{parsed.Scheme}://127.0.0.1:{parsed.Port}";
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        _certificate?.Dispose();

        if (Directory.Exists(_dataDir))
        {
            try { Directory.Delete(_dataDir, recursive: true); }
            catch (IOException) { /* 测试环境清理失败不致命 */ }
        }
    }

    [Fact]
    public async Task EndToEnd_PingCreateUpsertSearchDelete_RoundTrips()
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            // 显式关闭代理：开发机/CI 上常见 HTTP_PROXY、HTTPS_PROXY 或系统代理（如 127.0.0.1:7890）
            // 会把 loopback 请求也走代理，导致 CONNECT/h2 握手在代理处失败。
            UseProxy = false,
            Proxy = null,
            SslOptions = new SslClientAuthenticationOptions
            {
                // 测试用：信任进程内自签证书。
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
        var channelOptions = new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true };

        await using GrpcDotVectorClient client = new(new Uri(_endpoint), channelOptions);

        // Ping
        Assert.True(await client.PingAsync());

        // Create
        const string name = "it-coll";
        await client.CreateCollectionAsync(new CreateCollectionRequest(name, dimensions: 4, metric: "Cosine"));

        // List
        IReadOnlyList<CollectionInfo> infos = await client.ListCollectionsAsync();
        Assert.Contains(infos, c => c.Name == name && c.Dimensions == 4);

        // Upsert
        VectorUpsertRecord[] records =
        [
            new("a", [1f, 0f, 0f, 0f]),
            new("b", [0f, 1f, 0f, 0f]),
            new("c", [0f, 0f, 1f, 0f]),
        ];
        await client.UpsertAsync(name, records);

        // Search — 与 'a' 最相似
        IReadOnlyList<VectorSearchResult> hits = await client.SearchAsync(
            name,
            new VectorSearchRequest([1f, 0f, 0f, 0f], topK: 2));
        Assert.NotEmpty(hits);
        Assert.Equal("a", hits[0].Id);

        // Delete collection
        await client.DeleteCollectionAsync(name);
        IReadOnlyList<CollectionInfo> after = await client.ListCollectionsAsync();
        Assert.DoesNotContain(after, c => c.Name == name);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using X509Certificate2 ephemeral = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows 上 Kestrel 需要可导出的私钥，绕一圈 PFX 是最稳的做法。
        byte[] pfx = ephemeral.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }
}
