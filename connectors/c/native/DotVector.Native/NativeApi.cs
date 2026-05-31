using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DotVector.Api;
using DotVector.Core;
using DotVector.Core.Protocol;
using DotVector.Data;
using DotVector.Model;
using DotVector.Query;

namespace DotVector.Native;

/// <summary>
/// DotVector C ABI 入口，供 C / Python / 其他 FFI 运行时调用。
/// </summary>
/// <remarks>
/// <para>
/// 所有公开方法都是 <see cref="UnmanagedCallersOnlyAttribute"/> 静态导出，
/// 句柄通过线程安全的 <see cref="ConcurrentDictionary{TKey,TValue}"/> 表管理。
/// </para>
/// <para>
/// 变长输出 (search / list / payload) 一律使用 caller-allocated UTF-8 buffer + 必需长度
/// (out_required_size) 协议，不持有任何 .NET 端的临时缓冲。
/// </para>
/// </remarks>
public static class NativeApi
{
    private const int Ok = 0;
    private const int InvalidArgument = -1;
    private const int NotFound = -2;
    private const int BufferTooSmall = -3;
    private const int Failed = -100;
    private const string VersionText = "DotVector.Native 0.2.0";

    private static readonly ConcurrentDictionary<nint, NativeDatabase> Databases = new();
    private static readonly ConcurrentDictionary<nint, NativeCollection> Collections = new();
    private static long _nextHandle;

    [ThreadStatic]
    private static string? _lastError;

    // --------------------------------------------------------------------- //
    // Diagnostics                                                           //
    // --------------------------------------------------------------------- //

