using System.Runtime.InteropServices;
using DotVector.Exceptions;
using DotVector.IO;

namespace DotVector.Wal;

/// <summary>
/// WAL 写入器：把 <see cref="WalRecordType.Insert"/> / <see cref="WalRecordType.Delete"/>
/// 记录顺序追加到 <c>wal/wal-{seq:D6}.log</c> 文件。
/// </summary>
/// <remarks>
/// 文件格式（每条记录）：<br/>
/// <c>u32 bodyLength + body[bodyLength] + u32 crc32(body)</c><br/>
/// body 内部：<c>u8 type + Guid collectionId + u8 keyTypeCode + key bytes + (Insert: u32 dim + dim*4 字节)</c><br/>
/// 若中途崩溃造成最后一条记录截断，<see cref="WalReader"/> 会停在第一条不完整记录前。
/// </remarks>
internal sealed class WalWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();

    /// <summary>当前 WAL 文件路径。</summary>
    public string FilePath { get; }

    /// <summary>使用指定文件路径打开 WAL 写入器（追加模式）。</summary>
    public WalWriter(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        FilePath = filePath;
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: false);
    }

    /// <summary>追加 Insert 记录。</summary>
    public void AppendInsert<TKey>(Guid collectionId, TKey key, ReadOnlySpan<float> vector) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        KeyTypeCode code = KeyCodec.GetCode<TKey>();
        int keySize = KeyCodec.ComputeSize(key);
        int bodySize = 1 /*type*/ + 16 /*guid*/ + 1 /*keyTypeCode*/ + keySize + 4 /*dim*/ + vector.Length * sizeof(float);

        byte[] buffer = new byte[sizeof(uint) + bodySize + sizeof(uint)];
        SpanWriter w = new(buffer);
        w.WriteUInt32((uint)bodySize);
        int bodyStart = w.Written;
        w.WriteByte((byte)WalRecordType.Insert);
        w.WriteGuid(collectionId);
        w.WriteByte((byte)code);
        KeyCodec.Write(ref w, key);
        w.WriteUInt32((uint)vector.Length);
        w.WriteBytes(MemoryMarshal.AsBytes(vector));
        FinishRecord(buffer, bodyStart, bodySize, ref w);
    }

    /// <summary>追加 Delete 记录。</summary>
    public void AppendDelete<TKey>(Guid collectionId, TKey key) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        KeyTypeCode code = KeyCodec.GetCode<TKey>();
        int keySize = KeyCodec.ComputeSize(key);
        int bodySize = 1 + 16 + 1 + keySize;

        byte[] buffer = new byte[sizeof(uint) + bodySize + sizeof(uint)];
        SpanWriter w = new(buffer);
        w.WriteUInt32((uint)bodySize);
        int bodyStart = w.Written;
        w.WriteByte((byte)WalRecordType.Delete);
        w.WriteGuid(collectionId);
        w.WriteByte((byte)code);
        KeyCodec.Write(ref w, key);
        FinishRecord(buffer, bodyStart, bodySize, ref w);
    }

    private void FinishRecord(byte[] buffer, int bodyStart, int bodySize, scoped ref SpanWriter w)
    {
        if (w.Written - bodyStart != bodySize)
        {
            throw new DotVectorException(
                $"WAL 记录构建错误：实际写入 {w.Written - bodyStart} 字节，预期 {bodySize} 字节。");
        }

        ReadOnlySpan<byte> bodySpan = new ReadOnlySpan<byte>(buffer, bodyStart, bodySize);
        uint crc = Crc32.Compute(bodySpan);
        w.WriteUInt32(crc);

        lock (_lock)
        {
            _stream.Write(buffer, 0, buffer.Length);
        }
    }

    /// <summary>将缓冲数据落盘。</summary>
    public void Flush()
    {
        lock (_lock)
        {
            _stream.Flush(flushToDisk: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _stream.Flush(flushToDisk: true);
            _stream.Dispose();
        }
    }
}
