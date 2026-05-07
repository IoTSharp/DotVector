using DotVector.Data;
using Microsoft.Extensions.VectorData;

namespace DotVector.Tests;

/// <summary>
/// M7.2：LINQ Filter Expression → DotVector Filter IR 翻译，
/// 以及 <see cref="VectorStoreCollection{TKey,TRecord}.SearchAsync"/> /
/// <c>GetAsync(filter, top, ...)</c> 的端到端覆盖。
/// </summary>
public sealed class LinqFilterTests
{
    private sealed class Doc
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(3, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Embedding { get; set; }

        [VectorStoreData]
        public string? Category { get; set; }

        [VectorStoreData]
        public int Score { get; set; }

        [VectorStoreData]
        public bool IsActive { get; set; }
    }

    private static async Task<(InMemoryDotVectorClient client, VectorStoreCollection<string, Doc> col)> NewSeededAsync()
    {
        var client = new InMemoryDotVectorClient();
        var store = new DotVectorVectorStore(client);
        var col = store.GetCollection<string, Doc>("docs");
        await col.EnsureCollectionExistsAsync();
        await col.UpsertAsync(new[]
        {
            new Doc { Id = "1", Embedding = new float[] { 1f, 0f, 0f }, Category = "a", Score = 10, IsActive = true },
            new Doc { Id = "2", Embedding = new float[] { 0f, 1f, 0f }, Category = "a", Score = 20, IsActive = false },
            new Doc { Id = "3", Embedding = new float[] { 0f, 0f, 1f }, Category = "b", Score = 30, IsActive = true },
            new Doc { Id = "4", Embedding = new float[] { 1f, 1f, 0f }, Category = "b", Score = 40, IsActive = false },
        });
        return (client, col);
    }

    [Fact]
    public async Task Search_WithEqualityFilter_ReturnsMatching()
    {
        var (_, col) = await NewSeededAsync();
        var hits = new List<VectorSearchResult<Doc>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 10,
            new VectorSearchOptions<Doc> { Filter = d => d.Category == "b" }))
        {
            hits.Add(h);
        }
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("b", h.Record.Category));
    }

    [Fact]
    public async Task Search_WithRangeFilter_ReturnsMatching()
    {
        var (_, col) = await NewSeededAsync();
        var hits = new List<VectorSearchResult<Doc>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 10,
            new VectorSearchOptions<Doc> { Filter = d => d.Score >= 20 && d.Score < 40 }))
        {
            hits.Add(h);
        }
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.InRange(h.Record.Score, 20, 39));
    }

    [Fact]
    public async Task Search_WithAndOrNot_Combines()
    {
        var (_, col) = await NewSeededAsync();
        var hits = new List<VectorSearchResult<Doc>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 10,
            new VectorSearchOptions<Doc>
            {
                Filter = d => (d.Category == "a" || d.Category == "b") && !(d.Score == 40),
            }))
        {
            hits.Add(h);
        }
        Assert.Equal(3, hits.Count);
        Assert.DoesNotContain(hits, h => h.Record.Score == 40);
    }

    [Fact]
    public async Task Search_WithCapturedConstant_EvaluatesValue()
    {
        var (_, col) = await NewSeededAsync();
        int threshold = 25;
        var hits = new List<VectorSearchResult<Doc>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 10,
            new VectorSearchOptions<Doc> { Filter = d => d.Score > threshold }))
        {
            hits.Add(h);
        }
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.True(h.Record.Score > 25));
    }

    [Fact]
    public async Task Search_WithBoolMember_TreatedAsEqualsTrue()
    {
        var (_, col) = await NewSeededAsync();
        var hits = new List<VectorSearchResult<Doc>>();
        await foreach (var h in col.SearchAsync(
            new float[] { 1f, 0f, 0f },
            top: 10,
            new VectorSearchOptions<Doc> { Filter = d => d.IsActive }))
        {
            hits.Add(h);
        }
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.True(h.Record.IsActive));
    }

    [Fact]
    public async Task GetAsync_WithFilter_YieldsMatchingRecords()
    {
        var (_, col) = await NewSeededAsync();
        var got = new List<Doc>();
        await foreach (var d in col.GetAsync(d => d.Category == "a", top: 10))
        {
            got.Add(d);
        }
        Assert.Equal(2, got.Count);
        Assert.All(got, d => Assert.Equal("a", d.Category));
    }

    [Fact]
    public async Task GetAsync_WithFilter_RespectsTopLimit()
    {
        var (_, col) = await NewSeededAsync();
        var got = new List<Doc>();
        await foreach (var d in col.GetAsync(d => d.Score >= 0, top: 2))
        {
            got.Add(d);
        }
        Assert.Equal(2, got.Count);
    }

    [Fact]
    public async Task GetAsync_WithFilter_IncludeVectors_ReturnsVector()
    {
        var (_, col) = await NewSeededAsync();
        var got = new List<Doc>();
        await foreach (var d in col.GetAsync(
            d => d.Score == 10,
            top: 10,
            new FilteredRecordRetrievalOptions<Doc> { IncludeVectors = true }))
        {
            got.Add(d);
        }
        Assert.Single(got);
        Assert.Equal(new float[] { 1f, 0f, 0f }, got[0].Embedding.ToArray());
    }

    [Fact]
    public async Task GetAsync_WithFilter_DefaultExcludesVectors()
    {
        var (_, col) = await NewSeededAsync();
        var got = new List<Doc>();
        await foreach (var d in col.GetAsync(d => d.Category == "b", top: 10))
        {
            got.Add(d);
        }
        Assert.Equal(2, got.Count);
        Assert.All(got, d => Assert.Equal(0, d.Embedding.Length));
    }

    [Fact]
    public async Task Search_WithKeyOrVectorFilter_Throws()
    {
        var (_, col) = await NewSeededAsync();
        await Assert.ThrowsAnyAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in col.SearchAsync(
                new float[] { 1f, 0f, 0f },
                top: 1,
                new VectorSearchOptions<Doc> { Filter = d => d.Id == "1" }))
            {
            }
        });
    }
}
