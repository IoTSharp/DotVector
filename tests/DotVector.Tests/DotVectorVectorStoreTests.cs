using DotVector.Data;
using DotVector.Data.Internal;
using Microsoft.Extensions.VectorData;

namespace DotVector.Tests;

/// <summary>
/// DotVector.Data 适配层（M7）的集成测试。
/// </summary>
public sealed class DotVectorVectorStoreTests
{
    private sealed class TestRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Embedding { get; set; }

        [VectorStoreData]
        public string? Title { get; set; }
    }

    private static (InMemoryDotVectorClient client, VectorStoreCollection<string, TestRecord> col) NewCollection()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = store.GetCollection<string, TestRecord>("test");
        return (client, col);
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_SendsCreateCollectionRequest()
    {
        var (client, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        Assert.Equal(1, client.CreateCollectionCalls);
        Assert.Contains("test", client.CollectionNames);
    }

    [Fact]
    public async Task UpsertAndSearch_RoundTripsRecord()
    {
        var (client, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();

        var rec = new TestRecord
        {
            Id = "r1",
            Embedding = new float[] { 1f, 0f, 0f },
            Title = "hello",
        };
        await col.UpsertAsync(rec);
        Assert.Equal(1, client.RecordCount("test"));

        var hits = new List<VectorSearchResult<TestRecord>>();
        await foreach (var h in col.SearchAsync(new float[] { 1f, 0f, 0f }, top: 5))
        {
            hits.Add(h);
        }
        Assert.Single(hits);
        Assert.Equal("r1", hits[0].Record.Id);
        Assert.Equal("hello", hits[0].Record.Title);
    }

    [Fact]
    public async Task UpsertBatch_PersistsAllRecords()
    {
        var (client, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();

        var records = new[]
        {
            new TestRecord { Id = "a", Embedding = new float[] { 1f, 0f, 0f }, Title = "A" },
            new TestRecord { Id = "b", Embedding = new float[] { 0f, 1f, 0f }, Title = "B" },
            new TestRecord { Id = "c", Embedding = new float[] { 0f, 0f, 1f }, Title = "C" },
        };
        await col.UpsertAsync(records);
        Assert.Equal(3, client.RecordCount("test"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var (client, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new TestRecord { Id = "x", Embedding = new float[] { 1f, 0f, 0f } });
        await col.DeleteAsync("x");
        Assert.Equal(0, client.RecordCount("test"));
    }

    [Fact]
    public async Task EnsureCollectionDeletedAsync_RemovesCollection()
    {
        var (client, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.EnsureCollectionDeletedAsync();
        Assert.DoesNotContain("test", client.CollectionNames);
    }

    [Fact]
    public async Task SearchAsync_WithIncludeVectors_ReturnsVector()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new TestRecord
        {
            Id = "v1",
            Embedding = new float[] { 1f, 0f, 0f },
            Title = "with-vec",
        });

        var hits = new List<VectorSearchResult<TestRecord>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 1,
            new VectorSearchOptions<TestRecord> { IncludeVectors = true }))
        {
            hits.Add(h);
        }
        Assert.Single(hits);
        var vec = hits[0].Record.Embedding.ToArray();
        Assert.Equal(new float[] { 1f, 0f, 0f }, vec);
    }

    [Fact]
    public async Task SearchAsync_WithoutIncludeVectors_ReturnsEmptyVector()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new TestRecord
        {
            Id = "v2",
            Embedding = new float[] { 1f, 0f, 0f },
        });

        var hits = new List<VectorSearchResult<TestRecord>>();
        await foreach (var h in col.SearchAsync(new float[] { 1f, 0f, 0f }, top: 1))
        {
            hits.Add(h);
        }
        Assert.Single(hits);
        Assert.Equal(0, hits[0].Record.Embedding.Length);
    }

    [Fact]
    public async Task GetAsync_ByKey_ReturnsRecord()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new TestRecord
        {
            Id = "g1",
            Embedding = new float[] { 1f, 0f, 0f },
            Title = "got",
        });

        var got = await col.GetAsync("g1");
        Assert.NotNull(got);
        Assert.Equal("g1", got!.Id);
        Assert.Equal("got", got.Title);
        // 默认 IncludeVectors=false，向量不返回
        Assert.Equal(0, got.Embedding.Length);
    }

    [Fact]
    public async Task GetAsync_ByKey_WithIncludeVectors_ReturnsVector()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new TestRecord
        {
            Id = "g2",
            Embedding = new float[] { 0f, 1f, 0f },
        });

        var got = await col.GetAsync("g2", new RecordRetrievalOptions { IncludeVectors = true });
        Assert.NotNull(got);
        Assert.Equal(new float[] { 0f, 1f, 0f }, got!.Embedding.ToArray());
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        var got = await col.GetAsync("missing");
        Assert.Null(got);
    }

    [Fact]
    public async Task GetAsync_ByKeys_ReturnsMatchingRecords()
    {
        var (_, col) = NewCollection();
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new[]
        {
            new TestRecord { Id = "k1", Embedding = new float[] { 1f, 0f, 0f }, Title = "T1" },
            new TestRecord { Id = "k2", Embedding = new float[] { 0f, 1f, 0f }, Title = "T2" },
            new TestRecord { Id = "k3", Embedding = new float[] { 0f, 0f, 1f }, Title = "T3" },
        });

        var got = new List<TestRecord>();
        await foreach (var r in col.GetAsync(new[] { "k1", "k3", "missing" }))
        {
            got.Add(r);
        }
        Assert.Equal(2, got.Count);
        Assert.Contains(got, r => r.Id == "k1" && r.Title == "T1");
        Assert.Contains(got, r => r.Id == "k3" && r.Title == "T3");
    }

    [Fact]
    public void DistanceFunctionMapper_HandlesAllKnownValues()
    {
        Assert.Equal("Cosine", DistanceFunctionMapper.ToDotVectorMetric(null));
        Assert.Equal("Cosine", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.CosineSimilarity));
        Assert.Equal("Cosine", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.CosineDistance));
        Assert.Equal("L2", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.EuclideanDistance));
        Assert.Equal("L2", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.EuclideanSquaredDistance));
        Assert.Equal("DotProduct", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.DotProductSimilarity));
        Assert.Equal("InnerProduct", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.NegativeDotProductSimilarity));
        Assert.Equal("Hamming", DistanceFunctionMapper.ToDotVectorMetric(DistanceFunction.HammingDistance));
        Assert.Throws<NotSupportedException>(() => DistanceFunctionMapper.ToDotVectorMetric("UnknownMetric"));
    }

    [Fact]
    public void KeyConverter_RoundTripsSupportedTypes()
    {
        Assert.Equal("hello", KeyConverter<string>.ToProtocolId("hello"));
        Assert.Equal("hello", KeyConverter<string>.FromProtocolId("hello"));

        Assert.Equal("42", KeyConverter<int>.ToProtocolId(42));
        Assert.Equal(42, KeyConverter<int>.FromProtocolId("42"));

        Assert.Equal("123456789012", KeyConverter<long>.ToProtocolId(123456789012L));
        Assert.Equal(123456789012L, KeyConverter<long>.FromProtocolId("123456789012"));

        var g = Guid.NewGuid();
        Assert.Equal(g.ToString("D"), KeyConverter<Guid>.ToProtocolId(g));
        Assert.Equal(g, KeyConverter<Guid>.FromProtocolId(g.ToString("D")));
    }

    [Fact]
    public void VectorStore_GetService_ReturnsMetadata()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var meta = store.GetService(typeof(VectorStoreMetadata)) as VectorStoreMetadata;
        Assert.NotNull(meta);
        Assert.Equal("dotvector", meta!.VectorStoreSystemName);
    }
}
