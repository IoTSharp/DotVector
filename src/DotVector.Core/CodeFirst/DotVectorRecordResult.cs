namespace DotVector.CodeFirst;

/// <summary>
/// Code-First 按主键读取返回的记录。
/// </summary>
public sealed class DotVectorRecordResult
{
    /// <summary>
    /// 初始化 <see cref="DotVectorRecordResult"/>。
    /// </summary>
    /// <param name="key">记录主键。</param>
    /// <param name="vector">向量数据。</param>
    /// <param name="vectorFieldName">记录所属向量字段名称。</param>
    public DotVectorRecordResult(object key, IReadOnlyList<float> vector, string vectorFieldName)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentException.ThrowIfNullOrEmpty(vectorFieldName);
        Key = key;
        Vector = vector;
        VectorFieldName = vectorFieldName;
    }

    /// <summary>记录主键。</summary>
    public object Key { get; }

    /// <summary>记录向量。</summary>
    public IReadOnlyList<float> Vector { get; }

    /// <summary>记录所属向量字段名称。</summary>
    public string VectorFieldName { get; }

    /// <summary>记录 payload。</summary>
    public IReadOnlyDictionary<string, object?>? Payload { get; init; }
}