    [UnmanagedCallersOnly(EntryPoint = "dotvector_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Version(nint buffer, int bufferLength)
        => CopyUtf8(VersionText, buffer, bufferLength);

    [UnmanagedCallersOnly(EntryPoint = "dotvector_last_error", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int LastError(nint buffer, int bufferLength)
        => CopyUtf8(_lastError ?? string.Empty, buffer, bufferLength);

    // --------------------------------------------------------------------- //
    // Database lifecycle                                                    //
    // --------------------------------------------------------------------- //

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_create", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint DatabaseCreate()
        => GuardHandle(static () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "dotvector-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            DotVectorClient client = DotVectorClient.Embedded(tempDir);
            return AddDatabase(client, tempDir, ownsDirectory: true);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_open", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint DatabaseOpen(nint path)
        => GuardHandle(() =>
        {
            string managedPath = ReadUtf8(path, nameof(path));
            if (string.IsNullOrWhiteSpace(managedPath))
            {
                throw new ArgumentException("Database path must not be empty.", nameof(path));
            }

            DotVectorClient client = DotVectorClient.Embedded(managedPath);
            return AddDatabase(client, managedPath, ownsDirectory: false);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_connect", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint DatabaseConnect(nint endpoint, nint databaseName, nint apiKey, int useProxy)
        => GuardHandle(() =>
        {
            string endpointText = ReadUtf8(endpoint, nameof(endpoint));
            if (string.IsNullOrWhiteSpace(endpointText))
            {
                throw new ArgumentException("Endpoint must not be empty.", nameof(endpoint));
            }
            _ = ReadOptionalUtf8(databaseName);
            _ = ReadOptionalUtf8(apiKey);
            _ = useProxy;
            throw new NotSupportedException(
                "DotVector remote server mode has been removed. Use dotvector_database_open for local embedded databases, or use SonnetDB when a service endpoint is required.");
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_free", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void DatabaseFree(nint database)
    {
        try
        {
            ClearError();
            if (database == 0)
            {
                return;
            }

            if (!Databases.TryRemove(database, out NativeDatabase? entry))
            {
                SetError("Database handle was not found.");
                return;
            }

            foreach (nint collectionHandle in entry.CollectionHandles)
            {
                Collections.TryRemove(collectionHandle, out _);
            }

            entry.Client.DisposeAsync().AsTask().GetAwaiter().GetResult();

            if (entry.OwnsDirectory && Directory.Exists(entry.DirectoryPath))
            {
                try { Directory.Delete(entry.DirectoryPath, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_flush", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseFlush(nint database)
        => GuardStatus(() =>
        {
            GetUnderlyingDatabase(database).Flush();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_compact", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseCompact(nint database)
        => GuardStatus(() =>
        {
            GetUnderlyingDatabase(database).Compact();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_ping", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabasePing(nint database)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            bool ok = db.Client.PingAsync().AsTask().GetAwaiter().GetResult();
            return ok ? 1 : 0;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_list_collections", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseListCollections(nint database, nint outBuffer, int bufferLength, nint outRequiredSize)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            IReadOnlyList<CollectionInfo> all = db.Client.ListCollectionsAsync().AsTask().GetAwaiter().GetResult();

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                for (int i = 0; i < all.Count; i++)
                {
                    WriteCollectionInfo(writer, all[i]);
                }
                writer.WriteEndArray();
            }

            return WriteVariableOutput(ms, outBuffer, bufferLength, outRequiredSize);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_collection_exists", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseCollectionExists(nint database, nint name)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            bool exists = db.Client.CollectionExistsAsync(n).AsTask().GetAwaiter().GetResult();
            return exists ? 1 : 0;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_create_collection", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseCreateCollection(nint database, nint name, int dimensions, int metric)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            ValidateCollectionOptions(dimensions, metric);
            db.Client.CreateCollectionAsync(n, dimensions, MapMetric(metric)).AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_ensure_collection", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseEnsureCollection(nint database, nint name, int dimensions, int metric)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            ValidateCollectionOptions(dimensions, metric);
            db.Client.EnsureCollectionAsync(n, dimensions, MapMetric(metric)).AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_delete_collection", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DatabaseDeleteCollection(nint database, nint name)
        => GuardStatus(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            db.Client.DeleteCollectionAsync(n).AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_database_get_collection", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint DatabaseGetCollection(nint database, nint name)
        => GuardHandle(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            CollectionInfo info = DescribeFromList(db.Client, n)
                ?? throw new KeyNotFoundException($"Collection '{n}' was not found.");
            DotVectorClientCollection collection = db.Client.GetCollection(n);
            return AddCollection(db, collection, info.Dimensions);
        });

    // --------------------------------------------------------------------- //
    // Collection lifecycle (legacy int64)                                   //
    // --------------------------------------------------------------------- //

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_create_i64", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint CollectionCreateI64(nint database, nint name, int dimensions, int metric, int indexKind)
        => GuardHandle(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            ValidateCollectionOptions(dimensions, metric);
            ValidateIndexKind(indexKind);
            DotVectorClientCollection collection = db.Client
                .CreateCollectionAsync(n, dimensions, MapMetric(metric))
                .AsTask().GetAwaiter().GetResult();
            return AddCollection(db, collection, dimensions);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_get_i64", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint CollectionGetI64(nint database, nint name)
        => GuardHandle(() =>
        {
            NativeDatabase db = GetDatabase(database);
            string n = ReadCollectionName(name);
            CollectionInfo info = DescribeFromList(db.Client, n)
                ?? throw new KeyNotFoundException($"Collection '{n}' was not found.");
            DotVectorClientCollection collection = db.Client.GetCollection(n);
            return AddCollection(db, collection, info.Dimensions);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_free", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void CollectionFree(nint collection)
    {
        try
        {
            ClearError();
            if (collection != 0)
            {
                Collections.TryRemove(collection, out _);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_count", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static long CollectionCount(nint collection)
    {
        try
        {
            ClearError();
            NativeCollection entry = GetCollection(collection);
            return entry.Collection.CountAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex);
            return NotFound;
        }
        catch (ArgumentException ex)
        {
            SetError(ex);
            return NotFound;
        }
        catch (Exception ex)
        {
            SetError(ex);
            return Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_describe", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionDescribe(nint collection, nint outBuffer, int bufferLength, nint outRequiredSize)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            CollectionInfo? info = entry.Collection.DescribeAsync().AsTask().GetAwaiter().GetResult();
            if (info is null)
            {
                throw new KeyNotFoundException($"Collection '{entry.Collection.Name}' was not found.");
            }

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                WriteCollectionInfo(writer, info);
            }
            return WriteVariableOutput(ms, outBuffer, bufferLength, outRequiredSize);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_insert_i64", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionInsertI64(nint collection, long key, nint vector, int dimensions)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            ValidateVectorPointer(vector, dimensions, entry.Dimensions, nameof(vector));

            float[] managedVector = new float[dimensions];
            Marshal.Copy(vector, managedVector, 0, dimensions);
            entry.Collection
                .UpsertAsync(key.ToString(CultureInfo.InvariantCulture), managedVector)
                .AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_search_i64", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionSearchI64(
        nint collection,
        nint query,
        int dimensions,
        int topK,
        nint outKeys,
        nint outScores,
        nint outCount)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            ValidateVectorPointer(query, dimensions, entry.Dimensions, nameof(query));
            if (topK <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(topK), topK, "TopK must be positive.");
            }

            if (outKeys == 0 || outScores == 0 || outCount == 0)
            {
                throw new ArgumentException("Output buffers must not be null.");
            }

            float[] managedQuery = new float[dimensions];
            Marshal.Copy(query, managedQuery, 0, dimensions);

            IReadOnlyList<ScoredPoint> results = entry.Collection
                .SearchAsync(managedQuery, topK)
                .AsTask().GetAwaiter().GetResult();

            int count = results.Count;
            var keys = new long[count];
            var scores = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!long.TryParse(results[i].Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedKey))
                {
                    throw new InvalidOperationException(
                        $"Search hit id '{results[i].Id}' cannot be parsed as int64.");
                }
                keys[i] = parsedKey;
                scores[i] = results[i].Score;
            }

            Marshal.Copy(keys, 0, outKeys, count);
            Marshal.Copy(scores, 0, outScores, count);
            Marshal.WriteInt32(outCount, count);
            return Ok;
        });

    // --------------------------------------------------------------------- //
    // String-keyed full-feature collection API                              //
    // --------------------------------------------------------------------- //

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_upsert", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionUpsert(nint collection, nint id, nint vector, int dimensions, nint payloadJson)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            string idText = ReadUtf8(id, nameof(id));
            if (string.IsNullOrEmpty(idText))
            {
                throw new ArgumentException("Id must not be empty.", nameof(id));
            }
            ValidateVectorPointer(vector, dimensions, entry.Dimensions, nameof(vector));

            float[] managedVector = new float[dimensions];
            Marshal.Copy(vector, managedVector, 0, dimensions);

            IReadOnlyDictionary<string, object>? payload = null;
            string? payloadText = ReadOptionalUtf8(payloadJson);
            if (!string.IsNullOrWhiteSpace(payloadText))
            {
                payload = ParsePayloadJson(payloadText);
            }

            entry.Collection.UpsertAsync(idText, managedVector, payload).AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_upsert_batch", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionUpsertBatch(
        nint collection,
        nint idsUtf8,
        int count,
        nint flatVectors,
        int dimensions,
        nint payloadsJson)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
            }
            if (dimensions != entry.Dimensions)
            {
                throw new ArgumentException(
                    $"Vector dimensions mismatch: expected {entry.Dimensions}, actual {dimensions}.",
                    nameof(dimensions));
            }
            if (idsUtf8 == 0 || flatVectors == 0)
            {
                throw new ArgumentNullException(nameof(idsUtf8));
            }

            string[] ids = ReadUtf8Array(idsUtf8, count, nameof(idsUtf8));
            int totalFloats = checked(count * dimensions);
            float[] vectors = new float[totalFloats];
            Marshal.Copy(flatVectors, vectors, 0, totalFloats);

            IReadOnlyList<IReadOnlyDictionary<string, object>?>? payloads = null;
            if (payloadsJson != 0)
            {
                var arr = new IReadOnlyDictionary<string, object>?[count];
                for (int i = 0; i < count; i++)
                {
                    nint slot = Marshal.ReadIntPtr(payloadsJson, i * IntPtr.Size);
                    if (slot == 0)
                    {
                        arr[i] = null;
                        continue;
                    }
                    string? text = Marshal.PtrToStringUTF8(slot);
                    arr[i] = string.IsNullOrWhiteSpace(text) ? null : ParsePayloadJson(text);
                }
                payloads = arr;
            }

            entry.Collection
                .UpsertBatchAsync(ids, vectors, dimensions, payloads)
                .AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_delete", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionDelete(nint collection, nint idsUtf8, int count)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
            }
            string[] ids = ReadUtf8Array(idsUtf8, count, nameof(idsUtf8));
            entry.Collection.DeleteAsync(ids).AsTask().GetAwaiter().GetResult();
            return Ok;
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_get", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionGet(
        nint collection,
        nint idsUtf8,
        int count,
        int includeVector,
        nint outBuffer,
        int bufferLength,
        nint outRequiredSize)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
            }
            string[] ids = ReadUtf8Array(idsUtf8, count, nameof(idsUtf8));

            IReadOnlyList<Point> points = entry.Collection
                .GetAsync(ids, includeVector != 0)
                .AsTask().GetAwaiter().GetResult();

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                for (int i = 0; i < points.Count; i++)
                {
                    WritePoint(writer, points[i], includeVector != 0);
                }
                writer.WriteEndArray();
            }
            return WriteVariableOutput(ms, outBuffer, bufferLength, outRequiredSize);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_search", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionSearch(
        nint collection,
        nint query,
        int dimensions,
        int topK,
        nint filterJson,
        int includeVector,
        nint outBuffer,
        int bufferLength,
        nint outRequiredSize)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            ValidateVectorPointer(query, dimensions, entry.Dimensions, nameof(query));
            if (topK <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(topK), topK, "TopK must be positive.");
            }

            float[] managedQuery = new float[dimensions];
            Marshal.Copy(query, managedQuery, 0, dimensions);

            Filter? filter = null;
            string? filterText = ReadOptionalUtf8(filterJson);
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filter = ParseFilterJson(filterText);
            }

            IReadOnlyList<ScoredPoint> hits = entry.Collection
                .SearchAsync(managedQuery, topK, filter, includeVector != 0)
                .AsTask().GetAwaiter().GetResult();

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                for (int i = 0; i < hits.Count; i++)
                {
                    WriteScoredPoint(writer, hits[i], includeVector != 0);
                }
                writer.WriteEndArray();
            }
            return WriteVariableOutput(ms, outBuffer, bufferLength, outRequiredSize);
        });

    [UnmanagedCallersOnly(EntryPoint = "dotvector_collection_query", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CollectionQuery(
        nint collection,
        nint filterJson,
        int top,
        int includeVector,
        nint outBuffer,
        int bufferLength,
        nint outRequiredSize)
        => GuardStatus(() =>
        {
            NativeCollection entry = GetCollection(collection);
            if (top <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(top), top, "Top must be positive.");
            }
            string filterText = ReadUtf8(filterJson, nameof(filterJson));
            if (string.IsNullOrWhiteSpace(filterText))
            {
                throw new ArgumentException("Filter JSON must not be empty.", nameof(filterJson));
            }

            Filter filter = ParseFilterJson(filterText);
            IReadOnlyList<Point> points = entry.Collection
                .QueryAsync(filter, top, includeVector != 0)
                .AsTask().GetAwaiter().GetResult();

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                for (int i = 0; i < points.Count; i++)
                {
                    WritePoint(writer, points[i], includeVector != 0);
                }
                writer.WriteEndArray();
            }
            return WriteVariableOutput(ms, outBuffer, bufferLength, outRequiredSize);
        });

    // --------------------------------------------------------------------- //
    // JSON helpers                                                          //
    // --------------------------------------------------------------------- //

    private static void WriteCollectionInfo(Utf8JsonWriter writer, CollectionInfo info)
    {
        writer.WriteStartObject();
        writer.WriteString("name", info.Name);
        writer.WriteNumber("dimensions", info.Dimensions);
        writer.WriteString("metric", info.Metric);
        writer.WriteNumber("record_count", info.RecordCount);
        writer.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter writer, Point point, bool includeVector)
    {
        writer.WriteStartObject();
        writer.WriteString("id", point.Id);
        if (point.Payload is { Count: > 0 })
        {
            writer.WritePropertyName("payload");
            WritePayload(writer, point.Payload);
        }
        if (includeVector && point.Vector is { Length: > 0 })
        {
            WriteVector(writer, "vector", point.Vector);
        }
        writer.WriteEndObject();
    }

    private static void WriteScoredPoint(Utf8JsonWriter writer, ScoredPoint hit, bool includeVector)
    {
        writer.WriteStartObject();
        writer.WriteString("id", hit.Id);
        writer.WriteNumber("score", hit.Score);
        if (hit.Payload is { Count: > 0 })
        {
            writer.WritePropertyName("payload");
            WritePayload(writer, hit.Payload);
        }
        if (includeVector && hit.Vector is { Length: > 0 })
        {
            WriteVector(writer, "vector", hit.Vector);
        }
        writer.WriteEndObject();
    }

    private static void WriteVector(Utf8JsonWriter writer, string property, float[] vector)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        for (int i = 0; i < vector.Length; i++)
        {
            writer.WriteNumberValue(vector[i]);
        }
        writer.WriteEndArray();
    }

    private static void WritePayload(Utf8JsonWriter writer, IReadOnlyDictionary<string, object> payload)
    {
        writer.WriteStartObject();
        foreach (var pair in payload)
        {
            writer.WritePropertyName(pair.Key);
            WritePayloadValue(writer, pair.Value);
        }
        writer.WriteEndObject();
    }

    private static void WritePayloadValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong u:
                writer.WriteNumberValue(u);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static IReadOnlyDictionary<string, object> ParsePayloadJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Payload JSON must be an object.", nameof(json));
        }
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            object? converted = ConvertJsonScalar(prop.Value, prop.Name);
            if (converted is null)
            {
                continue;
            }
            dict[prop.Name] = converted;
        }
        return dict;
    }

    private static object? ConvertJsonScalar(JsonElement element, string fieldName) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.TryGetInt64(out long l) ? (object)l : element.GetDouble(),
        _ => throw new ArgumentException(
            $"Payload field '{fieldName}' has unsupported JSON kind {element.ValueKind}.", nameof(fieldName)),
    };

