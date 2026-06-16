using DotVector.Api;
using DotVector.Index.Ivf;
using DotVector.Model;
using Xunit;

namespace DotVector.Tests;

public sealed class IvfPersistenceTests
{
    [Fact]
    public void IvfFlatCollection_FlushReopen_RestoresSearchResults()
    {
        // 回归：DotVector IVF-Flat 持久化端到端——之前 Collection.Flush 对 IVF 抛
        // NotSupportedException；现在 Flush 写 ivfflat.bin，重开 DB 后应能用同样
        // 的 top-K 命中。
        string dir = Path.Combine(Path.GetTempPath(), "dv-ivfflat-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const int N = 200;
            const int Dim = 16;
            var rng = new Random(7);
            var data = new (string Key, float[] Vec)[N];
            var query = new float[Dim];
            for (int i = 0; i < Dim; i++) query[i] = (float)(rng.NextDouble() * 2 - 1);
            for (int i = 0; i < N; i++)
            {
                var v = new float[Dim];
                for (int j = 0; j < Dim; j++) v[j] = (float)(rng.NextDouble() * 2 - 1);
                data[i] = ($"k{i:D4}", v);
            }

            string[] expectedTop5;
            using (var db = new VectorDatabase(dir))
            {
                var c = db.CreateCollection<string>("ivfflat_col", Dim, Metric.Cosine,
                    new IvfOptions { NList = 16, NProbe = 8, MaxIterations = 25, Seed = 42 });
                foreach (var (k, v) in data)
                    c.Insert(new VectorRecord<string>(k, v));
                expectedTop5 = c.Search(query, topK: 5).Select(r => r.Key).ToArray();
                Assert.Equal(5, expectedTop5.Length);
                c.Flush();
            }

            using (var db = new VectorDatabase(dir))
            {
                var c = db.GetCollection<string>("ivfflat_col");
                var restored = c.Search(query, topK: 5).Select(r => r.Key).ToArray();
                Assert.Equal(expectedTop5, restored);

                // Delete + flush + 再开 → 删除应持久化。
                c.Delete(expectedTop5[0]);
                c.Flush();
            }

            using (var db = new VectorDatabase(dir))
            {
                var c = db.GetCollection<string>("ivfflat_col");
                var afterDelete = c.Search(query, topK: 5).Select(r => r.Key).ToArray();
                Assert.DoesNotContain(expectedTop5[0], afterDelete);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IvfPqCollection_FlushReopen_RestoresSearchResults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dv-ivfpq-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const int N = 512;
            const int Dim = 32;
            var rng = new Random(11);
            var data = new (string Key, float[] Vec)[N];
            var query = new float[Dim];
            for (int i = 0; i < Dim; i++) query[i] = (float)(rng.NextDouble() * 2 - 1);
            for (int i = 0; i < N; i++)
            {
                var v = new float[Dim];
                for (int j = 0; j < Dim; j++) v[j] = (float)(rng.NextDouble() * 2 - 1);
                data[i] = ($"k{i:D4}", v);
            }

            string[] expectedTop3;
            using (var db = new VectorDatabase(dir))
            {
                var c = db.CreateCollection<string>("ivfpq_col", Dim, Metric.Cosine,
                    new IvfPqOptions { NList = 32, NProbe = 16, M = 8, NBits = 8, MaxIterations = 25, Seed = 11 });
                foreach (var (k, v) in data)
                    c.Insert(new VectorRecord<string>(k, v));
                expectedTop3 = c.Search(query, topK: 3).Select(r => r.Key).ToArray();
                Assert.Equal(3, expectedTop3.Length);
                c.Flush();
            }

            using (var db = new VectorDatabase(dir))
            {
                var c = db.GetCollection<string>("ivfpq_col");
                var restored = c.Search(query, topK: 3).Select(r => r.Key).ToArray();
                Assert.Equal(expectedTop3, restored);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
