using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.VectorData;

namespace DotVector.Data.Internal;

/// <summary>
/// 基于反射的记录映射器：把用户定义的 <typeparamref name="TRecord"/>
/// 类型在 VectorData 接口与 DotVector 协议 DTO 之间互相转换。
/// </summary>
/// <typeparam name="TKey">主键类型。</typeparam>
/// <typeparam name="TRecord">VectorData 用户记录类型。必须有公共无参构造器，
/// 且包含一个 <see cref="VectorStoreKeyAttribute"/> 属性、一个
/// <see cref="VectorStoreVectorAttribute"/> 属性。</typeparam>
/// <remarks>
/// 这是 <strong>M7 的最小实现</strong>：
/// <list type="bullet">
///   <item><description>支持向量字段类型：<see cref="ReadOnlyMemory{T}"/> of <see cref="float"/> 与 <c>float[]</c></description></item>
///   <item><description>数据字段（标记 <see cref="VectorStoreDataAttribute"/>）整体序列化为 payload 字典；非空才写入</description></item>
///   <item><description>反射操作受 trim/AOT 影响，调用方必须在调用 <see cref="DotVectorVectorStore"/>
///     时知晓本类的 <see cref="RequiresUnreferencedCodeAttribute"/> / <see cref="RequiresDynamicCodeAttribute"/> 警告。</description></item>
/// </list>
/// TODO(M7+): 支持 <see cref="VectorStoreCollectionDefinition"/>（无属性标注、动态 schema）。
/// </remarks>
[RequiresUnreferencedCode("DotVectorRecordMapper 通过反射访问 TRecord 的属性，可能被 trim 移除。")]
[RequiresDynamicCode("DotVectorRecordMapper 通过反射访问 TRecord 的属性，AOT 下可能不可用。")]
internal sealed class DotVectorRecordMapper<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly PropertyInfo _keyProperty;
    private readonly PropertyInfo _vectorProperty;
    private readonly bool _vectorIsArray; // false => ReadOnlyMemory<float>
    private readonly PropertyInfo[] _dataProperties;
    private readonly Dictionary<string, PropertyInfo> _dataByStorageName;

    /// <summary>记录中向量字段声明的维度。</summary>
    public int Dimensions { get; }

    /// <summary>向量字段上声明的距离函数（来自 <see cref="VectorStoreVectorAttribute.DistanceFunction"/>）。</summary>
    public string? DistanceFunction { get; }

    public DotVectorRecordMapper()
    {
        var t = typeof(TRecord);
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        PropertyInfo? key = null;
        PropertyInfo? vector = null;
        var data = new List<PropertyInfo>();
        int dims = 0;
        string? distance = null;

        foreach (var p in props)
        {
            if (p.GetCustomAttribute<VectorStoreKeyAttribute>() is not null)
            {
                if (key is not null)
                {
                    throw new InvalidOperationException(
                        $"{t.FullName} 上声明了多个 [VectorStoreKey] 属性，DotVector.Data 仅支持单主键。");
                }
                if (p.PropertyType != typeof(TKey))
                {
                    throw new InvalidOperationException(
                        $"{t.FullName}.{p.Name} 的类型 {p.PropertyType.Name} 与 TKey={typeof(TKey).Name} 不一致。");
                }
                key = p;
                continue;
            }

            if (p.GetCustomAttribute<VectorStoreVectorAttribute>() is { } vAttr)
            {
                if (vector is not null)
                {
                    throw new InvalidOperationException(
                        $"{t.FullName} 上声明了多个 [VectorStoreVector] 属性，DotVector.Data 当前仅支持单向量字段。");
                }
                vector = p;
                dims = vAttr.Dimensions;
                distance = vAttr.DistanceFunction;
                continue;
            }

            if (p.GetCustomAttribute<VectorStoreDataAttribute>() is not null)
            {
                data.Add(p);
            }
        }

        if (key is null)
        {
            throw new InvalidOperationException(
                $"{t.FullName} 必须包含一个标注 [VectorStoreKey] 的属性。");
        }
        if (vector is null)
        {
            throw new InvalidOperationException(
                $"{t.FullName} 必须包含一个标注 [VectorStoreVector] 的属性。");
        }

        if (vector.PropertyType == typeof(float[]))
        {
            _vectorIsArray = true;
        }
        else if (vector.PropertyType == typeof(ReadOnlyMemory<float>))
        {
            _vectorIsArray = false;
        }
        else
        {
            throw new NotSupportedException(
                $"{t.FullName}.{vector.Name} 的类型 {vector.PropertyType.Name} 不受支持。" +
                " DotVector.Data 当前仅支持 float[] 与 ReadOnlyMemory<float>。");
        }

        _keyProperty = key;
        _vectorProperty = vector;
        _dataProperties = data.ToArray();
        _dataByStorageName = data.ToDictionary(
            p => p.GetCustomAttribute<VectorStoreDataAttribute>()?.StorageName ?? p.Name,
            StringComparer.Ordinal);
        Dimensions = dims;
        DistanceFunction = distance;
    }

    /// <summary>从记录中读取主键。</summary>
    public TKey GetKey(TRecord record)
    {
        var v = _keyProperty.GetValue(record)
            ?? throw new InvalidOperationException(
                $"{typeof(TRecord).Name}.{_keyProperty.Name} 不能为 null。");
        return (TKey)v;
    }

    /// <summary>从记录中读取向量数据并返回为 <c>float[]</c>。</summary>
    public float[] GetVector(TRecord record)
    {
        var raw = _vectorProperty.GetValue(record)
            ?? throw new InvalidOperationException(
                $"{typeof(TRecord).Name}.{_vectorProperty.Name} 不能为 null。");
        if (_vectorIsArray)
        {
            return (float[])raw;
        }

        var rom = (ReadOnlyMemory<float>)raw;
        return rom.ToArray();
    }

    /// <summary>把记录的 <c>[VectorStoreData]</c> 字段编码为协议层 payload 字典。</summary>
    public IReadOnlyDictionary<string, object>? GetPayload(TRecord record)
    {
        if (_dataProperties.Length == 0)
        {
            return null;
        }

        Dictionary<string, object>? dict = null;
        foreach (var p in _dataProperties)
        {
            var v = p.GetValue(record);
            if (v is null)
            {
                continue;
            }

            dict ??= new Dictionary<string, object>(StringComparer.Ordinal);
            var name = p.GetCustomAttribute<VectorStoreDataAttribute>()?.StorageName ?? p.Name;
            dict[name] = v;
        }

        return dict;
    }

    /// <summary>
    /// 把 <see cref="Core.Protocol.VectorSearchResult"/> 反向构造为 <typeparamref name="TRecord"/>。
    /// 当前 M7 仅恢复主键 + 数据字段；向量字段保持类型默认值。
    /// </summary>
    public TRecord CreateRecord(string id, IReadOnlyDictionary<string, object>? payload)
        => CreateRecord(id, payload, vector: null);

    /// <summary>
    /// 把 <see cref="Core.Protocol.VectorSearchResult"/> / <see cref="Core.Protocol.VectorRecordDto"/>
    /// 反向构造为 <typeparamref name="TRecord"/>，并在 <paramref name="vector"/> 非 <see langword="null"/>
    /// 时回填向量属性（M7.1 <c>IncludeVectors</c> 支持）。
    /// </summary>
    public TRecord CreateRecord(string id, IReadOnlyDictionary<string, object>? payload, float[]? vector)
    {
        var record = (TRecord?)Activator.CreateInstance(typeof(TRecord))
            ?? throw new InvalidOperationException(
                $"无法创建 {typeof(TRecord).FullName} 的实例，请确保其定义了公共无参构造器。");
        _keyProperty.SetValue(record, KeyConverter<TKey>.FromProtocolId(id));

        if (payload is not null)
        {
            foreach (var kv in payload)
            {
                if (!_dataByStorageName.TryGetValue(kv.Key, out var prop))
                {
                    continue;
                }

                if (kv.Value is null && Nullable.GetUnderlyingType(prop.PropertyType) is null
                    && prop.PropertyType.IsValueType)
                {
                    continue;
                }

                if (kv.Value is not null && !prop.PropertyType.IsInstanceOfType(kv.Value))
                {
                    var converted = Convert.ChangeType(
                        kv.Value,
                        Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType,
                        System.Globalization.CultureInfo.InvariantCulture);
                    prop.SetValue(record, converted);
                }
                else
                {
                    prop.SetValue(record, kv.Value);
                }
            }
        }

        if (vector is not null)
        {
            object boxed = _vectorIsArray ? vector : (object)new ReadOnlyMemory<float>(vector);
            _vectorProperty.SetValue(record, boxed);
        }

        return record;
    }
}
