using DotVector.Api;
using DotVector.Index.Hnsw;
using DotVector.Model;
using Xunit;

namespace DotVector.Tests;

public sealed class HnswPersistenceTests
{
    [Fact]
    public void HnswCollection_FlushReopen_RestoresSearchResults()
    {
        // 回归：DotVector HNSW 持久化端到端——之前 Collection.Flush 对 HNSW 抛
        // NotSupportedException；现在 Flush 写 hnsw.bin，重开 DB 后应能用同样
        // 的 top-K 命中（含 Insert/Search/Delete 全 API）。
        string dir = Path.Combine(Path.GetTempPath(), "dv-hnsw-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const int N = 200;
            const int Dim = 32;
            var rng = new Random(42);
            var query = new float[Dim];
            for (int i = 0; i < Dim; i++) query[i] = (float)(rng.NextDouble() * 2 - 1);

            (string Key, float[] Vec)[] data = new (string, float[])[N];
            for (int i = 0; i < N; i++)
            {
                var v = new float[Dim];
                for (int j = 0; j < Dim; j++) v[j] = (float)(rng.NextDouble() * 2 - 1);
                data[i] = ($"k{i:D4}", v);
            }

            string[] expectedTop3;
            using (var db = new VectorDatabase(dir))
            {
                var c = db.CreateCollection<string>("hnsw_col", Dim, Metric.Cosine, IndexKind.Hnsw,
                    new HnswOptions { M = 16, EfConstruction = 200, EfSearch = 100, Seed = 7 });
                foreach (var (k, v) in data)
                    c.Insert(new VectorRecord<string>(k, v));
                expectedTop3 = c.Search(query, topK: 3).Select(r => r.Key).ToArray();
                Assert.Equal(3, expectedTop3.Length);
                c.Flush();
            }

            using (var db = new VectorDatabase(dir))
            {
                var c = db.GetCollection<string>("hnsw_col");
                var restored = c.Search(query, topK: 3).Select(r => r.Key).ToArray();
                Assert.Equal(expectedTop3, restored);

                // Delete + 再 Flush + 再开 → 删除应也持久化。
                c.Delete(expectedTop3[0]);
                c.Flush();
            }

            using (var db = new VectorDatabase(dir))
            {
                var c = db.GetCollection<string>("hnsw_col");
                var afterDelete = c.Search(query, topK: 3).Select(r => r.Key).ToArray();
                Assert.DoesNotContain(expectedTop3[0], afterDelete);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
