using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotVector.Exceptions;
using DotVector.Format;

namespace DotVector.Storage;

/// <summary>
/// 集合清单（<c>manifest.bin</c>）的读写工具。
/// </summary>
/// <remarks>
/// 文件布局：固定大小 <see cref="CollectionManifest"/>（28 字节）。
/// 写入采用 <c>manifest.bin.tmp</c> + <see cref="File.Move(string, string, bool)"/> 原子替换。
/// </remarks>
internal static class CollectionManifestStore
{
    /// <summary>当前 manifest 格式版本号。</summary>
    public const uint CurrentVersion = 1;

    /// <summary>Magic 标识符："DVCOLMFT"（8 字节 ASCII）。</summary>
    public static ReadOnlySpan<byte> MagicBytes => "DVCOLMFT"u8;

    /// <summary>
    /// 读取指定路径的清单。文件不存在时返回默认值
    /// （<c>NextSegmentSequence=1, LastCoveredWalSequence=0</c>）。
    /// </summary>
    public static CollectionManifest Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            CollectionManifest defaultM = default;
            MagicBytes.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.As<Magic8, byte>(ref defaultM.Magic), 8));
            defaultM.Version = CurrentVersion;
            defaultM.NextSegmentSequence = 1;
            defaultM.LastCoveredWalSequence = 0;
            return defaultM;
        }

        byte[] buffer = File.ReadAllBytes(path);
        int size = Unsafe.SizeOf<CollectionManifest>();
        if (buffer.Length < size)
        {
            throw new DotVectorException($"manifest.bin 损坏：长度 {buffer.Length} 小于 {size}。");
        }
        CollectionManifest m = MemoryMarshal.Read<CollectionManifest>(buffer);
        ReadOnlySpan<byte> magic = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<Magic8, byte>(ref m.Magic), 8);
        if (!magic.SequenceEqual(MagicBytes))
        {
            throw new DotVectorException("manifest.bin 损坏：Magic 不匹配。");
        }
        if (m.Version != CurrentVersion)
        {
            throw new DotVectorException(
                $"不支持的 manifest 格式版本：{m.Version}（期望 {CurrentVersion}）。");
        }
        return m;
    }

    /// <summary>原子地写入清单到指定路径。</summary>
    public static void Write(string path, CollectionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(path);

        // 强制规范化 Magic 与 Version
        MagicBytes.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.As<Magic8, byte>(ref manifest.Magic), 8));
        manifest.Version = CurrentVersion;

        int size = Unsafe.SizeOf<CollectionManifest>();
        byte[] buffer = new byte[size];
        MemoryMarshal.Write(buffer, in manifest);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        string tmp = path + ".tmp";
        using (FileStream fs = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(buffer, 0, buffer.Length);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
