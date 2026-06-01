using System.Linq.Expressions;
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
    /// 按向量字段执行近似最近邻搜索。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="topK">返回结果数量。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>搜索结果。</returns>
    public IReadOnlyList<DotVectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int topK,
        Expression<Func<TEntity, object?>> vectorSelector,
        Filter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(vectorSelector);
        return Search(query, topK, ResolveVectorFieldName(vectorSelector), filter);
    }

    /// <summary>
    /// 返回最相似的一条记录。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>命中记录；未命中返回 <see langword="null"/>。</returns>
    public DotVectorSearchResult? SearchTop1(
        ReadOnlySpan<float> query,
        string? vectorFieldName = null,
        Filter? filter = null)
    {
        IReadOnlyList<DotVectorSearchResult> results = Search(query, topK: 1, vectorFieldName, filter);
        return results.Count == 0 ? null : results[0];
    }

    /// <summary>
    /// 返回最相似的一条记录。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>命中记录；未命中返回 <see langword="null"/>。</returns>
    public DotVectorSearchResult? SearchTop1(
        ReadOnlySpan<float> query,
        Expression<Func<TEntity, object?>> vectorSelector,
        Filter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(vectorSelector);
        return SearchTop1(query, ResolveVectorFieldName(vectorSelector), filter);
    }

    /// <summary>
    /// 返回满足分数阈值的相似记录。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="threshold">阈值。距离型度量使用小于等于；相似度型度量使用大于等于。</param>
    /// <param name="topK">候选返回数量。</param>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>满足阈值的搜索结果。</returns>
    public IReadOnlyList<DotVectorSearchResult> SearchByThreshold(
        ReadOnlySpan<float> query,
        float threshold,
        int topK = 10,
        string? vectorFieldName = null,
        Filter? filter = null)
        => _runtime.SearchByThreshold(query, threshold, topK, vectorFieldName, filter);

    /// <summary>
    /// 返回满足分数阈值的相似记录。
    /// </summary>
    /// <param name="query">查询向量。</param>
    /// <param name="threshold">阈值。距离型度量使用小于等于；相似度型度量使用大于等于。</param>
    /// <param name="topK">候选返回数量。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    /// <param name="filter">可选 payload 过滤条件。</param>
    /// <returns>满足阈值的搜索结果。</returns>
    public IReadOnlyList<DotVectorSearchResult> SearchByThreshold(
        ReadOnlySpan<float> query,
        float threshold,
        int topK,
        Expression<Func<TEntity, object?>> vectorSelector,
        Filter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(vectorSelector);
        return SearchByThreshold(query, threshold, topK, ResolveVectorFieldName(vectorSelector), filter);
    }

    /// <summary>
    /// 插入或覆盖实体。实体包含多个向量字段时会写入所有底层向量集合。
    /// </summary>
    /// <param name="entity">要写入的实体。</param>
    public void Upsert(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _runtime.Upsert(entity, vectorFieldName: null);
    }

    /// <summary>
    /// 插入或覆盖实体的指定向量字段。
    /// </summary>
    /// <param name="entity">要写入的实体。</param>
    /// <param name="vectorFieldName">向量字段名称。</param>
    public void Upsert(TEntity entity, string vectorFieldName)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(vectorFieldName);
        _runtime.Upsert(entity, vectorFieldName);
    }

    /// <summary>
    /// 插入或覆盖实体的指定向量字段。
    /// </summary>
    /// <param name="entity">要写入的实体。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    public void Upsert(TEntity entity, Expression<Func<TEntity, object?>> vectorSelector)
    {
        ArgumentNullException.ThrowIfNull(vectorSelector);
        Upsert(entity, ResolveVectorFieldName(vectorSelector));
    }

    /// <summary>
    /// 按主键查找记录。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <returns>命中记录；未命中返回 <see langword="null"/>。</returns>
    public DotVectorRecordResult? Find(object key, string? vectorFieldName = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _runtime.Find(key, vectorFieldName);
    }

    /// <summary>
    /// 按主键查找记录。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    /// <returns>命中记录；未命中返回 <see langword="null"/>。</returns>
    public DotVectorRecordResult? Find(object key, Expression<Func<TEntity, object?>> vectorSelector)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(vectorSelector);
        return Find(key, ResolveVectorFieldName(vectorSelector));
    }

    /// <summary>
    /// 按主键获取记录。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vectorFieldName">向量字段名称；单向量实体可省略。</param>
    /// <returns>命中记录。</returns>
    /// <exception cref="KeyNotFoundException">记录不存在。</exception>
    public DotVectorRecordResult Get(object key, string? vectorFieldName = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Find(key, vectorFieldName)
            ?? throw new KeyNotFoundException($"实体 {typeof(TEntity).FullName} 中不存在主键 '{key}' 的记录。");
    }

    /// <summary>
    /// 按主键获取记录。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vectorSelector">向量字段选择器。</param>
    /// <returns>命中记录。</returns>
    /// <exception cref="KeyNotFoundException">记录不存在。</exception>
    public DotVectorRecordResult Get(object key, Expression<Func<TEntity, object?>> vectorSelector)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(vectorSelector);
        return Get(key, ResolveVectorFieldName(vectorSelector));
    }

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

    private string ResolveVectorFieldName(Expression<Func<TEntity, object?>> vectorSelector)
    {
        Expression body = vectorSelector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            body = convert.Operand;
        }

        if (body is MemberExpression member)
        {
            return _runtime.ResolveVectorFieldName(member.Member.Name);
        }

        throw new NotSupportedException(
            $"向量字段 selector 仅支持直接属性访问，例如 x => x.Vector。表达式：{vectorSelector}。");
    }
}
