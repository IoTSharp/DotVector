using DotVector.Indexing;
using DotVector.Primitives;

namespace DotVector.Core.Tests.Indexing;

public sealed class LocalVectorIndexBuilderTests
{
    [Fact]
    public void Build_FlatIndex_SearchesContinuousPayload()
    {
        using var reader = LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Flat,
            KnnMetric.Cosine,
            new float[]
            {
                1f, 0f,
                0f, 1f,
                -1f, 0f,
            },
            Count: 3,
            Dimension: 2));

        var results = reader.Search(new VectorSearchRequest(new float[] { 1f, 0f }, TopK: 3, KnnMetric.Cosine));

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].PointIndex);
        Assert.InRange(results[0].Distance, 0f, 1e-5f);
        Assert.True(results[0].Distance <= results[1].Distance);
        Assert.True(results[1].Distance <= results[2].Distance);
    }

    [Fact]
    public void Build_FlatIndex_InnerProductUsesLowerIsBetterDistance()
    {
        using var reader = LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Flat,
            KnnMetric.InnerProduct,
            new float[]
            {
                1f, 0f,
                3f, 0f,
                -2f, 0f,
            },
            Count: 3,
            Dimension: 2));

        var results = reader.Search(new VectorSearchRequest(new float[] { 1f, 0f }, TopK: 3, KnnMetric.InnerProduct));

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].PointIndex);
        Assert.Equal(-3f, results[0].Distance, 5);
        Assert.True(results[0].Distance <= results[1].Distance);
        Assert.True(results[1].Distance <= results[2].Distance);
    }

    [Fact]
    public void Build_FlatIndex_L2ReturnsEuclideanDistance()
    {
        using var reader = LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Flat,
            KnnMetric.L2,
            new float[]
            {
                3f, 4f,
                6f, 8f,
            },
            Count: 2,
            Dimension: 2));

        var results = reader.Search(new VectorSearchRequest(new float[] { 0f, 0f }, TopK: 1, KnnMetric.L2));

        Assert.Single(results);
        Assert.Equal(0, results[0].PointIndex);
        Assert.Equal(5f, results[0].Distance, 5);
    }

    [Fact]
    public void Build_HnswIndex_SearchesContinuousPayload()
    {
        using var reader = LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Hnsw,
            KnnMetric.Cosine,
            new float[]
            {
                1f, 0f, 0f,
                0.9f, 0.1f, 0f,
                0f, 1f, 0f,
                -1f, 0f, 0f,
            },
            Count: 4,
            Dimension: 3,
            Hnsw: new VectorIndexHnswOptions(M: 4, EfConstruction: 8, EfSearch: 8, Seed: 7)));

        var results = reader.Search(new VectorSearchRequest(new float[] { 1f, 0f, 0f }, TopK: 2, KnnMetric.Cosine));

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].PointIndex);
        Assert.True(results[0].Distance <= results[1].Distance);
    }

    [Fact]
    public void Build_InvalidPayloadLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Flat,
            KnnMetric.Cosine,
            new float[] { 1f, 2f, 3f },
            Count: 2,
            Dimension: 2)));
    }

    [Fact]
    public void Search_WithDifferentMetric_Throws()
    {
        using var reader = LocalVectorIndexBuilder.Instance.Build(new VectorIndexBuildInput(
            VectorIndexAlgorithm.Flat,
            KnnMetric.Cosine,
            new float[] { 1f, 0f },
            Count: 1,
            Dimension: 2));

        Assert.Throws<ArgumentException>(() =>
            reader.Search(new VectorSearchRequest(new float[] { 1f, 0f }, TopK: 1, KnnMetric.L2)));
    }
}
