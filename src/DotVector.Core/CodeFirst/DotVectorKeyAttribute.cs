namespace DotVector.CodeFirst;

/// <summary>
/// 标记实体的主键属性。Code-First 映射会用该属性作为所有向量集合的记录键。
/// </summary>
/// <remarks>
/// 当前仅支持 <see cref="int"/>、<see cref="long"/>、<see cref="Guid"/> 与 <see cref="string"/>。
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DotVectorKeyAttribute : Attribute
{
}
