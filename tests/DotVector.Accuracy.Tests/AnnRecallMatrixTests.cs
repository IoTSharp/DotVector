using DotVector.Api;
using DotVector.Compute;
using DotVector.Index.DiskAnn;
using DotVector.Index.Hnsw;
using DotVector.Index.Ivf;
using DotVector.Model;

namespace DotVector.Accuracy.Tests;

/// <summary>
/// V6 统一召回矩阵：同一批数据与查询覆盖 HNSW、IVF-Flat、IVF-PQ 和 Vamana。
/// </summary>
public sealed class AnnRecallMatrixTests
{
    private const int Count = 1024;
    private const int Dim = 64;
    private const int TopK = 10;

    [Theory]
    [InlineData("hnsw", 0.95)]
    [InlineData("ivf", 0.90)]
    [InlineData("ivf_pq", 0.50)]
    [InlineData("vamana", 0.92)]
    public void AnnIndex_RecallAt10_MeetsV6Threshold(string indexKind, double expectedRecall)
    {
        float[][] vectors = GenerateClusteredVectors(Count, Dim, clusterCount: 16, seed: 20260601);
        float[] query = GenerateQuery(Dim, seed: 20260602);
        HashSet<int> truth = ComputeTruth(query, vectors, Metric.Cosine, TopK);

        using var database = new VectorDatabase();
        Collection<int> collection = indexKind switch
        {
            "hnsw" => database.CreateCollection<int>("hnsw", Dim, Metric.Cosine, IndexKind.Hnsw, new HnswOptions
            {
                M = 16,
                EfConstruction = 200,
                EfSearch = 100,
                Seed = 42,
            }),
            "ivf" => database.CreateCollection<int>("ivf", Dim, Metric.Cosine, new IvfOptions
            {
                NList = 16,
                NProbe = 8,
                MaxIterations = 25,
                Seed = 42,
            }),
            "ivf_pq" => database.CreateCollection<int>("ivf-pq", Dim, Metric.L2, new IvfPqOptions
            {
                NList = 16,
                NProbe = 8,
                M = 8,
                NBits = 8,
                MaxIterations = 25,
                Seed = 42,
            }),
            "vamana" => database.CreateCollection<int>("vamana", Dim, Metric.Cosine, new VamanaOptions
            {
                MaxDegree = 32,
                SearchListSize = 100,
                Alpha = 1.2f,
                BeamWidth = 4,
                Seed = 42,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(indexKind), indexKind, null),
        };

        for (int i = 0; i < vectors.Length; i++)
            collection.Insert(new VectorRecord<int>(i, vectors[i]));

        IReadOnlyList<SearchResult<int>> results = collection.Search(query, TopK);
        double recall = results.Count(result => truth.Contains(result.Key)) / (double)TopK;

        Assert.True(recall >= expectedRecall,
            $"{indexKind} Recall@{TopK} = {recall:F3}, 期望 >= {expectedRecall:F2}。");
    }

    private static float[][] GenerateClusteredVectors(int count, int dim, int clusterCount, int seed)
    {
        var random = new Random(seed);
        var centers = new float[clusterCount][];
        for (int cluster = 0; cluster < centers.Length; cluster++)
        {
            centers[cluster] = new float[dim];
            for (int d = 0; d < dim; d++)
                centers[cluster][d] = (float)(random.NextDouble() * 10.0 - 5.0);
        }

        var vectors = new float[count][];
        for (int i = 0; i < vectors.Length; i++)
        {
            int cluster = i % clusterCount;
            vectors[i] = new float[dim];
            for (int d = 0; d < dim; d++)
                vectors[i][d] = centers[cluster][d] + NextGaussian(random) * 0.3f;
        }

        return vectors;
    }

    private static float[] GenerateQuery(int dim, int seed)
    {
        var random = new Random(seed);
        var query = new float[dim];
        for (int d = 0; d < query.Length; d++)
            query[d] = (float)(random.NextDouble() * 2.0 - 1.0);
        return query;
    }

    private static HashSet<int> ComputeTruth(float[] query, float[][] vectors, Metric metric, int topK)
    {
        var scored = new (int Id, float Score)[vectors.Length];
        for (int i = 0; i < vectors.Length; i++)
            scored[i] = (i, Distance.Compute(query, vectors[i], metric));

        Array.Sort(scored, (a, b) => metric.IsLargerBetter()
            ? b.Score.CompareTo(a.Score)
            : a.Score.CompareTo(b.Score));

        return scored.Take(topK).Select(static item => item.Id).ToHashSet();
    }

    private static float NextGaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
