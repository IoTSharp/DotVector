using System.Globalization;
using DotVector.Api;
using DotVector.Catalog;
using DotVector.Exceptions;
using DotVector.Format;
using DotVector.Index.DiskAnn;
using DotVector.Index.Flat;
using DotVector.IO;
using DotVector.Model;
using DotVector.Wal;

namespace DotVector.Storage;

/// <summary>
/// 持久化目录管理器：负责打开/创建 <c>.dvec/</c>，加载 catalog.bin 与各集合
/// <c>manifest.bin</c>，维护单个共享的 WAL 写入器，并向各 <see cref="Collection{TKey}"/>
/// 注入 <see cref="IWriteSink{TKey}"/>，把所有变更先落盘 WAL 再写入内存索引。
/// </summary>
/// <remarks>
/// <para>M10 之后的能力：</para>
/// <list type="bullet">
///   <item>WAL 文件按序列号命名 <c>wal/wal-{seq:D6}.log</c>；启动时扫描目录找出最大 seq。</item>
///   <item>每个集合在 <c>collections/{id:N}/manifest.bin</c> 维护
///         <see cref="CollectionManifest"/>（下一个 Segment 序列号 + 已覆盖的 WAL 序列号）。</item>
///   <item>Flush：旋转 WAL 到下一个序列号，对索引快照后写入新 Segment 目录，更新 manifest，
///         尝试裁剪所有集合都已覆盖的旧 WAL。</item>
///   <item>Compact：把集合所有 Segment 合并为一个新 Segment，删除旧目录。</item>
/// </list>
/// </remarks>
internal sealed class PersistentDirectory : IDisposable
{
    private readonly string _root;
    private readonly object _lock = new();
    private readonly List<CatalogEntry> _entries;
    private readonly Dictionary<Guid, IDisposable> _sinks = new();
    private readonly Dictionary<Guid, CollectionManifest> _manifests = new();
    private readonly Dictionary<Guid, int> _flushedRows = new();
    private long _currentWalSeq;
    private WalWriter? _walWriter;
    private bool _disposed;

    /// <summary>已加载的集合元数据列表。</summary>
    public IReadOnlyList<CatalogEntry> Entries => _entries;

    private PersistentDirectory(string root, List<CatalogEntry> entries, long currentWalSeq)
    {
        _root = root;
        _entries = entries;
        _currentWalSeq = currentWalSeq;
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

        // 扫描已有 wal-*.log，找出最大序列号；若无则从 1 开始。
        long maxSeq = 0;
        string walDir = Path.Combine(directoryPath, "wal");
        foreach (string file in Directory.EnumerateFiles(walDir, "wal-*.log"))
        {
            long seq = TryParseWalSeq(Path.GetFileName(file));
            if (seq > maxSeq) { maxSeq = seq; }
        }
        long currentSeq = maxSeq == 0 ? 1 : maxSeq;

        var pd = new PersistentDirectory(directoryPath, entries, currentSeq);
        // 加载各集合 manifest
        foreach (CatalogEntry e in entries)
        {
            pd._manifests[e.CollectionId] = CollectionManifestStore.Read(pd.GetManifestPath(e.CollectionId));
        }
        return pd;
    }

    /// <summary>解析 "wal-{N:D6}.log" 中的序列号；不匹配返回 0。</summary>
    private static long TryParseWalSeq(string fileName)
    {
        if (!fileName.StartsWith("wal-", StringComparison.Ordinal) ||
            !fileName.EndsWith(".log", StringComparison.Ordinal))
        {
            return 0;
        }
        ReadOnlySpan<char> mid = fileName.AsSpan(4, fileName.Length - 4 - 4);
        return long.TryParse(mid, NumberStyles.None, CultureInfo.InvariantCulture, out long seq) ? seq : 0;
    }

    private string GetWalPath(long seq)
        => Path.Combine(_root, "wal", $"wal-{seq:D6}.log");

    private string GetCollectionDir(Guid id)
        => Path.Combine(_root, "collections", id.ToString("N"));

    private string GetManifestPath(Guid id)
        => Path.Combine(GetCollectionDir(id), "manifest.bin");

    private string GetSegmentsDir(Guid id)
        => Path.Combine(GetCollectionDir(id), "segments");

    private string GetSegmentDir(Guid id, long segSeq)
        => Path.Combine(GetSegmentsDir(id), $"seg-{segSeq:D6}");

