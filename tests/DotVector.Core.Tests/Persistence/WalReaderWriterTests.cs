using DotVector.IO;
using DotVector.Wal;

namespace DotVector.Core.Tests.Persistence;

/// <summary>
/// 验证 <see cref="WalWriter"/> 与 <see cref="WalReader"/> 的写入/读取/容错行为。
/// </summary>
public sealed class WalReaderWriterTests : IDisposable
{
    private readonly string _walDir;

    public WalReaderWriterTests()
    {
        _walDir = Path.Combine(Path.GetTempPath(), "dotvec-wal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_walDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_walDir, recursive: true); }
        catch { /* 忽略清理失败 */ }
    }

    [Fact]
    public void AppendInsert_AndDelete_RoundTrip_Int32Key()
    {
        Guid collId = Guid.NewGuid();
        string path = Path.Combine(_walDir, "wal-000001.log");
        float[] vec = [1f, 2f, 3f, 4f];

        using (var writer = new WalWriter(path))
        {
            writer.AppendInsert(collId, 42, vec);
            writer.AppendDelete(collId, 42);
        }

        var records = WalReader.ReadAll(_walDir).ToList();
        Assert.Equal(2, records.Count);

        Assert.Equal(WalRecordType.Insert, records[0].Type);
        Assert.Equal(collId, records[0].CollectionId);
        SpanReader r0 = new(records[0].Body);
        Assert.Equal((byte)KeyTypeCode.Int32, r0.ReadByte());
        Assert.Equal(42, r0.ReadInt32());
        Assert.Equal(4u, r0.ReadUInt32());

        Assert.Equal(WalRecordType.Delete, records[1].Type);
        SpanReader r1 = new(records[1].Body);
        Assert.Equal((byte)KeyTypeCode.Int32, r1.ReadByte());
        Assert.Equal(42, r1.ReadInt32());
    }

    [Theory]
    [InlineData(typeof(long))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(string))]
    public void AppendInsert_VariousKeyTypes_RoundTrip(Type keyType)
    {
        Guid collId = Guid.NewGuid();
        string path = Path.Combine(_walDir, "wal-000001.log");
        float[] vec = [0.1f, 0.2f];

        using (var writer = new WalWriter(path))
        {
            if (keyType == typeof(long)) writer.AppendInsert<long>(collId, 1234567890L, vec);
            else if (keyType == typeof(Guid)) writer.AppendInsert<Guid>(collId, Guid.Parse("11111111-2222-3333-4444-555555555555"), vec);
            else writer.AppendInsert<string>(collId, "hello-世界", vec);
        }

        var records = WalReader.ReadAll(_walDir).ToList();
        Assert.Single(records);
        Assert.Equal(WalRecordType.Insert, records[0].Type);
        Assert.Equal(collId, records[0].CollectionId);
    }

    [Fact]
    public void TornWrite_StopsAtTruncatedRecord()
    {
        Guid collId = Guid.NewGuid();
        string path = Path.Combine(_walDir, "wal-000001.log");
        using (var writer = new WalWriter(path))
        {
            writer.AppendInsert(collId, 1, new float[] { 1f, 2f });
            writer.AppendInsert(collId, 2, new float[] { 3f, 4f });
        }

        // 截断最后 5 字节，模拟崩溃
        long len = new FileInfo(path).Length;
        using (FileStream fs = new(path, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(len - 5);
        }

        var records = WalReader.ReadAll(_walDir).ToList();
        Assert.Single(records); // 只读到第一条完整记录
    }

    [Fact]
    public void CrcMismatch_StopsAtCorruptedRecord()
    {
        Guid collId = Guid.NewGuid();
        string path = Path.Combine(_walDir, "wal-000001.log");
        using (var writer = new WalWriter(path))
        {
            writer.AppendInsert(collId, 1, new float[] { 1f, 2f });
            writer.AppendInsert(collId, 2, new float[] { 3f, 4f });
        }

        // 翻转第一条记录 body 中的一个字节（破坏 CRC）
        byte[] data = File.ReadAllBytes(path);
        data[10] ^= 0xFF; // 在 body 中
        File.WriteAllBytes(path, data);

        var records = WalReader.ReadAll(_walDir).ToList();
        Assert.Empty(records); // 第一条就 CRC 失败 → 立即停止
    }

    [Fact]
    public void EmptyWalDirectory_ReturnsNoRecords()
    {
        Assert.Empty(WalReader.ReadAll(_walDir));
    }
}
