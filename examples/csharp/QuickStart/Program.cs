using DotVector.Api;
using DotVector.Model;

using var database = new VectorDatabase();
Collection<string> articles = database.CreateCollection<string>(
    "articles",
    dimensions: 4,
    metric: Metric.Cosine);

articles.InsertBatch(
[
    new VectorRecord<string>("dotnet-runtime", [0.95f, 0.10f, 0.08f, 0.02f])
    {
        Payload = new Dictionary<string, object>
        {
            ["title"] = "DotNet runtime",
            ["category"] = "runtime",
        },
    },
    new VectorRecord<string>("vector-search", [0.85f, 0.15f, 0.10f, 0.05f])
    {
        Payload = new Dictionary<string, object>
        {
            ["title"] = "Vector search basics",
            ["category"] = "search",
        },
    },
    new VectorRecord<string>("analytics", [0.10f, 0.90f, 0.20f, 0.30f])
    {
        Payload = new Dictionary<string, object>
        {
            ["title"] = "Analytics pipeline",
            ["category"] = "analytics",
        },
    },
]);

float[] query = [0.92f, 0.12f, 0.07f, 0.03f];
IReadOnlyList<SearchResult<string>> results = articles.Search(query, topK: 2);

Console.WriteLine("Top matches:");
foreach (SearchResult<string> result in results)
{
    string title = result.Payload is not null
        && result.Payload.TryGetValue("title", out object? value)
        && value is string text
        ? text
        : result.Key;

    Console.WriteLine($"{result.Key,-16} score={result.Score:F4} title={title}");
}
