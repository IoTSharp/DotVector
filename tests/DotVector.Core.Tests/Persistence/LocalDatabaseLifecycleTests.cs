using DotVector.Api;
using DotVector.Model;

namespace DotVector.Core.Tests.Persistence;

public sealed class LocalDatabaseLifecycleTests : IDisposable
{
    private readonly string _root;

    public LocalDatabaseLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotvector-lifecycle-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CreateListCloseOpenDelete_ManagesIndependentDvecDirectories()
    {
        using var manager = new LocalVectorDatabaseManager(_root);

        VectorDatabase alpha = manager.CreateDatabase("alpha");
        VectorDatabase beta = manager.CreateDatabase("beta");

        alpha.CreateCollection<int>("items", 2, Metric.L2)
            .Insert(new VectorRecord<int>(1, new float[] { 1f, 0f }));
        beta.CreateCollection<int>("items", 2, Metric.L2)
            .Insert(new VectorRecord<int>(2, new float[] { 0f, 1f }));

        IReadOnlyList<LocalVectorDatabaseInfo> openDatabases = manager.ListDatabases();
        Assert.Equal(new[] { "alpha", "beta" }, openDatabases.Select(static db => db.Name).ToArray());
        Assert.All(openDatabases, static db => Assert.True(db.IsOpen));
        Assert.All(openDatabases, static db => Assert.EndsWith(".dvec", db.DirectoryPath, StringComparison.Ordinal));

        Assert.True(manager.CloseDatabase("alpha"));
        Assert.True(manager.CloseDatabase("beta"));

        VectorDatabase reopenedAlpha = manager.OpenDatabase("alpha");
        VectorDatabase reopenedBeta = manager.OpenDatabase("beta.dvec");

        Assert.Equal(1, reopenedAlpha.GetCollection<int>("items").Search(new float[] { 1f, 0f }, 1)[0].Key);
        Assert.Equal(2, reopenedBeta.GetCollection<int>("items").Search(new float[] { 0f, 1f }, 1)[0].Key);

        Assert.True(manager.CloseDatabase("alpha"));
        Assert.True(manager.CloseDatabase("beta"));
        Assert.True(manager.DeleteDatabase("alpha"));

        IReadOnlyList<LocalVectorDatabaseInfo> remaining = manager.ListDatabases();
        Assert.Single(remaining);
        Assert.Equal("beta", remaining[0].Name);
    }

    [Fact]
    public void OpenDatabase_RequiresExistingClosedDatabase()
    {
        using var manager = new LocalVectorDatabaseManager(_root);

        Assert.Throws<DirectoryNotFoundException>(() => manager.OpenDatabase("missing"));

        _ = manager.CreateDatabase("docs");
        Assert.Throws<InvalidOperationException>(() => manager.OpenDatabase("docs"));
        Assert.False(manager.CloseDatabase("missing"));
    }

    [Fact]
    public void DeleteDatabase_RequiresDatabaseToBeClosed()
    {
        using var manager = new LocalVectorDatabaseManager(_root);

        _ = manager.CreateDatabase("docs");

        Assert.Throws<InvalidOperationException>(() => manager.DeleteDatabase("docs"));
        Assert.True(manager.CloseDatabase("docs"));
        Assert.True(manager.DeleteDatabase("docs"));
        Assert.False(manager.DeleteDatabase("docs"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nested/db")]
    [InlineData("nested\\db")]
    public void DatabaseName_MustBeSingleDirectoryName(string name)
    {
        using var manager = new LocalVectorDatabaseManager(_root);

        Assert.Throws<ArgumentException>(() => manager.CreateDatabase(name));
    }
}
