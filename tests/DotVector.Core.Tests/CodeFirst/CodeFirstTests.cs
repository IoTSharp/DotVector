using DotVector.Api;
using DotVector.CodeFirst;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Core.Tests.CodeFirst;

public sealed class CodeFirstTests
{
    [Fact]
    public void BindSets_FromAttributes_CreatesCollectionAndSearches()
    {
        using var db = new VectorDatabase();
        using var context = new AttributeContext(db);

        context.Articles.Insert(new Article
        {
            Id = "doc-1",
            Title = "alpha",
            Category = "guide",
            Text = new float[] { 1f, 0f, 0f },
        });
        context.Articles.Insert(new Article
        {
            Id = "doc-2",
            Title = "beta",
            Category = "reference",
            Text = new float[] { 0f, 1f, 0f },
        });

        IReadOnlyList<DotVectorSearchResult> results = context.Articles.Search(
            new float[] { 1f, 0f, 0f },
            topK: 1);

        Assert.True(db.HasCollection("Articles"));
        Assert.Single(results);
        Assert.Equal("doc-1", results[0].Key);
        Assert.Equal("alpha", results[0].Payload!["Title"]);
    }

    [Fact]
    public void MultiVectorEntity_MapsEachVectorFieldToSeparateCollection()
    {
        using var db = new VectorDatabase();
        using var context = new MultiVectorContext(db);

        context.Assets.Insert(new Asset
        {
            Id = 1,
            Name = "pump",
            TextVector = new float[] { 1f, 0f },
            ImageVector = new float[] { 0f, 1f },
        });
        context.Assets.Insert(new Asset
        {
            Id = 2,
            Name = "valve",
            TextVector = new float[] { 0f, 1f },
            ImageVector = new float[] { 1f, 0f },
        });

        IReadOnlyList<DotVectorSearchResult> textResults = context.Assets.Search(
            new float[] { 1f, 0f },
            topK: 1,
            vectorFieldName: "text");
        IReadOnlyList<DotVectorSearchResult> imageResults = context.Assets.Search(
            new float[] { 1f, 0f },
            topK: 1,
            vectorFieldName: "image");

        Assert.True(db.HasCollection("asset_text"));
        Assert.True(db.HasCollection("asset_image"));
        Assert.Equal(1, textResults[0].Key);
        Assert.Equal(2, imageResults[0].Key);

        Collection<int> text = db.GetCollection<int>("asset_text");
        Collection<int> image = db.GetCollection<int>("asset_image");
        Assert.Equal(IndexKind.Hnsw, text.IndexKind);
        Assert.Equal(IndexKind.Vamana, image.IndexKind);
        Assert.Equal(Metric.Cosine, text.Metric);
        Assert.Equal(Metric.L2, image.Metric);
    }

