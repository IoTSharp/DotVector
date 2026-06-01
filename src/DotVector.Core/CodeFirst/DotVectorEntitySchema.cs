using DotVector.Model;
using DotVector.Api;

namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 实体 schema。描述一个实体如何映射到一个或多个 DotVector 向量集合。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public sealed class DotVectorEntitySchema<TEntity, TKey>
    : IDotVectorEntitySchema
    where TEntity : class
    where TKey : notnull
{
    private readonly Dictionary<string, DotVectorVectorFieldMetadata> _vectors;

    private DotVectorEntitySchema(
        string? setName,
        DotVectorEntityAccessors<TEntity, TKey> accessors,
        IReadOnlyList<DotVectorVectorFieldMetadata> vectors)
    {
        ArgumentNullException.ThrowIfNull(accessors);
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Count == 0)
        {
            throw new ArgumentException("Code-First 实体至少需要一个向量字段。", nameof(vectors));
        }

        SetName = string.IsNullOrWhiteSpace(setName) ? null : setName;
        Accessors = accessors;
        _vectors = new Dictionary<string, DotVectorVectorFieldMetadata>(StringComparer.Ordinal);
        foreach (DotVectorVectorFieldMetadata vector in vectors)
        {
            if (!_vectors.TryAdd(vector.Name, vector))
            {
                throw new InvalidOperationException(
                    $"实体 {typeof(TEntity).FullName} 重复注册向量字段 '{vector.Name}'。");
            }
            if (!accessors.VectorGetters.ContainsKey(vector.Name))
            {
                throw new InvalidOperationException(
                    $"实体 {typeof(TEntity).FullName} 的访问器缺少向量字段 '{vector.Name}'。");
            }
        }

        Vectors = _vectors.Values.OrderBy(static v => v.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>默认 set 名称；为 <see langword="null"/> 时由上下文属性名或实体类型名提供。</summary>
    public string? SetName { get; }

    /// <summary>实体访问器集合。</summary>
    public DotVectorEntityAccessors<TEntity, TKey> Accessors { get; }

    /// <summary>向量字段列表。</summary>
    public IReadOnlyList<DotVectorVectorFieldMetadata> Vectors { get; }

    Type IDotVectorEntitySchema.EntityType => typeof(TEntity);

    object IDotVectorEntitySchema.CreateSet(VectorDatabase database, string? setName)
    {
        ArgumentNullException.ThrowIfNull(database);
        string resolvedSetName = ResolveSetName(setName);
        var runtime = new DotVectorSetRuntime<TEntity, TKey>(database, this, resolvedSetName);
        return new DotVectorSet<TEntity>(runtime);
    }

    /// <summary>
    /// 基于显式访问器创建实体 schema。该入口不扫描实体类型，适合作为 AOT 兜底注册方式。
    /// </summary>
    /// <param name="accessors">实体访问器集合。</param>
    /// <param name="vectors">向量字段列表。</param>
    /// <param name="setName">默认 set 名称。</param>
    /// <returns>实体 schema。</returns>
    public static DotVectorEntitySchema<TEntity, TKey> Create(
        DotVectorEntityAccessors<TEntity, TKey> accessors,
        IReadOnlyList<DotVectorVectorFieldMetadata> vectors,
        string? setName = null)
        => new(setName, accessors, vectors);

    /// <summary>
    /// 基于运行时 Attribute 扫描创建实体 schema。
    /// </summary>
    /// <returns>实体 schema。</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    public static DotVectorEntitySchema<TEntity, TKey> FromAttributes()
        => DotVectorAttributeSchemaBuilder.Build<TEntity, TKey>();

    internal DotVectorVectorFieldMetadata GetVector(string? vectorFieldName)
    {
        if (vectorFieldName is null)
        {
            if (Vectors.Count == 1)
            {
                return Vectors[0];
            }

            throw new InvalidOperationException(
                $"实体 {typeof(TEntity).FullName} 注册了多个向量字段，请显式指定 vectorFieldName。");
        }

        if (_vectors.TryGetValue(vectorFieldName, out DotVectorVectorFieldMetadata? metadata))
        {
            return metadata;
        }

        throw new KeyNotFoundException(
            $"实体 {typeof(TEntity).FullName} 未注册名为 '{vectorFieldName}' 的向量字段。");
    }

    internal string ResolveCollectionName(DotVectorVectorFieldMetadata vector, string setName)
        => vector.CollectionName ?? (Vectors.Count == 1 ? setName : setName + "_" + vector.Name);

    private string ResolveSetName(string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            return requestedName;
        }
        if (!string.IsNullOrWhiteSpace(SetName))
        {
            return SetName!;
        }
        return typeof(TEntity).Name;
    }
}

