using System.Net.Http;

namespace DotVector.Data;

/// <summary>
/// <see cref="DotVectorClient"/> 的连接 / 通道选项。
/// </summary>
public sealed class DotVectorClientOptions
{
    /// <summary>
    /// 服务端逻辑数据库名称（用于多租户）。为空时使用服务端默认数据库。
    /// 仅对远程 gRPC 模式有效；嵌入式模式忽略。
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// 预留鉴权凭据（写入 gRPC metadata <c>authorization</c> 头，例如 <c>Bearer xxx</c>）。
    /// 当前服务端尚未启用鉴权，写入但不强制校验。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 远程 RPC 调用的默认超时；为 <see langword="null"/> 时不设置。
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 自定义 <see cref="HttpMessageHandler"/>。为空时由 <see cref="DotVectorClient"/> 构造一个
    /// 默认的 <see cref="System.Net.Http.SocketsHttpHandler"/>（启用 HTTP/2 多连接、关闭代理）。
    /// </summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>
    /// 是否走系统代理。默认 <see langword="false"/>，避免本地 loopback 被
    /// HTTP_PROXY / Clash / Fiddler 等劫持后 gRPC 子通道失败。
    /// </summary>
    public bool UseProxy { get; set; }
}
