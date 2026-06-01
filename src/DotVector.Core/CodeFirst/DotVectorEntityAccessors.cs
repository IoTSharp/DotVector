using System.Diagnostics.CodeAnalysis;

namespace DotVector.CodeFirst;

/// <summary>
/// 实体访问器集合。用于在热路径上读取主键、向量和 payload，避免每次插入或搜索时反射访问属性。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public sealed class DotVectorEntityAccessors<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// 初始化 <see cref="DotVectorEntityAccessors{TEntity,TKey}"/>。
    /// </summary>
    /// <param name="keyGetter">主键读取器。</param>
    /// <param name="vectorGetters">按向量字段名称索引的向量读取器。</param>
    /// <param name="payloadGetter">payload 读取器。</param>
    public DotVectorEntityAccessors(
        Func<TEntity, TKey> keyGetter,
        IReadOnlyDictionary<string, Func<TEntity, ReadOnlyMemory<float>>> vectorGetters,
        Func<TEntity, IReadOnlyDictionary<string, object?>?> payloadGetter)
    {
        ArgumentNullException.ThrowIfNull(keyGetter);
        ArgumentNullException.ThrowIfNull(vectorGetters);
        ArgumentNullException.ThrowIfNull(payloadGetter);
        KeyGetter = keyGetter;
        VectorGetters = vectorGetters;
        PayloadGetter = payloadGetter;
    }

    /// <summary>主键读取器。</summary>
    public Func<TEntity, TKey> KeyGetter { get; }

    /// <summary>按向量字段名称索引的向量读取器。</summary>
    public IReadOnlyDictionary<string, Func<TEntity, ReadOnlyMemory<float>>> VectorGetters { get; }

    /// <summary>payload 读取器。</summary>
    public Func<TEntity, IReadOnlyDictionary<string, object?>?> PayloadGetter { get; }

    internal Func<TEntity, ReadOnlyMemory<float>> GetVectorGetter(string vectorFieldName)
    {
        if (VectorGetters.TryGetValue(vectorFieldName, out Func<TEntity, ReadOnlyMemory<float>>? getter))
        {
            return getter;
        }

        throw new KeyNotFoundException(
            $"实体 {typeof(TEntity).FullName} 未注册名为 '{vectorFieldName}' 的向量字段。");
    }
}

/// <summary>
/// 基于运行时 Attribute 扫描创建 Code-First 实体访问器。
/// </summary>
/// <remarks>
/// 该工厂依赖反射和表达式编译。Native AOT 或 trim 敏感应用应优先通过
/// <see cref="DotVectorEntitySchema.Create{TEntity,TKey}"/> 显式注册访问器。
/// </remarks>
public static class DotVectorEntityAccessors
{
    /// <summary>
    /// 从实体类型的 <see cref="DotVectorKeyAttribute"/> / <see cref="DotVectorVectorAttribute"/>
    /// 标记属性创建访问器。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <returns>编译后的实体访问器。</returns>
    [RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    public static DotVectorEntityAccessors<TEntity, TKey> FromAttributes<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
        => DotVectorAttributeSchemaBuilder.Build<TEntity, TKey>().Accessors;
}
