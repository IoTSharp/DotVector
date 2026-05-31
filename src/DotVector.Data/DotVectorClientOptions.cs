using System.Net.Http;

namespace DotVector.Data;

/// <summary>
/// 旧版远程 <see cref="DotVectorClient"/> 连接选项。
/// </summary>
/// <remarks>
/// DotVector 独立 Server / gRPC 远程模式已经删除。本类型仅为源兼容保留；
/// <see cref="DotVectorClient.Connect(string, DotVectorClientOptions)"/> 会始终抛出
/// <see cref="NotSupportedException"/>。
/// </remarks>
public sealed class DotVectorClientOptions
{
    /// <summary>
    /// 旧版服务端逻辑数据库名称。该属性仅为源兼容保留。
    /// </summary>
    public string? Database { get; set; }

    /// <summary>
    /// 旧版远程鉴权凭据。该属性仅为源兼容保留。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 旧版远程调用超时。该属性仅为源兼容保留。
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 旧版远程 HTTP handler。该属性仅为源兼容保留。
    /// </summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>
    /// 旧版远程代理开关。该属性仅为源兼容保留。
    /// </summary>
    public bool UseProxy { get; set; }
}
