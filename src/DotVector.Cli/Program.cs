using System.Globalization;
using System.Reflection;
using DotVector.Core.Protocol;
using DotVector.Data;

namespace DotVector.Cli;

/// <summary>
/// DotVector 本地命令行工具。
/// </summary>
/// <remarks>
/// 独立 gRPC Server / Docker 服务端项目已删除；CLI 只打开本地 <c>.dvec/</c> 数据库目录。
/// 需要服务端 endpoint 时应使用 SonnetDB。
/// </remarks>
internal static class Program
{
    private const string DefaultDataDirectory = "dotvector.dvec";

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
        Console.WriteLine("DotVector CLI - local embedded database");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotvector ping [--data <path>]");
        Console.WriteLine("  dotvector collections list [--data <path>]");
        Console.WriteLine("  dotvector collections create --name <n> --dim <d> [--metric <m>] [--data <path>]");
        Console.WriteLine("  dotvector collections delete --name <n> [--data <path>]");
        Console.WriteLine();
        Console.WriteLine($"  --data 默认 {DefaultDataDirectory} (也可读环境变量 DOTVECTOR_DATA)");
    }

    private static async Task<int> PingAsync(string[] args)
    {
        await using DotVectorClient client = NewClient(args);
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

        await using DotVectorClient client = NewClient(args);
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
                    string metricText = TryArg(args, "--metric") ?? nameof(DistanceMetric.Cosine);
                    DistanceMetric metric = ParseMetric(metricText);
                    await client.CreateCollectionAsync(name, dim, metric).ConfigureAwait(false);
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

    private static DotVectorClient NewClient(string[] args)
    {
        string dataDirectory = TryArg(args, "--data")
            ?? Environment.GetEnvironmentVariable("DOTVECTOR_DATA")
            ?? DefaultDataDirectory;
        return DotVectorClient.Embedded(dataDirectory);
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

    private static DistanceMetric ParseMetric(string value)
    {
        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse(normalized, ignoreCase: true, out DistanceMetric metric))
        {
            return metric;
        }
        throw new ArgumentException($"未知距离度量: {value}");
    }
}
