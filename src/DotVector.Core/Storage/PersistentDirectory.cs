using DotVector.Api;
using DotVector.Catalog;
using DotVector.Exceptions;
using DotVector.IO;
using DotVector.Model;
using DotVector.Wal;

namespace DotVector.Storage;

/// <summary>
/// 持久化目录管理器：负责打开/创建 <c>.dvec/</c>，加载 catalog.bin，
/// 维护单个共享的 WAL 写入器，并向各 <see cref="Collection{TKey}"/> 注入
/// <see cref="IWriteSink{TKey}"/>，把所有变更先落盘 WAL 再写入内存索引。
/// </summary>
/// <remarks>
/// 当前实现：
/// <list type="bullet">
///   <item>单个数据库目录共享一个 WAL 文件 <c>wal/wal-000001.log</c>。</item>
///   <item>启动时回放 WAL 中所有有效记录到对应集合。</item>
///   <item>关闭时执行 <see cref="WalWriter.Flush"/> 并释放句柄。</item>
/// </list>
/// 不支持 WAL 滚动 / Segment 落盘 / 压缩——后续 Milestone 处理。
/// </remarks>
internal sealed class PersistentDirectory : IDisposable
{
    private readonly string _root;
    private readonly string _walPath;
    private readonly object _lock = new();
    private readonly List<CatalogEntry> _entries;
    private readonly Dictionary<Guid, IDisposable> _sinks = new();
    private WalWriter? _walWriter;
    private bool _disposed;

    /// <summary>已加载的集合元数据列表。</summary>
    public IReadOnlyList<CatalogEntry> Entries => _entries;

    private PersistentDirectory(string root, string walPath, List<CatalogEntry> entries)
    {
        _root = root;
        _walPath = walPath;
        _entries = entries;
    }

    /// <summary>打开或创建指定的 <c>.dvec/</c> 目录。</summary>
    public static PersistentDirectory Open(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        Directory.CreateDirectory(directoryPath);
        Directory.CreateDirectory(Path.Combine(directoryPath, "wal"));
        Directory.CreateDirectory(Path.Combine(directoryPath, "collections"));

        string catalogPath = Path.Combine(directoryPath, "catalog.bin");
        var entries = new List<CatalogEntry>(CatalogStore.Read(catalogPath));
        string walPath = Path.Combine(directoryPath, "wal", "wal-000001.log");
        return new PersistentDirectory(directoryPath, walPath, entries);
    }

    private WalWriter EnsureWalWriter()
    {
        if (_walWriter is null)
        {
            _walWriter = new WalWriter(_walPath);
        }
        return _walWriter;
    }

    /// <summary>把新建集合写入 catalog 并返回其分配的 <see cref="Guid"/>。</summary>
    public Guid RegisterCollection<TKey>(
        string name,
        int dimensions,
        Metric metric,
        IndexKind indexKind) where TKey : notnull
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_entries.Any(e => e.Name == name))
            {
                throw new InvalidOperationException($"集合 '{name}' 已存在。");
            }
            var entry = new CatalogEntry
            {
                CollectionId = Guid.NewGuid(),
                Name = name,
                Dimensions = dimensions,
                KeyType = KeyCodec.GetCode<TKey>(),
                IndexKind = indexKind,
                Metric = metric,
            };
            _entries.Add(entry);
            PersistCatalog();
            return entry.CollectionId;
        }
    }

    /// <summary>从 catalog 中删除指定集合。</summary>
    public bool UnregisterCollection(string name)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            int idx = _entries.FindIndex(e => e.Name == name);
            if (idx < 0) { return false; }
            Guid id = _entries[idx].CollectionId;
            _entries.RemoveAt(idx);
            PersistCatalog();
            if (_sinks.Remove(id, out IDisposable? sink))
            {
                // sinks 当前是非托管空壳，仅记录，无需 Dispose。
                _ = sink;
            }
            // 删除 collection 目录（若存在）。
            string collDir = Path.Combine(_root, "collections", id.ToString("N"));
            if (Directory.Exists(collDir))
            {
                try { Directory.Delete(collDir, recursive: true); }
                catch (IOException) { /* 文件被占用时忽略 — 由调用方决定后续处理 */ }
            }
            return true;
        }
    }

    private void PersistCatalog()
    {
        string catalogPath = Path.Combine(_root, "catalog.bin");
        CatalogStore.Write(catalogPath, _entries);
    }

    /// <summary>为指定集合创建 WAL 写入观察者。</summary>
    public IWriteSink<TKey> CreateSink<TKey>(Guid collectionId) where TKey : notnull
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            var sink = new WalSink<TKey>(EnsureWalWriter(), collectionId);
            _sinks[collectionId] = sink;
            return sink;
        }
    }

    /// <summary>枚举指定集合在 WAL 中的所有有效记录（按写入顺序）。</summary>
    public IEnumerable<WalRecord> ReadWalFor(Guid collectionId)
    {
        string walDir = Path.Combine(_root, "wal");
        foreach (WalRecord r in WalReader.ReadAll(walDir))
        {
            if (r.CollectionId == collectionId)
            {
                yield return r;
            }
        }
    }

    /// <summary>把所有缓冲数据刷入磁盘。</summary>
    public void Flush()
    {
        lock (_lock)
        {
            _walWriter?.Flush();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        lock (_lock)
        {
            try { _walWriter?.Dispose(); }
            catch (IOException) { /* 关闭阶段忽略 */ }
            _walWriter = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>WAL 写入观察者实现。</summary>
    private sealed class WalSink<TKey> : IWriteSink<TKey>, IDisposable where TKey : notnull
    {
        private readonly WalWriter _wal;
        private readonly Guid _collectionId;

        public WalSink(WalWriter wal, Guid collectionId)
        {
            _wal = wal;
            _collectionId = collectionId;
        }

        public void OnInsert(TKey key, ReadOnlySpan<float> vector)
            => _wal.AppendInsert(_collectionId, key, vector);

        public void OnDelete(TKey key)
            => _wal.AppendDelete(_collectionId, key);

        public void Dispose() { /* WalWriter 由 PersistentDirectory 拥有 */ }
    }
}
