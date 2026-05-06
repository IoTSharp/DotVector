using DotVector.Api;
using DotVector.Model;

namespace DotVector.Tests;

/// <summary>
/// DotVector 集成 smoke 测试。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证测试框架正常运行。
    /// </summary>
    [Fact]
    public void Smoke_AlwaysTrue()
    {
        Assert.True(true);
    }

    /// <summary>
    /// 验证 VectorDatabase 可以被实例化和释放。
    /// </summary>
    [Fact]
    public void VectorDatabase_CreateAndDispose_DoesNotThrow()
    {
        using var db = new VectorDatabase();
        Assert.NotNull(db);
    }

    /// <summary>
    /// 验证可以创建 Collection，并且其属性正确设置。
    /// </summary>
    [Fact]
    public void CreateCollection_WithValidParams_SetsProperties()
    {
        using var db = new VectorDatabase();
        var collection = db.CreateCollection<string>("test", dimensions: 4, metric: Metric.Cosine);

        Assert.Equal("test", collection.Name);
        Assert.Equal(4, collection.Dimensions);
        Assert.Equal(Metric.Cosine, collection.Metric);
    }

    /// <summary>
    /// 验证向量维度不匹配时 Search 抛出正确异常。
    /// </summary>
    [Fact]
    public void Search_WithWrongDimensions_ThrowsArgumentException()
    {
        using var db = new VectorDatabase();
        var collection = db.CreateCollection<string>("test", dimensions: 4);

        // 查询向量维度与集合不一致，应当抛出 ArgumentException
        Assert.Throws<ArgumentException>(() =>
        {
            float[] wrongDim = [1f, 2f]; // 2 维，期望 4 维
            collection.Search(wrongDim, topK: 5);
        });
    }

    /// <summary>
    /// 验证对空集合的搜索返回空结果（不抛异常）。
    /// </summary>
    [Fact]
    public void Search_EmptyCollection_ReturnsEmpty()
    {
        using var db = new VectorDatabase();
        var collection = db.CreateCollection<string>("test", dimensions: 4);

        float[] query = [1f, 0f, 0f, 0f];
        var results = collection.Search(query, topK: 5);

        Assert.Empty(results);
    }
}