    [Fact]
    public void Set_WithExplicitSchema_UsesAotFriendlyRegistration()
    {
        using var db = new VectorDatabase();
        using var context = new ExplicitContext(db);
        DotVectorSet<ExplicitDoc> docs = context.Set<ExplicitDoc>();

        docs.Insert(new ExplicitDoc(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "green",
            new ReadOnlyMemory<float>(new float[] { 0f, 1f })));
        docs.Insert(new ExplicitDoc(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "red",
            new ReadOnlyMemory<float>(new float[] { 1f, 0f })));

        IReadOnlyList<DotVectorSearchResult> results = docs.Search(
            new float[] { 1f, 0f },
            topK: 5,
            filter: Filter.Eq("Kind", "red"));

        Assert.True(db.HasCollection("explicit_docs"));
        Assert.Single(results);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), results[0].Key);
        Assert.Equal("red", results[0].Payload!["Kind"]);
    }

    [Fact]
    public void ConvenienceApi_Upsert_Find_Get_SearchTop1_And_Threshold_Work()
    {
        using var db = new VectorDatabase();
        using var context = new AttributeContext(db);

        context.Articles.Upsert(new Article
        {
            Id = "doc-1",
            Title = "alpha",
            Category = "guide",
            Text = new float[] { 1f, 0f, 0f },
        });
        context.Articles.Upsert(new Article
        {
            Id = "doc-2",
            Title = "beta",
            Category = "reference",
            Text = new float[] { 0f, 1f, 0f },
        });
        context.Articles.Upsert(new Article
        {
            Id = "doc-1",
            Title = "alpha-updated",
            Category = "guide",
            Text = new float[] { 1f, 0f, 0f },
        });

        DotVectorRecordResult? found = context.Articles.Find("doc-1");
        DotVectorRecordResult got = context.Articles.Get("doc-1");
        DotVectorSearchResult? top1 = context.Articles.SearchTop1(
            new float[] { 1f, 0f, 0f },
            filter: Filter.Eq("Category", "guide"));
        IReadOnlyList<DotVectorSearchResult> thresholdResults = context.Articles.SearchByThreshold(
            new float[] { 1f, 0f, 0f },
            threshold: 0.01f,
            topK: 2);

        Assert.NotNull(found);
        Assert.Equal("alpha-updated", found.Payload!["Title"]);
        Assert.Equal("doc-1", got.Key);
        Assert.Equal(new float[] { 1f, 0f, 0f }, got.Vector);
        Assert.NotNull(top1);
        Assert.Equal("doc-1", top1.Key);
        Assert.Single(thresholdResults);
        Assert.Equal("doc-1", thresholdResults[0].Key);
        Assert.Null(context.Articles.Find("missing"));
        Assert.Throws<KeyNotFoundException>(() => context.Articles.Get("missing"));
    }

    [Fact]
    public void ConvenienceApi_SelectsMultiVectorFieldBySelectorOrName()
    {
        using var db = new VectorDatabase();
        using var context = new MultiVectorContext(db);

        context.Assets.Upsert(new Asset
        {
            Id = 1,
            Name = "pump",
            TextVector = new float[] { 1f, 0f },
            ImageVector = new float[] { 0f, 1f },
        });
        context.Assets.Upsert(new Asset
        {
            Id = 2,
            Name = "valve",
            TextVector = new float[] { 0f, 1f },
            ImageVector = new float[] { 1f, 0f },
        });

        DotVectorSearchResult? textResult = context.Assets.SearchTop1(
            new float[] { 1f, 0f },
            asset => asset.TextVector);
        IReadOnlyList<DotVectorSearchResult> imageResults = context.Assets.SearchByThreshold(
            new float[] { 1f, 0f },
            threshold: 0.01f,
            topK: 2,
            asset => asset.ImageVector);
        IReadOnlyList<DotVectorSearchResult> namedResults = context.Assets.Search(
            new float[] { 1f, 0f },
            topK: 1,
            vectorFieldName: "image");

        Assert.NotNull(textResult);
        Assert.Equal(1, textResult.Key);
        Assert.Equal("text", textResult.VectorFieldName);
        Assert.Single(imageResults);
        Assert.Equal(2, imageResults[0].Key);
        Assert.Equal("image", imageResults[0].VectorFieldName);
        Assert.Equal(2, namedResults[0].Key);
    }

    [Fact]
    public void ConvenienceApi_SelectorWorksWithExplicitSingleVectorSchema()
    {
        using var db = new VectorDatabase();
        using var context = new ExplicitContext(db);
        DotVectorSet<ExplicitDoc> docs = context.Set<ExplicitDoc>();

        docs.Upsert(new ExplicitDoc(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "green",
            new ReadOnlyMemory<float>(new float[] { 0f, 1f })));

        DotVectorSearchResult? result = docs.SearchTop1(
            new float[] { 0f, 1f },
            doc => doc.Embedding,
            Filter.Eq("Kind", "green"));

        Assert.NotNull(result);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), result.Key);
    }

    [Fact]
    public void Set_WithoutExplicitSchema_ThrowsClearAotMessage()
    {
        using var db = new VectorDatabase();
        using var context = new EmptyContext(db);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.Set<Article>());
        Assert.Contains("尚未注册 Code-First schema", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_MultipleVectorsWithoutFieldName_Throws()
    {
        using var db = new VectorDatabase();
        using var context = new MultiVectorContext(db);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            context.Assets.Search(new float[] { 1f, 0f }, topK: 1));
        Assert.Contains("多个向量字段", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Insert_WithWrongVectorDimensions_Throws()
    {
        using var db = new VectorDatabase();
        using var context = new AttributeContext(db);

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            context.Articles.Insert(new Article
            {
                Id = "bad",
                Title = "bad",
                Category = "bad",
                Text = new float[] { 1f, 0f },
            }));

        Assert.Contains("维度不匹配", ex.Message, StringComparison.Ordinal);
    }

    private sealed class AttributeContext : DotVectorDbContext
    {
        public AttributeContext(VectorDatabase database)
            : base(database)
        {
            BindSets();
        }

        public DotVectorSet<Article> Articles { get; private set; } = null!;
    }

    private sealed class MultiVectorContext : DotVectorDbContext
    {
        public MultiVectorContext(VectorDatabase database)
            : base(database)
        {
            BindSets();
        }

        public DotVectorSet<Asset> Assets { get; private set; } = null!;
    }

    private sealed class ExplicitContext : DotVectorDbContext
    {
        public ExplicitContext(VectorDatabase database)
            : base(database)
        {
            RegisterSchema(DotVectorEntitySchema.Create<ExplicitDoc, Guid>(
                keyGetter: static doc => doc.Id,
                vectorGetter: static doc => doc.Embedding,
                dimensions: 2,
                payloadGetter: static doc => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Kind"] = doc.Kind,
                },
                setName: "ExplicitDocs",
                collectionName: "explicit_docs"));
        }
    }

    private sealed class EmptyContext : DotVectorDbContext
    {
        public EmptyContext(VectorDatabase database)
            : base(database)
        {
        }
    }

    private sealed class Article
    {
        [DotVectorKey]
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        [DotVectorVector(3, Metric = Metric.L2)]
        public float[] Text { get; init; } = [];
    }

    private sealed class Asset
    {
        [DotVectorKey]
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        [DotVectorVector(2, Name = "text", CollectionName = "asset_text", Metric = Metric.Cosine)]
        [DotVectorIndex(IndexKind.Hnsw, HnswM = 4, EfConstruction = 8, EfSearch = 8, Seed = 17)]
        public float[] TextVector { get; init; } = [];

        [DotVectorVector(2, Name = "image", CollectionName = "asset_image", Metric = Metric.L2)]
        [DotVectorIndex(IndexKind.Vamana, MaxDegree = 4, SearchListSize = 8, BeamWidth = 2, Seed = 17)]
        public float[] ImageVector { get; init; } = [];
    }

    private sealed record ExplicitDoc(Guid Id, string Kind, ReadOnlyMemory<float> Embedding);
}
