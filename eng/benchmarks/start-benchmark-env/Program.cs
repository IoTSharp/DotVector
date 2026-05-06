namespace StartBenchmarkEnv;

/// <summary>
/// 基准对照环境启动脚手架。
/// 将在 M8 中使用 Testcontainers 自动拉起 Qdrant / Milvus / pgvector 对照容器。
/// </summary>
/// <remarks>
/// TODO(M8): 使用 Testcontainers 拉起 Qdrant、Milvus、pgvector 容器，提供统一的对照基准环境。
/// </remarks>
internal static class Program
{
    internal static int Main(string[] args)
    {
        Console.WriteLine("DotVector start-benchmark-env — 基准对照环境启动脚手架");
        Console.WriteLine("TODO(M8): 使用 Testcontainers 启动 Qdrant / Milvus / pgvector 对照容器。");
        return 0;
    }
}
