namespace DotVector.Api;

/// <summary>
/// 管理本地嵌入式 DotVector 数据库生命周期。每个数据库名称对应根目录下一个独立的 <c>.dvec/</c> 目录。
/// </summary>
public sealed class LocalVectorDatabaseManager : IDisposable
{
    private const string DatabaseDirectorySuffix = ".dvec";
    private readonly Dictionary<string, VectorDatabase> _openDatabases = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// 初始化本地数据库管理器。
    /// </summary>
    /// <param name="rootDirectoryPath">存放多个 <c>.dvec/</c> 数据库目录的根目录。</param>
    public LocalVectorDatabaseManager(string rootDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectoryPath);
        RootDirectoryPath = Path.GetFullPath(rootDirectoryPath);
        Directory.CreateDirectory(RootDirectoryPath);
    }

    /// <summary>数据库根目录路径。</summary>
    public string RootDirectoryPath { get; }

    /// <summary>
    /// 创建新的本地数据库并打开它。
    /// </summary>
    /// <param name="name">数据库名称。对应目录为 <c>{name}.dvec/</c>。</param>
    /// <returns>已打开的数据库实例。</returns>
    public VectorDatabase CreateDatabase(string name)
    {
        string normalizedName = NormalizeDatabaseName(name);
        string path = GetDatabaseDirectoryPath(normalizedName);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_openDatabases.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"数据库 '{normalizedName}' 已打开。");
            }
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"数据库 '{normalizedName}' 已存在。");
            }

            VectorDatabase database = new(path);
            _openDatabases.Add(normalizedName, database);
            return database;
        }
    }

    /// <summary>
    /// 打开已存在的本地数据库。
    /// </summary>
    /// <param name="name">数据库名称。</param>
    /// <returns>已打开的数据库实例。</returns>
    public VectorDatabase OpenDatabase(string name)
    {
        string normalizedName = NormalizeDatabaseName(name);
        string path = GetDatabaseDirectoryPath(normalizedName);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_openDatabases.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"数据库 '{normalizedName}' 已打开。");
            }
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"数据库 '{normalizedName}' 不存在。");
            }

            VectorDatabase database = new(path);
            _openDatabases.Add(normalizedName, database);
            return database;
        }
    }

    /// <summary>
    /// 列出根目录下所有本地数据库。
    /// </summary>
    /// <returns>按名称升序排列的数据库信息快照。</returns>
    public IReadOnlyList<LocalVectorDatabaseInfo> ListDatabases()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!Directory.Exists(RootDirectoryPath))
            {
                return Array.Empty<LocalVectorDatabaseInfo>();
            }

            string[] directories = Directory.GetDirectories(RootDirectoryPath, "*" + DatabaseDirectorySuffix, SearchOption.TopDirectoryOnly);
            var databases = new List<LocalVectorDatabaseInfo>(directories.Length);
            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory)[..^DatabaseDirectorySuffix.Length];
                databases.Add(new LocalVectorDatabaseInfo(
                    name,
                    directory,
                    _openDatabases.ContainsKey(name)));
            }

            databases.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return databases;
        }
    }

    /// <summary>
    /// 关闭已打开的本地数据库。
    /// </summary>
    /// <param name="name">数据库名称。</param>
    /// <returns>存在打开实例并成功关闭返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    public bool CloseDatabase(string name)
    {
        string normalizedName = NormalizeDatabaseName(name);
        VectorDatabase? database;

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_openDatabases.Remove(normalizedName, out database))
            {
                return false;
            }
        }

        database.Dispose();
        return true;
    }

    /// <summary>
    /// 删除已关闭的本地数据库目录。
    /// </summary>
    /// <param name="name">数据库名称。</param>
    /// <returns>数据库目录存在并已删除返回 <see langword="true"/>；不存在返回 <see langword="false"/>。</returns>
    public bool DeleteDatabase(string name)
    {
        string normalizedName = NormalizeDatabaseName(name);
        string path = GetDatabaseDirectoryPath(normalizedName);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_openDatabases.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"数据库 '{normalizedName}' 仍处于打开状态，请先调用 CloseDatabase。");
            }
            if (!Directory.Exists(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        VectorDatabase[] databases;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            databases = _openDatabases.Values.ToArray();
            _openDatabases.Clear();
        }

        foreach (VectorDatabase database in databases)
        {
            database.Dispose();
        }
    }

    private string GetDatabaseDirectoryPath(string normalizedName)
        => Path.Combine(RootDirectoryPath, normalizedName + DatabaseDirectorySuffix);

    private static string NormalizeDatabaseName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("数据库名称不能为空。", nameof(name));
        }
        if (normalized.EndsWith(DatabaseDirectorySuffix, StringComparison.Ordinal))
        {
            normalized = normalized[..^DatabaseDirectorySuffix.Length];
        }
        if (normalized.Length == 0 || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || normalized.Contains(Path.DirectorySeparatorChar) || normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"数据库名称 '{name}' 不是有效的目录名称。", nameof(name));
        }

        return normalized;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// 本地 DotVector 数据库目录信息。
/// </summary>
/// <param name="Name">数据库名称。</param>
/// <param name="DirectoryPath">数据库 <c>.dvec/</c> 目录路径。</param>
/// <param name="IsOpen">当前管理器是否持有打开的数据库实例。</param>
public sealed record LocalVectorDatabaseInfo(string Name, string DirectoryPath, bool IsOpen);
