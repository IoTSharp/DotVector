using DotVector.Catalog;
using DotVector.Exceptions;
using DotVector.IO;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 catalog.bin 的写入/读取与格式校验。
/// </summary>
public sealed class CatalogStoreTests : IDisposable
{
    private readonly string _dir;

    public CatalogStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dotvec-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void WriteThenRead_PreservesAllFields()
    {
        string path = Path.Combine(_dir, "catalog.bin");
        var entries = new List<CatalogEntry>
        {
            new() { CollectionId = Guid.NewGuid(), Name = "users", Dimensions = 384, KeyType = KeyTypeCode.Int64, IndexKind = IndexKind.Hnsw, Metric = Metric.Cosine },
            new() { CollectionId = Guid.NewGuid(), Name = "docs-中文", Dimensions = 1536, KeyType = KeyTypeCode.Guid, IndexKind = IndexKind.Flat, Metric = Metric.L2 },
        };
        CatalogStore.Write(path, entries);

        var loaded = CatalogStore.Read(path);
        Assert.Equal(2, loaded.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].CollectionId, loaded[i].CollectionId);
            Assert.Equal(entries[i].Name, loaded[i].Name);
            Assert.Equal(entries[i].Dimensions, loaded[i].Dimensions);
            Assert.Equal(entries[i].KeyType, loaded[i].KeyType);
            Assert.Equal(entries[i].IndexKind, loaded[i].IndexKind);
            Assert.Equal(entries[i].Metric, loaded[i].Metric);
        }
    }

    [Fact]
    public void Read_NonexistentFile_ReturnsEmpty()
    {
        Assert.Empty(CatalogStore.Read(Path.Combine(_dir, "missing.bin")));
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        string path = Path.Combine(_dir, "catalog.bin");
        File.WriteAllBytes(path, new byte[64]); // 全 0 → magic 不匹配
        Assert.Throws<DotVectorException>(() => CatalogStore.Read(path));
    }

    [Fact]
    public void Write_OverwritesAtomically()
    {
        string path = Path.Combine(_dir, "catalog.bin");
        var first = new List<CatalogEntry>
        {
            new() { CollectionId = Guid.NewGuid(), Name = "a", Dimensions = 8, KeyType = KeyTypeCode.Int32, IndexKind = IndexKind.Flat, Metric = Metric.Cosine },
        };
        CatalogStore.Write(path, first);

        var second = new List<CatalogEntry>
        {
            new() { CollectionId = Guid.NewGuid(), Name = "b", Dimensions = 16, KeyType = KeyTypeCode.String, IndexKind = IndexKind.IvfFlat, Metric = Metric.InnerProduct },
        };
        CatalogStore.Write(path, second);

        var loaded = CatalogStore.Read(path);
        Assert.Single(loaded);
        Assert.Equal("b", loaded[0].Name);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
