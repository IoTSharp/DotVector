using DotVector.Query;

namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 实体集合。一个集合可以映射到一个或多个底层向量字段集合。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
public sealed class DotVectorSet<TEntity>
    where TEntity : class
{
    private readonly DotVectorSetRuntime<TEntity> _runtime;

    internal DotVectorSet(DotVectorSetRuntime<TEntity> runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    /// <summary>实体 set 名称。</summary>
    public string Name => _runtime.SetName;

    /// <summary>已注册的向量字段名称。</summary>
    public IReadOnlyList<string> VectorFields => _runtime.VectorFields;

    /// <summary>
    /// 插入实体。实体包含多个向量字段时会写入所有底层向量集合。
    /// </summary>
    /// <param name="entity">要插入的实体。</param>
    public void Insert(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _runtime.Insert(entity, vectorFieldName: null);
    }

    /// <summary>
    /// 只插入实体的指定向量字段。
    /// </summary>
    /// <param name="entity">要插入的实体。</param>
    /// <param name="vectorFieldName">向量字段名称。</param>
    public void Insert(TEntity entity, string vectorFieldName)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(vectorFieldName);
        _runtime.Insert(entity, vectorFieldName);
    }

    /// <summary>
    /// 批量插入实体。
    /// </summary>
    /// <param name="entities">实体列表。</param>
    public void InsertBatch(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (TEntity entity in entities)
        {
            Insert(entity);
        }
    }

    /// <summary>
    /// 按向量字段执行近似最近邻搜索。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>搜索结果。</returns>
    public IReadOnlyList<DotVectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int topK = 10,
        string? vectorFieldName = null,
        Filter? filter = null)
        => _runtime.Search(query, topK, vectorFieldName, filter);

    /// <summary>
    /// 删除指定主键。实体包含多个向量字段时会从所有底层向量集合删除。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <returns>至少一个底层集合删除成功时返回 <see langword="true"/>。</returns>
    public bool Delete(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _runtime.Delete(key, vectorFieldName: null);
    }

    /// <summary>
    /// 从指定向量字段对应的底层集合删除主键。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vectorFieldName">向量字段名称。</param>
    /// <returns>删除成功返回 <see langword="true"/>。</returns>
    public bool Delete(object key, string vectorFieldName)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(vectorFieldName);
        return _runtime.Delete(key, vectorFieldName);
    }

    /// <summary>
    /// 获取底层集合中的记录数。
    /// </summary>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <returns>记录数。</returns>
    public long Count(string? vectorFieldName = null) => _runtime.Count(vectorFieldName);
}
