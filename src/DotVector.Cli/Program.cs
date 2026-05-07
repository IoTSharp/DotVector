using System.Globalization;
using System.Reflection;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Data.Grpc;

namespace DotVector.Cli;

/// <summary>
/// DotVector CLI（M9）。
/// </summary>
/// <remarks>
/// 仅作为 gRPC 客户端使用 <see cref="GrpcDotVectorClient"/> 连接远端 DotVector 服务。
/// 服务端入口在独立的 <c>DotVector</c> 可执行中（参见 <c>src/DotVector/Program.cs</c>）。
/// </remarks>
internal static class Program
{
    private const string DefaultEndpoint = "http://localhost:5180";

    /// <summary>程序入口。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>退出码。</returns>
    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] is "--version" or "-v")
        {
            PrintVersion();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "ping" => await PingAsync(args).ConfigureAwait(false),
                "collections" => await CollectionsAsync(args).ConfigureAwait(false),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (global::Grpc.Core.RpcException rex)
        {
            Console.Error.WriteLine($"gRPC 错误: {rex.Status.StatusCode} - {rex.Status.Detail}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 4;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"未知命令: {cmd}");
        PrintUsage();
        return 2;
    }

    private static bool IsHelp(string s) => s is "-h" or "--help" or "help";

    private static void PrintVersion()
    {
        AssemblyInformationalVersionAttribute? attr = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Console.WriteLine(attr?.InformationalVersion ?? "0.0.0");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("DotVector CLI — gRPC client");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  dotvector ping [--endpoint <url>]");
        Console.WriteLine("  dotvector collections list   [--endpoint <url>]");
        Console.WriteLine("  dotvector collections create --name <n> --dim <d> [--metric <m>] [--endpoint <url>]");
        Console.WriteLine("  dotvector collections delete --name <n> [--endpoint <url>]");
        Console.WriteLine();
        Console.WriteLine($"  --endpoint 默认 {DefaultEndpoint}（也可读环境变量 DOTVECTOR_ENDPOINT）");
    }

    private static async Task<int> PingAsync(string[] args)
    {
        await using GrpcDotVectorClient client = NewClient(args);
        bool ok = await client.PingAsync().ConfigureAwait(false);
        Console.WriteLine(ok ? "OK" : "FAIL");
        return ok ? 0 : 1;
    }

    private static async Task<int> CollectionsAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: dotvector collections <list|create|delete> [...]");
            return 2;
        }

        await using GrpcDotVectorClient client = NewClient(args);
        switch (args[1])
        {
            case "list":
                IReadOnlyList<CollectionInfo> infos = await client.ListCollectionsAsync().ConfigureAwait(false);
                Console.WriteLine("NAME                 DIM    METRIC          COUNT");
                foreach (CollectionInfo info in infos)
                {
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-20} {1,-6} {2,-15} {3}",
                        info.Name, info.Dimensions, info.Metric, info.RecordCount));
                }
                return 0;

            case "create":
                {
                    string name = RequireArg(args, "--name");
                    int dim = int.Parse(RequireArg(args, "--dim"), CultureInfo.InvariantCulture);
                    string metric = TryArg(args, "--metric") ?? "Cosine";
                    await client.CreateCollectionAsync(new CreateCollectionRequest(name, dim, metric)).ConfigureAwait(false);
                    Console.WriteLine($"已创建集合 '{name}' (dim={dim}, metric={metric})");
                    return 0;
                }

            case "delete":
                {
                    string name = RequireArg(args, "--name");
                    await client.DeleteCollectionAsync(name).ConfigureAwait(false);
                    Console.WriteLine($"已删除集合 '{name}'");
                    return 0;
                }

            default:
                Console.Error.WriteLine($"未知子命令: {args[1]}");
                return 2;
        }
    }

    private static GrpcDotVectorClient NewClient(string[] args)
    {
        string endpoint = TryArg(args, "--endpoint")
            ?? Environment.GetEnvironmentVariable("DOTVECTOR_ENDPOINT")
            ?? DefaultEndpoint;
        return new GrpcDotVectorClient(new Uri(endpoint));
    }

    private static string? TryArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    private static string RequireArg(string[] args, string name)
        => TryArg(args, name) ?? throw new ArgumentException($"缺少参数 {name}");
}