    private static Filter ParseFilterJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return ParseFilterElement(doc.RootElement);
    }

    private static Filter ParseFilterElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Filter node must be a JSON object.");
        }

        // 单键对象：{ "<op>": <args> }
        JsonProperty op = default;
        int opCount = 0;
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            op = prop;
            opCount++;
        }
        if (opCount != 1)
        {
            throw new ArgumentException("Filter node must contain exactly one operator key.");
        }

        switch (op.Name)
        {
            case "eq":
                return ParseEqOrNe(op.Value, isEq: true);
            case "ne":
                return ParseEqOrNe(op.Value, isEq: false);
            case "range":
                return ParseRange(op.Value);
            case "exists":
                return Filter.Exists(op.Value.GetString()
                    ?? throw new ArgumentException("'exists' value must be a string field name."));
            case "missing":
                return Filter.Missing(op.Value.GetString()
                    ?? throw new ArgumentException("'missing' value must be a string field name."));
            case "and":
                return Filter.And(ParseFilterArray(op.Value));
            case "or":
                return Filter.Or(ParseFilterArray(op.Value));
            case "not":
                return Filter.Not(ParseFilterElement(op.Value));
            default:
                throw new ArgumentException($"Unknown filter operator '{op.Name}'.");
        }
    }

    private static Filter[] ParseFilterArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Filter combinator value must be an array.");
        }
        var list = new List<Filter>(element.GetArrayLength());
        foreach (JsonElement child in element.EnumerateArray())
        {
            list.Add(ParseFilterElement(child));
        }
        return list.ToArray();
    }

    private static Filter ParseEqOrNe(JsonElement element, bool isEq)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("eq/ne value must be an object {field: value}.");
        }
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            object? value = ConvertJsonScalar(prop.Value, prop.Name);
            return isEq ? Filter.Eq(prop.Name, value) : Filter.Ne(prop.Name, value);
        }
        throw new ArgumentException("eq/ne value object must contain exactly one field.");
    }

    private static Filter ParseRange(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("range value must be an object {field: {min, max, ...}}.");
        }
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            JsonElement spec = prop.Value;
            if (spec.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"range field '{prop.Name}' must map to an object.");
            }
            object? min = null, max = null;
            bool minInclusive = true, maxInclusive = true;
            foreach (JsonProperty s in spec.EnumerateObject())
            {
                switch (s.Name)
                {
                    case "min": min = ConvertJsonScalar(s.Value, s.Name); break;
                    case "max": max = ConvertJsonScalar(s.Value, s.Name); break;
                    case "min_inclusive": minInclusive = s.Value.GetBoolean(); break;
                    case "max_inclusive": maxInclusive = s.Value.GetBoolean(); break;
                    default: throw new ArgumentException($"Unknown range key '{s.Name}'.");
                }
            }
            return Filter.Range(prop.Name, (IComparable?)min, (IComparable?)max, minInclusive, maxInclusive);
        }
        throw new ArgumentException("range value object must contain exactly one field.");
    }

    private static int WriteVariableOutput(MemoryStream payload, nint outBuffer, int bufferLength, nint outRequiredSize)
    {
        int required = (int)payload.Length;
        if (outRequiredSize != 0)
        {
            Marshal.WriteInt32(outRequiredSize, required);
        }
        if (outBuffer == 0 || bufferLength <= 0 || bufferLength < required + 1)
        {
            return BufferTooSmall;
        }
        if (required > 0)
        {
            byte[] bytes = payload.GetBuffer();
            Marshal.Copy(bytes, 0, outBuffer, required);
        }
        Marshal.WriteByte(outBuffer, required, 0);
        return Ok;
    }

    // --------------------------------------------------------------------- //
    // Internal infrastructure                                               //
    // --------------------------------------------------------------------- //

    private static nint GuardHandle(Func<nint> body)
    {
        try
        {
            ClearError();
            return body();
        }
        catch (Exception ex)
        {
            SetError(ex);
            return 0;
        }
    }

    private static int GuardStatus(Func<int> body)
    {
        try
        {
            ClearError();
            return body();
        }
        catch (ArgumentException ex)
        {
            SetError(ex);
            return InvalidArgument;
        }
        catch (KeyNotFoundException ex)
        {
            SetError(ex);
            return NotFound;
        }
        catch (Exception ex)
        {
            SetError(ex);
            return Failed;
        }
    }

    private static nint AddDatabase(DotVectorClient client, string directoryPath, bool ownsDirectory)
    {
        nint handle = NewHandle();
        Databases[handle] = new NativeDatabase(client, directoryPath, ownsDirectory);
        return handle;
    }

    private static nint AddCollection(NativeDatabase database, DotVectorClientCollection collection, int dimensions)
    {
        nint handle = NewHandle();
        Collections[handle] = new NativeCollection(database, collection, dimensions);
        database.CollectionHandles.Add(handle);
        return handle;
    }

    private static nint NewHandle()
        => (nint)Interlocked.Increment(ref _nextHandle);

    private static NativeDatabase GetDatabase(nint database)
    {
        if (database == 0 || !Databases.TryGetValue(database, out NativeDatabase? entry))
        {
            throw new KeyNotFoundException("Database handle was not found.");
        }
        return entry;
    }

    private static NativeCollection GetCollection(nint collection)
    {
        if (collection == 0 || !Collections.TryGetValue(collection, out NativeCollection? entry))
        {
            throw new KeyNotFoundException("Collection handle was not found.");
        }
        return entry;
    }

    private static VectorDatabase GetUnderlyingDatabase(nint database)
    {
        NativeDatabase entry = GetDatabase(database);
        if (entry.Client.Protocol is LocalDotVectorClient local)
        {
            return local.Database;
        }
        throw new InvalidOperationException("Underlying database is not an embedded local database.");
    }

    private static CollectionInfo? DescribeFromList(DotVectorClient client, string name)
    {
        IReadOnlyList<CollectionInfo> all = client.ListCollectionsAsync().AsTask().GetAwaiter().GetResult();
        for (int i = 0; i < all.Count; i++)
        {
            if (string.Equals(all[i].Name, name, StringComparison.Ordinal))
            {
                return all[i];
            }
        }
        return null;
    }

    private static string ReadCollectionName(nint name)
    {
        string collectionName = ReadUtf8(name, nameof(name));
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must not be empty.", nameof(name));
        }
        return collectionName;
    }

    private static string ReadUtf8(nint pointer, string parameterName)
    {
        if (pointer == 0)
        {
            throw new ArgumentNullException(parameterName);
        }
        return Marshal.PtrToStringUTF8(pointer)
            ?? throw new ArgumentException("UTF-8 string pointer is invalid.", parameterName);
    }

    private static string? ReadOptionalUtf8(nint pointer)
        => pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);

    private static string[] ReadUtf8Array(nint arrayPointer, int count, string parameterName)
    {
        if (arrayPointer == 0)
        {
            throw new ArgumentNullException(parameterName);
        }
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            nint slot = Marshal.ReadIntPtr(arrayPointer, i * IntPtr.Size);
            if (slot == 0)
            {
                throw new ArgumentException($"{parameterName}[{i}] is null.", parameterName);
            }
            result[i] = Marshal.PtrToStringUTF8(slot)
                ?? throw new ArgumentException($"{parameterName}[{i}] is not valid UTF-8.", parameterName);
        }
        return result;
    }

    private static void ValidateCollectionOptions(int dimensions, int metric)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be positive.");
        }
        if (!Enum.IsDefined<Metric>((Metric)metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric), metric, "Metric is not supported.");
        }
    }

    private static void ValidateIndexKind(int indexKind)
    {
        if (!Enum.IsDefined<IndexKind>((IndexKind)indexKind))
        {
            throw new ArgumentOutOfRangeException(nameof(indexKind), indexKind, "Index kind is not supported.");
        }
    }

    private static DistanceMetric MapMetric(int metric) => (Metric)metric switch
    {
        Metric.Cosine => DistanceMetric.Cosine,
        Metric.L2 => DistanceMetric.L2,
        Metric.InnerProduct => DistanceMetric.InnerProduct,
        Metric.DotProduct => DistanceMetric.DotProduct,
        Metric.Hamming => DistanceMetric.Hamming,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Metric is not supported."),
    };

    private static void ValidateVectorPointer(nint vector, int dimensions, int expectedDimensions, string parameterName)
    {
        if (vector == 0)
        {
            throw new ArgumentNullException(parameterName);
        }
        if (dimensions != expectedDimensions)
        {
            throw new ArgumentException(
                $"Vector dimensions mismatch: expected {expectedDimensions}, actual {dimensions}.",
                nameof(dimensions));
        }
    }

    private static int CopyUtf8(string value, nint buffer, int bufferLength)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (buffer == 0 || bufferLength <= 0)
        {
            return bytes.Length;
        }
        int copyLength = Math.Min(bytes.Length, bufferLength - 1);
        if (copyLength > 0)
        {
            Marshal.Copy(bytes, 0, buffer, copyLength);
        }
        Marshal.WriteByte(buffer, copyLength, 0);
        return bytes.Length;
    }

    private static void ClearError() => _lastError = null;
    private static void SetError(Exception ex) => _lastError = ex.Message;
    private static void SetError(string message) => _lastError = message;

    private sealed class NativeDatabase(DotVectorClient client, string directoryPath, bool ownsDirectory)
    {
        public DotVectorClient Client { get; } = client;
        public string DirectoryPath { get; } = directoryPath;
        public bool OwnsDirectory { get; } = ownsDirectory;
        public ConcurrentBag<nint> CollectionHandles { get; } = [];
    }

    private sealed class NativeCollection(NativeDatabase database, DotVectorClientCollection collection, int dimensions)
    {
        public NativeDatabase Database { get; } = database;
        public DotVectorClientCollection Collection { get; } = collection;
        public int Dimensions { get; } = dimensions;
    }
}
