using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using DotVector.Api;
using DotVector.Compute;
using DotVector.Index.DiskAnn;
using DotVector.Index.Hnsw;
using DotVector.Index.Ivf;
using DotVector.Model;

namespace DotVector.Benchmarks;

/// <summary>
/// V6：ANN 索引召回率基准，使用同一批查询对比 HNSW、IVF-Flat 和 Vamana 的 Recall@10。
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class AnnRecallBenchmark
{
    private const int Dim = 64;
    private const int TopK = 10;
    private const int QueryCount = 8;

    private float[][] _vectors = [];
    private float[][] _queries = [];
    private HashSet<int>[] _truthTopK = [];
    private VectorDatabase? _database;
    private Collection<int>? _hnsw;
    private Collection<int>? _ivfFlat;
    private Collection<int>? _vamana;

    /// <summary>索引数据规模。</summary>
    [Params(1_000, 10_000)]
    public int Count { get; set; }

    /// <summary>初始化向量、真值和待测索引。</summary>
    [GlobalSetup]
    public void Setup()
    {
        _vectors = GenerateClusteredVectors(Count, Dim, clusterCount: 32, seed: 20260601);
        _queries = GenerateQueries(QueryCount, Dim, seed: 20260602);
        _truthTopK = ComputeTruth(_vectors, _queries, Metric.Cosine, TopK);

        _database = new VectorDatabase();
        _hnsw = _database.CreateCollection<int>(
            "v6-hnsw-recall",
            Dim,
            Metric.Cosine,
            IndexKind.Hnsw,
            new HnswOptions
            {
                M = 16,
                EfConstruction = 200,
                EfSearch = 100,
                Seed = 42,
            });

        _ivfFlat = _database.CreateCollection<int>(
            "v6-ivf-flat-recall",
            Dim,
            Metric.Cosine,
            new IvfOptions
            {
                NList = 32,
                NProbe = 8,
                MaxIterations = 25,
                Seed = 42,
            });

        _vamana = _database.CreateCollection<int>(
            "v6-vamana-recall",
            Dim,
            Metric.Cosine,
            new VamanaOptions
            {
                MaxDegree = 32,
                SearchListSize = 100,
                Alpha = 1.2f,
                BeamWidth = 4,
                Seed = 42,
            });

        for (int i = 0; i < _vectors.Length; i++)
        {
            var record = new VectorRecord<int>(i, _vectors[i]);
            _hnsw.Insert(record);
            _ivfFlat.Insert(record);
            _vamana.Insert(record);
        }
    }

    /// <summary>释放基准数据库。</summary>
    [GlobalCleanup]
    public void Cleanup() => _database?.Dispose();

    /// <summary>HNSW Recall@10。</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = QueryCount)]
    public double Hnsw_RecallAt10() => ComputeRecall(_hnsw!);

    /// <summary>IVF-Flat Recall@10。</summary>
    [Benchmark(OperationsPerInvoke = QueryCount)]
    public double IvfFlat_RecallAt10() => ComputeRecall(_ivfFlat!);

    /// <summary>Vamana Recall@10。</summary>
    [Benchmark(OperationsPerInvoke = QueryCount)]
    public double Vamana_RecallAt10() => ComputeRecall(_vamana!);

    private double ComputeRecall(Collection<int> collection)
    {
        double recallSum = 0;
        for (int queryIndex = 0; queryIndex < _queries.Length; queryIndex++)
        {
            var results = collection.Search(_queries[queryIndex], TopK);
            int hits = 0;
            foreach (var result in results)
            {
                if (_truthTopK[queryIndex].Contains(result.Key))
                    hits++;
            }

            recallSum += hits / (double)TopK;
        }

        return recallSum / _queries.Length;
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

    private static float[][] GenerateQueries(int count, int dim, int seed)
    {
        var random = new Random(seed);
        var queries = new float[count][];
        for (int i = 0; i < queries.Length; i++)
        {
            queries[i] = new float[dim];
            for (int d = 0; d < dim; d++)
                queries[i][d] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        return queries;
    }

    private static HashSet<int>[] ComputeTruth(float[][] vectors, float[][] queries, Metric metric, int topK)
    {
        var truth = new HashSet<int>[queries.Length];
        var scored = new (int Id, float Score)[vectors.Length];
        for (int queryIndex = 0; queryIndex < queries.Length; queryIndex++)
        {
            for (int vectorIndex = 0; vectorIndex < vectors.Length; vectorIndex++)
                scored[vectorIndex] = (vectorIndex, Distance.Compute(queries[queryIndex], vectors[vectorIndex], metric));

            Array.Sort(scored, (a, b) => metric.IsLargerBetter()
                ? b.Score.CompareTo(a.Score)
                : a.Score.CompareTo(b.Score));

            truth[queryIndex] = scored.Take(topK).Select(static item => item.Id).ToHashSet();
        }

        return truth;
    }

    private static float NextGaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(1).WithIterationCount(3));
        }
    }
}
