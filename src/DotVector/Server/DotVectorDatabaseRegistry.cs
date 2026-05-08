using System.Collections.Concurrent;
using System.Text.Json;
using DotVector.Api;

namespace DotVector.Server;

internal sealed class DotVectorDatabaseRegistry : IDisposable
{
    public const string DefaultDatabaseName = "default";

    private static readonly string[] ReservedNames = ["system", "databases"];

    private readonly ConcurrentDictionary<string, Lazy<LocalDotVectorClient>> _clients =
        new(StringComparer.Ordinal);
    private readonly object _catalogLock = new();
    private readonly string _rootDirectory;
    private readonly string _systemDirectory;
    private readonly string _databaseRootDirectory;
    private DatabaseCatalogDocument _databaseCatalog;
    private bool _disposed;

    public DotVectorDatabaseRegistry(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _systemDirectory = Path.Combine(_rootDirectory, "system");
        _databaseRootDirectory = Path.Combine(_rootDirectory, "databases");

        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_systemDirectory);
        Directory.CreateDirectory(_databaseRootDirectory);

        _databaseCatalog = LoadOrCreateDatabaseCatalog();
        EnsureDefaultDatabase();
    }

    public string RootDirectory => _rootDirectory;

    public IReadOnlyList<DatabaseInfoResponse> ListDatabases()
    {
        lock (_catalogLock)
        {
            return _databaseCatalog.Databases
                .OrderBy(static d => d.Name, StringComparer.Ordinal)
                .Select(ToResponse)
                .ToArray();
        }
    }

    public DatabaseInfoResponse CreateDatabase(string name)
    {
        DatabaseCatalogEntry entry = EnsureDatabase(name);
        return ToResponse(entry);
    }

    public LocalDotVectorClient GetClient(string? databaseName)
    {
        ThrowIfDisposed();
        DatabaseCatalogEntry entry = EnsureDatabase(NormalizeDatabaseName(databaseName));
        Lazy<LocalDotVectorClient> lazy = _clients.GetOrAdd(
            entry.Name,
            _ => new Lazy<LocalDotVectorClient>(
                () => new LocalDotVectorClient(
                    new VectorDatabase(ResolveDatabasePath(entry.RelativePath)),
                    ownsDatabase: true),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (Lazy<LocalDotVectorClient> lazy in _clients.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            lazy.Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _clients.Clear();
        _disposed = true;
    }

    private void EnsureDefaultDatabase()
        => EnsureDatabase(DefaultDatabaseName, detectLegacyDefault: true);

    private DatabaseCatalogEntry EnsureDatabase(string? rawName, bool detectLegacyDefault = false)
    {
        string name = NormalizeDatabaseName(rawName);
        ValidateDatabaseName(name);

        lock (_catalogLock)
        {
            DatabaseCatalogEntry? existing = _databaseCatalog.Databases
                .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing;
            }

            string relativePath = GetDefaultRelativeDatabasePath(name, detectLegacyDefault);
            string fullPath = ResolveDatabasePath(relativePath);
            Directory.CreateDirectory(fullPath);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var entry = new DatabaseCatalogEntry
            {
                Name = name,
                RelativePath = relativePath,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            _databaseCatalog.Databases.Add(entry);
            SaveDatabaseCatalogLocked();
            return entry;
        }
    }

    private string GetDefaultRelativeDatabasePath(string name, bool detectLegacyDefault)
    {
        if (detectLegacyDefault && HasLegacyDatabaseLayout(_rootDirectory))
        {
            return ".";
        }

        return Path.Combine("databases", name + ".dvec");
    }

    private static bool HasLegacyDatabaseLayout(string rootDirectory)
        => File.Exists(Path.Combine(rootDirectory, "catalog.bin"))
           || Directory.Exists(Path.Combine(rootDirectory, "collections"))
           || Directory.Exists(Path.Combine(rootDirectory, "wal"));

    private string ResolveDatabasePath(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));
        if (!fullPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("数据库目录必须位于服务端数据根目录内。");
        }

        return fullPath;
    }

    private DatabaseCatalogDocument LoadOrCreateDatabaseCatalog()
    {
        string path = Path.Combine(_systemDirectory, "databases.json");
        DatabaseCatalogDocument? loaded = ReadDatabaseCatalog(path);
        if (loaded is not null)
        {
            return loaded;
        }

        var created = new DatabaseCatalogDocument();
        WriteDatabaseCatalog(path, created);
        return created;
    }

    private void SaveDatabaseCatalogLocked()
        => WriteDatabaseCatalog(Path.Combine(_systemDirectory, "databases.json"), _databaseCatalog);

    private static string NormalizeDatabaseName(string? databaseName)
        => string.IsNullOrWhiteSpace(databaseName) ? DefaultDatabaseName : databaseName.Trim();

    private static void ValidateDatabaseName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (ReservedNames.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"数据库名称 '{name}' 为保留名称。", nameof(name));
        }

        if (name is "." or ".." || name.StartsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException("数据库名称不能以点号开头。", nameof(name));
        }

        foreach (char ch in name)
        {
            bool allowed = char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_';
            if (!allowed)
            {
                throw new ArgumentException("数据库名称仅允许 ASCII 字母、数字、短横线和下划线。", nameof(name));
            }
        }
    }

    private static DatabaseInfoResponse ToResponse(DatabaseCatalogEntry entry)
        => new()
        {
            Name = entry.Name,
            RelativePath = entry.RelativePath,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
        };

    private static DatabaseCatalogDocument? ReadDatabaseCatalog(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, DotVectorServerJsonContext.Default.DatabaseCatalogDocument);
    }

    private static void WriteDatabaseCatalog(string path, DatabaseCatalogDocument value)
    {
        string tempPath = path + ".tmp";
        using (FileStream stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, value, DotVectorServerJsonContext.Default.DatabaseCatalogDocument);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

}

internal sealed class DatabaseCatalogDocument
{
    public List<DatabaseCatalogEntry> Databases { get; set; } = [];
}

internal sealed class DatabaseCatalogEntry
{
    public string Name { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class DatabaseInfoResponse
{
    public string Name { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
