using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotVector.Api;

namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 嵌入式上下文。负责把实体 schema 与 <see cref="VectorDatabase"/> 绑定，
/// 并为 <see cref="DotVectorSet{TEntity}"/> 属性赋值。
/// </summary>
/// <remarks>
/// 默认构造会通过反射发现派生上下文上的 <see cref="DotVectorSet{TEntity}"/> 属性。
/// Native AOT 或 trim 敏感应用可使用 <see cref="Set{TEntity}"/> 配合显式 schema 注册，避免运行时属性扫描。
/// </remarks>
public abstract class DotVectorDbContext : IDisposable
{
    private readonly Dictionary<Type, object> _schemas = new();
    private readonly Dictionary<Type, object> _sets = new();
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="DotVectorDbContext"/>，使用外部传入的数据库实例。
    /// </summary>
    /// <param name="database">向量数据库实例。</param>
    protected DotVectorDbContext(VectorDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        Database = database;
    }

    /// <summary>
    /// 初始化 <see cref="DotVectorDbContext"/>，并打开指定 <c>.dvec/</c> 数据库目录。
    /// </summary>
    /// <param name="directoryPath">数据库目录路径。</param>
    protected DotVectorDbContext(string directoryPath)
        : this(new VectorDatabase(directoryPath))
    {
    }

    /// <summary>底层向量数据库实例。</summary>
    public VectorDatabase Database { get; }

    /// <summary>
    /// 显式注册实体 schema。该路径不扫描实体类型，是 Native AOT 下的推荐兜底方式。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <param name="schema">实体 schema。</param>
    protected void RegisterSchema<TEntity, TKey>(DotVectorEntitySchema<TEntity, TKey> schema)
        where TEntity : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemas[typeof(TEntity)] = schema;
    }

    /// <summary>
    /// 绑定派生上下文上声明的 <see cref="DotVectorSet{TEntity}"/> 属性。
    /// </summary>
    /// <remarks>
    /// 该方法会扫描派生上下文的公共和非公共实例属性。Native AOT 下可跳过该方法，
    /// 并通过 <see cref="Set{TEntity}"/> 显式获取集合。
    /// </remarks>
    [RequiresUnreferencedCode("DotVectorDbContext.BindSets 会反射扫描上下文属性和实体 Attribute。Native AOT 下请显式注册 schema 并调用 Set<TEntity>()。")]
    [RequiresDynamicCode("DotVectorDbContext.BindSets 会通过反射构造泛型 set。Native AOT 下请显式注册 schema 并调用 Set<TEntity>()。")]
    protected void BindSets()
    {
        ThrowIfDisposed();
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (PropertyInfo property in GetType().GetProperties(Flags))
        {
            Type propertyType = property.PropertyType;
            if (!propertyType.IsGenericType || propertyType.GetGenericTypeDefinition() != typeof(DotVectorSet<>))
            {
                continue;
            }
            if (property.SetMethod is null)
            {
                throw new InvalidOperationException(
                    $"上下文属性 {GetType().FullName}.{property.Name} 必须提供 setter，才能由 DotVectorDbContext 绑定。");
            }

            Type entityType = propertyType.GetGenericArguments()[0];
            if (!_schemas.ContainsKey(entityType))
            {
                _schemas[entityType] = CreateSchemaFromAttributes(entityType);
            }
            object set = GetOrCreateSet(entityType, property.Name);
            property.SetValue(this, set);
        }
    }

    /// <summary>
    /// 获取指定实体类型的 Code-First 集合。若尚未创建，会按已注册 schema 创建并绑定底层集合。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <returns>实体集合。</returns>
    public DotVectorSet<TEntity> Set<TEntity>()
        where TEntity : class
    {
        ThrowIfDisposed();
        return (DotVectorSet<TEntity>)GetOrCreateSet(typeof(TEntity), setName: null);
    }

    /// <summary>
    /// 将所有底层集合刷盘。
    /// </summary>
    public void Flush()
    {
        ThrowIfDisposed();
        Database.Flush();
    }

    /// <summary>
    /// 合并底层持久化 Segment。
    /// </summary>
    public void Compact()
    {
        ThrowIfDisposed();
        Database.Compact();
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Database.Dispose();
        GC.SuppressFinalize(this);
    }

    private object GetOrCreateSet(Type entityType, string? setName)
    {
        if (_sets.TryGetValue(entityType, out object? existing))
        {
            return existing;
        }

        if (!_schemas.TryGetValue(entityType, out object? schema))
        {
            throw new InvalidOperationException(
                $"实体 {entityType.FullName} 尚未注册 Code-First schema。请在上下文构造函数中调用 RegisterSchema，或调用 BindSets() 使用 Attribute 自动发现。");
        }

        object set = CreateSetFromSchema(schema, setName);
        _sets[entityType] = set;
        return set;
    }

    [RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    private static object CreateSchemaFromAttributes(Type entityType)
    {
        Type keyType = DotVectorAttributeSchemaBuilder.FindKeyType(entityType);
        MethodInfo method = typeof(DotVectorEntitySchema)
            .GetMethod(nameof(DotVectorEntitySchema.FromAttributes), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("无法找到 DotVectorEntitySchema.FromAttributes 方法。");
        MethodInfo closed = method.MakeGenericMethod(entityType, keyType);
        return closed.Invoke(null, parameters: null)
            ?? throw new InvalidOperationException($"无法为 {entityType.FullName} 创建 Code-First schema。");
    }

    private object CreateSetFromSchema(object schema, string? setName)
    {
        if (schema is not IDotVectorEntitySchema typed)
        {
            throw new InvalidOperationException(
                $"schema 对象 {schema.GetType().FullName} 不是有效的 DotVectorEntitySchema。");
        }
        return typed.CreateSet(Database, setName);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
