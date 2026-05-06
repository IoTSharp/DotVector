using DotVector.IO;

namespace DotVector.Wal;

/// <summary>
/// WAL 读取器：按 <c>wal/wal-{seq:D6}.log</c> 序列遍历目录下所有 WAL 文件，
/// 顺序产出有效记录；遇到首条不完整或 CRC 不匹配的记录时停止。
/// </summary>
internal static class WalReader
{
    /// <summary>
    /// 枚举指定 WAL 目录中的所有有效记录（按 seq 升序）。
    /// </summary>
    public static IEnumerable<WalRecord> ReadAll(string walDirectory)
    {
        ArgumentNullException.ThrowIfNull(walDirectory);
        if (!Directory.Exists(walDirectory))
        {
            yield break;
        }

        // 收集 wal-*.log 并按文件名排序
        string[] files = Directory.GetFiles(walDirectory, "wal-*.log");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files)
        {
            foreach (WalRecord record in ReadFile(file))
            {
                yield return record;
            }
        }
    }

    /// <summary>读取单个 WAL 文件中的所有有效记录。</summary>
    public static IEnumerable<WalRecord> ReadFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        // 使用 FileShare.ReadWrite 容忍同进程内可能持有的 WAL 写入句柄。
        byte[] data;
        using (FileStream fs = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            data = new byte[fs.Length];
            int read = 0;
            while (read < data.Length)
            {
                int n = fs.Read(data, read, data.Length - read);
                if (n <= 0) { break; }
                read += n;
            }
            if (read < data.Length) { Array.Resize(ref data, read); }
        }
        int offset = 0;
        while (offset + sizeof(uint) <= data.Length)
        {
            // 读取长度
            ReadOnlySpan<byte> lenSpan = data.AsSpan(offset, sizeof(uint));
            SpanReader reader = new(lenSpan);
            uint bodyLen = reader.ReadUInt32();
            int recordTotal = sizeof(uint) + (int)bodyLen + sizeof(uint);
            if (offset + recordTotal > data.Length)
            {
                // 截断 — 停止
                yield break;
            }

            ReadOnlySpan<byte> body = data.AsSpan(offset + sizeof(uint), (int)bodyLen);
            SpanReader crcReader = new(data.AsSpan(offset + sizeof(uint) + (int)bodyLen, sizeof(uint)));
            uint storedCrc = crcReader.ReadUInt32();
            uint actualCrc = Crc32.Compute(body);
            if (storedCrc != actualCrc)
            {
                // 损坏 — 停止
                yield break;
            }

            // 解析 body 头部：type + guid
            SpanReader bodyReader = new(body);
            WalRecordType type = (WalRecordType)bodyReader.ReadByte();
            Guid collectionId = bodyReader.ReadGuid();

            // 把剩余字节作为 body 返回（包括 keyTypeCode + key + 可能的向量）
            byte[] payload = body.Slice(bodyReader.Position).ToArray();

            // 重组 body：调用方期望 body 中包含完整的 keyTypeCode/key/vector 信息，
            // 这里把"类型 + GUID"剥离后剩下的内容作为 Body 提供。
            yield return new WalRecord
            {
                Type = type,
                CollectionId = collectionId,
                Body = payload,
            };

            offset += recordTotal;
        }
    }
}