internal interface IDotVectorEntitySchema
{
    Type EntityType { get; }

    object CreateSet(VectorDatabase database, string? setName);
}

/// <summary>
/// 创建 <see cref="DotVectorEntitySchema{TEntity,TKey}"/> 的便捷工厂。
/// </summary>
public static class DotVectorEntitySchema
{
    /// <summary>
    /// 基于显式访问器创建实体 schema。该入口不扫描实体类型，适合作为 AOT 兜底注册方式。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <param name="keyGetter">主键读取器。</param>
    /// <param name="vectorGetters">按向量字段名称索引的向量读取器。</param>
    /// <param name="payloadGetter">payload 读取器。</param>
    /// <param name="vectors">向量字段列表。</param>
    /// <param name="setName">默认 set 名称。</param>
    /// <returns>实体 schema。</returns>
    public static DotVectorEntitySchema<TEntity, TKey> Create<TEntity, TKey>(
        Func<TEntity, TKey> keyGetter,
        IReadOnlyDictionary<string, Func<TEntity, ReadOnlyMemory<float>>> vectorGetters,
        Func<TEntity, IReadOnlyDictionary<string, object?>?> payloadGetter,
        IReadOnlyList<DotVectorVectorFieldMetadata> vectors,
        string? setName = null)
        where TEntity : class
        where TKey : notnull
    {
        var accessors = new DotVectorEntityAccessors<TEntity, TKey>(keyGetter, vectorGetters, payloadGetter);
        return DotVectorEntitySchema<TEntity, TKey>.Create(accessors, vectors, setName);
    }

    /// <summary>
    /// 创建单向量字段实体 schema。该入口不扫描实体类型，适合作为 AOT 兜底注册方式。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <param name="keyGetter">主键读取器。</param>
    /// <param name="vectorGetter">向量读取器。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="payloadGetter">payload 读取器。</param>
    /// <param name="setName">默认 set 名称。</param>
    /// <param name="vectorName">向量字段名称。</param>
    /// <param name="metric">距离度量。</param>
    /// <param name="indexKind">索引类型。</param>
    /// <param name="indexOptions">索引参数。</param>
    /// <param name="collectionName">显式底层集合名称。</param>
    /// <returns>实体 schema。</returns>
    public static DotVectorEntitySchema<TEntity, TKey> Create<TEntity, TKey>(
        Func<TEntity, TKey> keyGetter,
        Func<TEntity, ReadOnlyMemory<float>> vectorGetter,
        int dimensions,
        Func<TEntity, IReadOnlyDictionary<string, object?>?>? payloadGetter = null,
        string? setName = null,
        string vectorName = DotVectorSchemaDefaults.DefaultVectorName,
        Metric metric = Metric.Cosine,
        IndexKind indexKind = IndexKind.Flat,
        DotVectorIndexOptions? indexOptions = null,
        string? collectionName = null)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keyGetter);
        ArgumentNullException.ThrowIfNull(vectorGetter);
        var vectors = new[]
        {
            new DotVectorVectorFieldMetadata(
                vectorName,
                collectionName,
                dimensions,
                metric,
                indexKind,
                indexOptions ?? new DotVectorIndexOptions()),
        };
        var getters = new Dictionary<string, Func<TEntity, ReadOnlyMemory<float>>>(StringComparer.Ordinal)
        {
            [vectorName] = vectorGetter,
        };
        return Create(
            keyGetter,
            getters,
            payloadGetter ?? (static _ => null),
            vectors,
            setName);
    }

    /// <summary>
    /// 基于运行时 Attribute 扫描创建实体 schema。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <returns>实体 schema。</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    public static DotVectorEntitySchema<TEntity, TKey> FromAttributes<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
        => DotVectorEntitySchema<TEntity, TKey>.FromAttributes();
}
