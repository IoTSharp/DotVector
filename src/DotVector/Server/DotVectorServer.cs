using DotVector.Api;

namespace DotVector.Server;

/// <summary>
/// DotVector 服务端宿主：在一个进程内托管多个嵌入式 <see cref="VectorDatabase"/> 实例。
/// 每个目录对应一个 <see cref="VectorDatabase"/>（即 DotVector.Core 的一次"打开"），
/// 多个数据库 ⇒ 多个 <see cref="VectorDatabase"/> 实例。
/// </summary>
/// <remarks>
/// TODO(M9): 此处将增加 gRPC 端点、生命周期管理、按目录路径动态打开 / 卸载数据库等能力。
/// 在 M0~M8 阶段，所有功能均通过嵌入式 <see cref="VectorDatabase"/> 直接调用即可。
/// </remarks>
public sealed class DotVectorServer
{
    private DotVectorServer()
    {
        // 占位：阻止外部实例化，等 M9 引入 Builder/Options 后开放。
    }
}
