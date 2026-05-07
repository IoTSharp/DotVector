using DotVector.Data;
using Microsoft.Extensions.VectorData;

namespace DotVector.Tests;

/// <summary>
/// M7.3 测试：ListCollectionNames、GetDynamicCollection、显式 Definition vs 反射推断的等价性。
/// </summary>
public sealed class M7_3_DynamicAndListTests
{
    // ------------- 用于反射映射对照的强类型记录 -------------

    private sealed class TypedRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Embedding { get; set; }

        [VectorStoreData]
        public string? Title { get; set; }
    }

    /// <summary>构造一个与 <see cref="TypedRecord"/> 等价的 Definition。</summary>
    private static VectorStoreCollectionDefinition BuildEquivalentDefinition() => new()
    {
        Properties =
        {
            new VectorStoreKeyProperty("Id", typeof(string)),
            new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), 3)
            {
                DistanceFunction = DistanceFunction.CosineSimilarity,
            },
            new VectorStoreDataProperty("Title", typeof(string)),
        },
    };

    // ------------- ListCollectionNames -------------

    [Fact]
    public async Task ListCollectionNamesAsync_ReturnsAllCreatedCollections()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);

        await store.GetCollection<string, TypedRecord>("col-a").EnsureCollectionExistsAsync();
        await store.GetCollection<string, TypedRecord>("col-b").EnsureCollectionExistsAsync();
        await store.GetCollection<string, TypedRecord>("col-c").EnsureCollectionExistsAsync();

        var names = new List<string>();
        await foreach (var n in store.ListCollectionNamesAsync())
        {
            names.Add(n);
        }

        Assert.Equal(3, names.Count);
        Assert.Contains("col-a", names);
        Assert.Contains("col-b", names);
        Assert.Contains("col-c", names);
    }

    [Fact]
    public async Task CollectionExistsAsync_ReturnsTrueOnlyAfterCreate()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);

        Assert.False(await store.CollectionExistsAsync("c1"));
        await store.GetCollection<string, TypedRecord>("c1").EnsureCollectionExistsAsync();
        Assert.True(await store.CollectionExistsAsync("c1"));
        Assert.False(await store.CollectionExistsAsync("c2"));
    }

    // ------------- GetDynamicCollection 端到端 -------------

    [Fact]
    public async Task DynamicCollection_UpsertAndSearch_RoundTripsRecord()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var def = BuildEquivalentDefinition();

        var col = store.GetDynamicCollection("dyn", def);
        await col.EnsureCollectionExistsAsync();

        var rec = new Dictionary<string, object?>
        {
            ["Id"] = "r1",
            ["Embedding"] = new float[] { 1f, 0f, 0f },
            ["Title"] = "hello",
        };
        await col.UpsertAsync(rec);
        Assert.Equal(1, client.RecordCount("dyn"));

        var hits = new List<VectorSearchResult<Dictionary<string, object?>>>();
        await foreach (var h in col.SearchAsync(new float[] { 1f, 0f, 0f }, top: 5))
        {
            hits.Add(h);
        }
        Assert.Single(hits);
        Assert.Equal("r1", hits[0].Record["Id"]);
        Assert.Equal("hello", hits[0].Record["Title"]);
    }

    [Fact]
    public async Task DynamicCollection_GetAsync_ReturnsDictionaryWithVectorWhenIncluded()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = store.GetDynamicCollection("dyn-get", BuildEquivalentDefinition());
        await col.EnsureCollectionExistsAsync();

        await col.UpsertAsync(new Dictionary<string, object?>
        {
            ["Id"] = "k1",
            ["Embedding"] = new float[] { 0.1f, 0.2f, 0.3f },
            ["Title"] = "doc",
        });

        var record = await col.GetAsync("k1", new RecordRetrievalOptions { IncludeVectors = true });
        Assert.NotNull(record);
        Assert.Equal("k1", record!["Id"]);
        Assert.Equal("doc", record["Title"]);
        var vec = Assert.IsType<float[]>(record["Embedding"]);
        Assert.Equal(3, vec.Length);
    }

    [Fact]
    public async Task DynamicCollection_DeleteAsync_RemovesRecord()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = store.GetDynamicCollection("dyn-del", BuildEquivalentDefinition());
        await col.EnsureCollectionExistsAsync();

        await col.UpsertAsync(new Dictionary<string, object?>
        {
            ["Id"] = "z",
            ["Embedding"] = new float[] { 1f, 0f, 0f },
        });
        Assert.Equal(1, client.RecordCount("dyn-del"));

        await col.DeleteAsync("z");
        Assert.Equal(0, client.RecordCount("dyn-del"));
    }

    [Fact]
    public async Task DynamicCollection_MissingKeyField_Throws()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = store.GetDynamicCollection("dyn-x", BuildEquivalentDefinition());
        await col.EnsureCollectionExistsAsync();

        var bad = new Dictionary<string, object?>
        {
            ["Embedding"] = new float[] { 1f, 0f, 0f },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => col.UpsertAsync(bad));
    }

    // ------------- Definition vs 反射 等价性（参数化）-------------

    public static TheoryData<bool> UseDefinitionMatrix => new() { false, true };

    [Theory]
    [MemberData(nameof(UseDefinitionMatrix))]
    public async Task TypedCollection_DefinitionAndReflection_ProduceSameSearchResults(bool useDefinition)
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = useDefinition
            ? store.GetCollection<string, TypedRecord>("eq", BuildEquivalentDefinition())
            : store.GetCollection<string, TypedRecord>("eq");

        await col.EnsureCollectionExistsAsync();

        await col.UpsertAsync(new[]
        {
            new TypedRecord { Id = "a", Embedding = new float[] { 1f, 0f, 0f }, Title = "Alpha" },
            new TypedRecord { Id = "b", Embedding = new float[] { 0f, 1f, 0f }, Title = "Bravo" },
            new TypedRecord { Id = "c", Embedding = new float[] { 0f, 0f, 1f }, Title = "Charlie" },
        });

        var hits = new List<VectorSearchResult<TypedRecord>>();
        await foreach (var h in col.SearchAsync(new float[] { 1f, 0f, 0f }, top: 3))
        {
            hits.Add(h);
        }
        Assert.Equal(3, hits.Count);
        Assert.Equal("a", hits[0].Record.Id);
        Assert.Equal("Alpha", hits[0].Record.Title);
    }
}