    private WalWriter EnsureWalWriter()
    {
        _walWriter ??= new WalWriter(GetWalPath(_currentWalSeq));
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
            // 初始化 manifest（默认值，先不写盘 — 第一次 flush 时再持久化）。
            _manifests[entry.CollectionId] = CollectionManifestStore.Read(GetManifestPath(entry.CollectionId));
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
            _manifests.Remove(id);
            PersistCatalog();
            if (_sinks.Remove(id, out IDisposable? sink))
            {
                _ = sink;
            }
            string collDir = GetCollectionDir(id);
            if (Directory.Exists(collDir))
            {
                try { Directory.Delete(collDir, recursive: true); }
                catch (IOException) { /* 文件被占用时忽略 — 由调用方决定后续处理 */ }
            }
            // 删除一个集合后，可能让一些 WAL 变得可裁剪。
            TryTrimWal();
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
            _ = EnsureWalWriter();
            var sink = new WalSink<TKey>(this, collectionId);
            _sinks[collectionId] = sink;
            return sink;
        }
    }

    /// <summary>追加 Insert 记录到 WAL（在 _lock 内串行化）。</summary>
    internal void AppendInsert<TKey>(Guid collectionId, TKey key, ReadOnlySpan<float> vector) where TKey : notnull
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            EnsureWalWriter().AppendInsert(collectionId, key, vector);
        }
    }

    /// <summary>追加 Delete 记录到 WAL（在 _lock 内串行化）。</summary>
    internal void AppendDelete<TKey>(Guid collectionId, TKey key) where TKey : notnull
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            EnsureWalWriter().AppendDelete(collectionId, key);
        }
    }

    /// <summary>追加 SetPayload 记录到 WAL（M11，串行化）。</summary>
    internal void AppendPayload<TKey>(Guid collectionId, TKey key, ReadOnlySpan<byte> encodedPayload) where TKey : notnull
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            EnsureWalWriter().AppendPayload(collectionId, key, encodedPayload);
        }
    }

    /// <summary>
    /// 枚举指定集合在 WAL 中的所有有效记录（按写入顺序）；
    /// 仅返回所在 WAL 文件序列号 &gt; <paramref name="minSeqExclusive"/> 的记录。
    /// </summary>
    public IEnumerable<WalRecord> ReadWalFor(Guid collectionId, long minSeqExclusive = 0)
    {
        string walDir = Path.Combine(_root, "wal");
        if (!Directory.Exists(walDir))
        {
            yield break;
        }
        string[] files = Directory.GetFiles(walDir, "wal-*.log");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            long seq = TryParseWalSeq(Path.GetFileName(file));
            if (seq <= minSeqExclusive) { continue; }
            foreach (WalRecord r in WalReader.ReadFile(file))
            {
                if (r.CollectionId == collectionId)
                {
                    yield return r;
                }
            }
        }
    }

    /// <summary>读取指定集合的 manifest 副本（默认值若尚未注册）。</summary>
    public CollectionManifest GetManifest(Guid collectionId)
    {
        lock (_lock)
        {
            if (_manifests.TryGetValue(collectionId, out CollectionManifest m)) { return m; }
            return CollectionManifestStore.Read(GetManifestPath(collectionId));
        }
    }

    /// <summary>
    /// 在恢复完成后，告知 PersistentDirectory 该集合内存索引中已经从 Segment 加载的行数。
    /// 后续 <see cref="FlushCollection{TKey}(Guid, FlatIndex{TKey})"/> 会跳过这些行，仅写入新增的 delta。
    /// </summary>
    internal void NotifyRestoredRowCount(Guid collectionId, int restoredRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(restoredRows);
        lock (_lock)
        {
            _flushedRows[collectionId] = restoredRows;
        }
    }

    /// <summary>枚举指定集合现存的所有 Segment（按 seq 升序），调用方负责释放。</summary>
    public IEnumerable<SegmentReader<TKey>> LoadSegments<TKey>(Guid collectionId) where TKey : notnull
    {
        string segsDir = GetSegmentsDir(collectionId);
        if (!Directory.Exists(segsDir))
        {
            yield break;
        }
        string[] dirs = Directory.GetDirectories(segsDir, "seg-*");
        var clean = dirs.Where(d => !d.EndsWith(".tmp", StringComparison.Ordinal)).ToArray();
        Array.Sort(clean, StringComparer.Ordinal);
        foreach (string dir in clean)
        {
            yield return SegmentReader<TKey>.Open(dir);
        }
    }

    /// <summary>
    /// 返回集合最新（按字典序最大）segment 目录的绝对路径；不存在时返回 <see langword="null"/>。
    /// 主要供 Vamana 恢复使用：M12.3 起 Vamana 每次 Flush 都是单 segment 全量快照。
    /// </summary>
    internal string? TryGetLatestSegmentDir(Guid collectionId)
    {
        string segsDir = GetSegmentsDir(collectionId);
        if (!Directory.Exists(segsDir)) { return null; }
        string? latest = null;
        foreach (string dir in Directory.EnumerateDirectories(segsDir, "seg-*"))
        {
            if (dir.EndsWith(".tmp", StringComparison.Ordinal)) { continue; }
            if (latest is null || string.CompareOrdinal(dir, latest) > 0)
            {
                latest = dir;
            }
        }
        return latest;
    }

    /// <summary>
    /// 把集合的当前内存索引快照写成新 Segment 并旋转 WAL。
    /// 仅 <see cref="FlatIndex{TKey}"/> 在 M10 受支持。
    /// </summary>
    /// <param name="collectionId">集合 GUID。</param>
    /// <param name="index">内存中的 Flat 索引。</param>
    /// <param name="encodedPayloadProvider">M11：可选回调，根据键返回该行已编码的 payload 字节序列；
    /// 返回 <see langword="null"/> 表示该行无 payload。本参数本身为 <see langword="null"/> 时不写出 <c>payload.bin</c>。</param>
    internal void FlushCollection<TKey>(
        Guid collectionId,
        FlatIndex<TKey> index,
        Func<TKey, byte[]?>? encodedPayloadProvider = null) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(index);
        lock (_lock)
        {
            ThrowIfDisposed();

            CatalogEntry entry = _entries.FirstOrDefault(e => e.CollectionId == collectionId)
                ?? throw new DotVectorException($"集合 {collectionId} 未在 catalog 中注册。");

            // 1. 旋转 WAL：当前文件成为"已封闭"的 closedSeq；新写入进入 closedSeq + 1
            long closedSeq = _currentWalSeq;
            long newSeq = closedSeq + 1;
            EnsureWalWriter().Rotate(GetWalPath(newSeq));
            _currentWalSeq = newSeq;

            // 2. 快照索引（仅 delta：自上次 Flush 起新增的行）
            int prevFlushed = _flushedRows.TryGetValue(collectionId, out int p) ? p : 0;
            index.SnapshotSince(prevFlushed, out List<TKey> keys, out float[] vectors, out int newFlushed);

            // 若没有新增行，仍写一个空 Segment（用于推进 LastCoveredWalSequence + 触发 WAL 旋转/裁剪）。

            // 3. 选择 segment 序号
            CollectionManifest manifest = _manifests.TryGetValue(collectionId, out var m)
                ? m
                : CollectionManifestStore.Read(GetManifestPath(collectionId));
            ulong segSeq = manifest.NextSegmentSequence == 0 ? 1UL : manifest.NextSegmentSequence;

            // 4. 写 Segment（即便 keys 为空也写出来）
            SegmentHeader header = new()
            {
                SequenceNumber = segSeq,
                VectorCount = (uint)keys.Count,
                Dimensions = (uint)entry.Dimensions,
                Metric = (byte)entry.Metric,
                CreatedAtUtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Reserved = default,
            };
            string segDir = GetSegmentDir(collectionId, (long)segSeq);
            byte[]?[]? payloadsArr = null;
            if (encodedPayloadProvider is not null && keys.Count > 0)
            {
                payloadsArr = new byte[]?[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    payloadsArr[i] = encodedPayloadProvider(keys[i]);
                }
            }
            SegmentWriter.Write(segDir, header, keys, vectors, payloadsArr);

            // 5. 更新 manifest + 已 flush 行数
            manifest.NextSegmentSequence = segSeq + 1;
            manifest.LastCoveredWalSequence = (ulong)closedSeq;
            CollectionManifestStore.Write(GetManifestPath(collectionId), manifest);
            _manifests[collectionId] = manifest;
            _flushedRows[collectionId] = newFlushed;

            // 6. 尝试裁剪 WAL
            TryTrimWal();
        }
    }

    /// <summary>
    /// 把集合的 Vamana 索引快照写成新 Segment（包含 <c>vamana.bin</c> 图文件）并旋转 WAL。
    /// </summary>
    /// <remarks>
    /// 与 Flat 的 delta 写入不同，Vamana 每次 Flush 写出"全量"快照（key+vector+graph），
    /// 并删除该集合先前所有 Segment 目录，使最新 Segment 即为完整状态。
    /// 这样可避免在恢复时合并多 segment 图结构。
    /// </remarks>
    internal void FlushVamanaCollection<TKey>(
        Guid collectionId,
        VamanaIndex<TKey> index,
        VamanaOptions options,
        Func<TKey, byte[]?>? encodedPayloadProvider = null) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(options);
        lock (_lock)
        {
            ThrowIfDisposed();

            CatalogEntry entry = _entries.FirstOrDefault(e => e.CollectionId == collectionId)
                ?? throw new DotVectorException($"集合 {collectionId} 未在 catalog 中注册。");

            // 1. 旋转 WAL
            long closedSeq = _currentWalSeq;
            long newSeq = closedSeq + 1;
            EnsureWalWriter().Rotate(GetWalPath(newSeq));
            _currentWalSeq = newSeq;

            // 2. 全量快照
            index.Snapshot(
                out List<TKey> keys,
                out float[] vectors,
                out int entryPoint,
                out List<int[]> neighbors,
                out HashSet<int> tombstones);

            // 3. 选择新 segment 序号
            CollectionManifest manifest = _manifests.TryGetValue(collectionId, out var m)
                ? m
                : CollectionManifestStore.Read(GetManifestPath(collectionId));
            ulong segSeq = manifest.NextSegmentSequence == 0 ? 1UL : manifest.NextSegmentSequence;

            // 4. 写 Segment（vectors.bin + keys.bin + payload.bin）
            SegmentHeader header = new()
            {
                SequenceNumber = segSeq,
                VectorCount = (uint)keys.Count,
                Dimensions = (uint)entry.Dimensions,
                Metric = (byte)entry.Metric,
                CreatedAtUtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Reserved = default,
            };
            string segDir = GetSegmentDir(collectionId, (long)segSeq);
            byte[]?[]? payloadsArr = null;
            if (encodedPayloadProvider is not null && keys.Count > 0)
            {
                payloadsArr = new byte[]?[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    payloadsArr[i] = encodedPayloadProvider(keys[i]);
                }
            }
            SegmentWriter.Write(segDir, header, keys, vectors, payloadsArr);

            // 5. 写 vamana.bin
            string vamanaPath = Path.Combine(segDir, "vamana.bin");
            VamanaGraphIO.Write(vamanaPath, entry.Dimensions, entry.Metric, options, entryPoint, neighbors, tombstones);

            // 6. 删除旧 Segment 目录（保留刚写入的新 segment）
            string segsDir = GetSegmentsDir(collectionId);
            string newSegName = Path.GetFileName(segDir);
            foreach (string oldDir in Directory.EnumerateDirectories(segsDir, "seg-*"))
            {
                if (oldDir.EndsWith(".tmp", StringComparison.Ordinal)) { continue; }
                if (string.Equals(Path.GetFileName(oldDir), newSegName, StringComparison.Ordinal))
                {
                    continue;
                }
                try { Directory.Delete(oldDir, recursive: true); }
                catch (IOException) { /* 占用时跳过 */ }
            }

            // 7. 更新 manifest（Vamana 不维护 _flushedRows，因为是全量）
            manifest.NextSegmentSequence = segSeq + 1;
            manifest.LastCoveredWalSequence = (ulong)closedSeq;
            CollectionManifestStore.Write(GetManifestPath(collectionId), manifest);
            _manifests[collectionId] = manifest;
            _flushedRows[collectionId] = keys.Count;

            // 8. 尝试裁剪 WAL
            TryTrimWal();
        }
    }

    /// <summary>
    /// 把指定集合的所有 Segment 合并为一个新的 Segment（按 seq 顺序拼接）。
    /// 不影响 <see cref="CollectionManifest.LastCoveredWalSequence"/>。
    /// </summary>
    internal void CompactCollection<TKey>(Guid collectionId) where TKey : notnull
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            CatalogEntry entry = _entries.FirstOrDefault(e => e.CollectionId == collectionId)
                ?? throw new DotVectorException($"集合 {collectionId} 未在 catalog 中注册。");

            string segsDir = GetSegmentsDir(collectionId);
            if (!Directory.Exists(segsDir)) { return; }

            string[] dirs = Directory.GetDirectories(segsDir, "seg-*")
                .Where(d => !d.EndsWith(".tmp", StringComparison.Ordinal))
                .ToArray();
            if (dirs.Length <= 1) { return; }
            Array.Sort(dirs, StringComparer.Ordinal);

            List<TKey> mergedKeys = new();
            List<float> mergedVectors = new();
            List<byte[]?> mergedPayloads = new();
            bool anyPayload = false;
            foreach (string dir in dirs)
            {
                using SegmentReader<TKey> reader = SegmentReader<TKey>.Open(dir);
                IReadOnlyList<byte[]?>? segPayloads = reader.EncodedPayloads;
                int n = reader.Keys.Count;
                for (int i = 0; i < n; i++)
                {
                    mergedKeys.Add(reader.Keys[i]);
                    byte[]? p = segPayloads is null ? null : segPayloads[i];
                    if (p is { Length: > 0 }) { anyPayload = true; }
                    mergedPayloads.Add(p);
                }
                mergedVectors.AddRange(reader.ReadAllVectors());
            }

            CollectionManifest manifest = _manifests.TryGetValue(collectionId, out var m)
                ? m
                : CollectionManifestStore.Read(GetManifestPath(collectionId));
            ulong newSegSeq = manifest.NextSegmentSequence == 0 ? 1UL : manifest.NextSegmentSequence;

            SegmentHeader header = new()
            {
                SequenceNumber = newSegSeq,
                VectorCount = (uint)mergedKeys.Count,
                Dimensions = (uint)entry.Dimensions,
                Metric = (byte)entry.Metric,
                CreatedAtUtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Reserved = default,
            };
            string newSegDir = GetSegmentDir(collectionId, (long)newSegSeq);
            float[] vectorArr = mergedVectors.ToArray();
            SegmentWriter.Write(newSegDir, header, mergedKeys, vectorArr,
                anyPayload ? mergedPayloads : null);

            manifest.NextSegmentSequence = newSegSeq + 1;
            CollectionManifestStore.Write(GetManifestPath(collectionId), manifest);
            _manifests[collectionId] = manifest;

            // 删除旧 Segment 目录
            foreach (string dir in dirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { /* 占用时跳过；下次启动可清理 */ }
            }
        }
    }

    /// <summary>
    /// 删除所有集合的 <see cref="CollectionManifest.LastCoveredWalSequence"/>
    /// 都已覆盖的旧 WAL 文件。永远不删除当前正在写入的 WAL（_currentWalSeq）。
    /// </summary>
    private void TryTrimWal()
    {
        ulong minCovered;
        if (_manifests.Count == 0)
        {
            minCovered = (ulong)Math.Max(0, _currentWalSeq - 1);
        }
        else
        {
            minCovered = ulong.MaxValue;
            foreach (CollectionManifest m in _manifests.Values)
            {
                if (m.LastCoveredWalSequence < minCovered)
                {
                    minCovered = m.LastCoveredWalSequence;
                }
            }
        }
        if (minCovered == 0) { return; }

        string walDir = Path.Combine(_root, "wal");
        if (!Directory.Exists(walDir)) { return; }
        foreach (string file in Directory.EnumerateFiles(walDir, "wal-*.log"))
        {
            long seq = TryParseWalSeq(Path.GetFileName(file));
            if (seq <= 0) { continue; }
            if (seq >= _currentWalSeq) { continue; }
            if ((ulong)seq <= minCovered)
            {
                try { File.Delete(file); }
                catch (IOException) { /* 占用则保留 */ }
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

    /// <summary>WAL 写入观察者实现：转发到 PersistentDirectory 的串行化追加方法。</summary>
    private sealed class WalSink<TKey> : IWriteSink<TKey>, IDisposable where TKey : notnull
    {
        private readonly PersistentDirectory _owner;
        private readonly Guid _collectionId;

        public WalSink(PersistentDirectory owner, Guid collectionId)
        {
            _owner = owner;
            _collectionId = collectionId;
        }

        public void OnInsert(TKey key, ReadOnlySpan<float> vector)
            => _owner.AppendInsert(_collectionId, key, vector);

        public void OnDelete(TKey key)
            => _owner.AppendDelete(_collectionId, key);

        public void OnPayload(TKey key, ReadOnlySpan<byte> encodedPayload)
            => _owner.AppendPayload(_collectionId, key, encodedPayload);

        public void Dispose() { /* WalWriter 由 PersistentDirectory 拥有 */ }
    }
}
