using System.Reflection;

namespace DotVector.Cli;

/// <summary>
/// DotVector 命令行工具入口点。
/// </summary>
internal static class Program
{
    /// <summary>
    /// CLI 入口，打印版本信息。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <remarks>
    /// TODO(M9): 实现 gRPC server 模式（--serve）与 Native AOT 单文件发布。
    /// </remarks>
    internal static int Main(string[] args)
    {
        string version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0";

        Console.WriteLine($"DotVector CLI v{version}");
        Console.WriteLine("嵌入式向量数据库 — .NET 10 原生");
        Console.WriteLine();
        Console.WriteLine("用法（M9 实现后填充）：");
        Console.WriteLine("  dotvector --help");

        return 0;
    }
}
