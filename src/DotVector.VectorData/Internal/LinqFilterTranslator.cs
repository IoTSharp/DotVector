using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using DotVector.Query;

namespace DotVector.VectorData.Internal;

/// <summary>
/// 将 <see cref="Microsoft.Extensions.VectorData.VectorSearchOptions{TRecord}.Filter"/>
/// 与 <c>VectorStoreCollection.GetAsync(filter, ...)</c> 使用的
/// <see cref="Expression{TDelegate}"/>(<c>Func&lt;TRecord, bool&gt;</c>) 翻译为
/// DotVector 内部的 <see cref="Filter"/> IR（M7.2）。
/// </summary>
/// <remarks>
/// <para>支持的语法（左侧必须是当前 lambda 参数的属性访问，右侧为常量/捕获/可求值表达式）：</para>
/// <list type="bullet">
///   <item><description><c>r.Field == value</c> / <c>r.Field != value</c>（含 <c>== null</c> / <c>!= null</c>，分别映射为 Missing / Exists）</description></item>
///   <item><description><c>r.Field &gt; value</c> / <c>&gt;=</c> / <c>&lt;</c> / <c>&lt;=</c>（映射为 Range）</description></item>
///   <item><description><c>a &amp;&amp; b</c> → <see cref="Filter.And"/>；<c>a || b</c> → <see cref="Filter.Or"/></description></item>
///   <item><description><c>!a</c> → <see cref="Filter.Not"/></description></item>
/// </list>
/// <para>当前不支持：方法调用（如 <c>Contains</c>）、字符串拼接、向量字段或主键字段引用、字段两侧比较。</para>
/// </remarks>
[RequiresUnreferencedCode("LINQ Filter 翻译依赖反射访问 TRecord 属性。")]
[RequiresDynamicCode("LINQ Filter 翻译会编译子表达式以求值常量。")]
internal static class LinqFilterTranslator
{
    /// <summary>
    /// 翻译入口。
    /// </summary>
    /// <typeparam name="TKey">主键类型。</typeparam>
    /// <typeparam name="TRecord">记录类型。</typeparam>
    /// <param name="expression">用户提供的过滤 lambda。</param>
    /// <param name="mapper">用于把 CLR 属性名解析为 payload 存储字段名的映射器。</param>
    /// <returns>等价的 DotVector <see cref="Filter"/>。</returns>
    public static Filter Translate<TKey, TRecord>(
        Expression<Func<TRecord, bool>> expression,
        DotVectorRecordMapper<TKey, TRecord> mapper)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(mapper);
        if (expression.Parameters.Count != 1)
        {
            throw new NotSupportedException(
                "DotVector LINQ Filter 翻译仅支持单参数 lambda（即 Expression<Func<TRecord, bool>>）。");
        }

        var parameter = expression.Parameters[0];
        return Visit(expression.Body, parameter, mapper);
    }

    private static Filter Visit<TKey, TRecord>(
        Expression node,
        ParameterExpression parameter,
        DotVectorRecordMapper<TKey, TRecord> mapper)
        where TKey : notnull
        where TRecord : class
    {
        // 去掉显式 / 隐式装箱、类型转换包装。
        node = StripConvert(node);

        switch (node)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso } and1:
                return Filter.And(
                    Visit(and1.Left, parameter, mapper),
                    Visit(and1.Right, parameter, mapper));

            case BinaryExpression { NodeType: ExpressionType.OrElse } or1:
                return Filter.Or(
                    Visit(or1.Left, parameter, mapper),
                    Visit(or1.Right, parameter, mapper));

            case UnaryExpression { NodeType: ExpressionType.Not } notExpr:
                return Filter.Not(Visit(notExpr.Operand, parameter, mapper));

            case BinaryExpression bin when IsComparison(bin.NodeType):
                return TranslateComparison(bin, parameter, mapper);

            // 直接 bool 属性：r.Flag → Eq(field, true)
            case MemberExpression m when m.Type == typeof(bool) && IsParameterMember(m, parameter):
                {
                    var field = ResolveField(m, parameter, mapper);
                    return Filter.Eq(field, true);
                }

            // 常量 true/false（罕见但合法）。
            case ConstantExpression { Value: bool b }:
                return b ? AlwaysTrue() : AlwaysFalse();

            default:
                throw new NotSupportedException(
                    $"DotVector LINQ Filter 翻译尚不支持表达式节点：{node.NodeType} ({node})。");
        }
    }

    private static Filter TranslateComparison<TKey, TRecord>(
        BinaryExpression bin,
        ParameterExpression parameter,
        DotVectorRecordMapper<TKey, TRecord> mapper)
        where TKey : notnull
        where TRecord : class
    {
        var left = StripConvert(bin.Left);
        var right = StripConvert(bin.Right);

        // 规范化：保证 MemberExpression 在左侧。
        ExpressionType op = bin.NodeType;
        MemberExpression? memberExpr;
        Expression valueExpr;

        if (left is MemberExpression lm && IsParameterMember(lm, parameter))
        {
            memberExpr = lm;
            valueExpr = right;
        }
        else if (right is MemberExpression rm && IsParameterMember(rm, parameter))
        {
            memberExpr = rm;
            valueExpr = left;
            op = FlipComparison(op);
        }
        else
        {
            throw new NotSupportedException(
                $"DotVector LINQ Filter 翻译要求比较的一侧是参数成员访问（r.Field）。表达式：{bin}");
        }

        var field = ResolveField(memberExpr, parameter, mapper);
        var value = EvaluateConstant(valueExpr);

        return op switch
        {
            ExpressionType.Equal => value is null ? Filter.Missing(field) : Filter.Eq(field, value),
            ExpressionType.NotEqual => value is null ? Filter.Exists(field) : Filter.Ne(field, value),
            ExpressionType.GreaterThan => Filter.Range(field, AsComparable(value), null, minInclusive: false),
            ExpressionType.GreaterThanOrEqual => Filter.Range(field, AsComparable(value), null),
            ExpressionType.LessThan => Filter.Range(field, null, AsComparable(value), maxInclusive: false),
            ExpressionType.LessThanOrEqual => Filter.Range(field, null, AsComparable(value)),
            _ => throw new NotSupportedException(
                $"DotVector LINQ Filter 翻译尚不支持比较运算符：{op}。"),
        };
    }

    private static string ResolveField<TKey, TRecord>(
        MemberExpression member,
        ParameterExpression parameter,
        DotVectorRecordMapper<TKey, TRecord> mapper)
        where TKey : notnull
        where TRecord : class
    {
        if (member.Expression != parameter)
        {
            throw new NotSupportedException(
                $"DotVector LINQ Filter 翻译仅支持直接访问 lambda 参数的属性（r.Field）。表达式：{member}");
        }

        var name = member.Member.Name;
        if (string.Equals(name, mapper.KeyPropertyName, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"DotVector LINQ Filter 翻译不支持对主键 {name} 过滤；请改用 GetAsync(key)。");
        }
        if (string.Equals(name, mapper.VectorPropertyName, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"DotVector LINQ Filter 翻译不支持对向量字段 {name} 过滤。");
        }
        if (!mapper.TryGetPayloadFieldName(name, out var storage))
        {
            throw new NotSupportedException(
                $"属性 {typeof(TRecord).Name}.{name} 未标注 [VectorStoreData]，无法用于 Filter。");
        }
        return storage;
    }

    private static object? EvaluateConstant(Expression expr)
    {
        expr = StripConvert(expr);
        if (expr is ConstantExpression c)
        {
            return c.Value;
        }
        // 闭包捕获 / 静态字段 / 复杂表达式：编译求值。
        var lambda = Expression.Lambda(Expression.Convert(expr, typeof(object)));
        var compiled = (Func<object?>)lambda.Compile();
        return compiled();
    }

    private static IComparable? AsComparable(object? value)
    {
        if (value is null)
        {
            throw new NotSupportedException("Range 比较的右侧值不能为 null。");
        }
        if (value is IComparable cmp)
        {
            return cmp;
        }
        throw new NotSupportedException(
            $"Range 比较要求右侧值实现 IComparable，得到 {value.GetType().Name}。");
    }

    private static Expression StripConvert(Expression e)
    {
        while (e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
        {
            e = u.Operand;
        }
        return e;
    }

    private static bool IsParameterMember(MemberExpression m, ParameterExpression p)
        => m.Expression == p;

    private static bool IsComparison(ExpressionType t) => t is
        ExpressionType.Equal or ExpressionType.NotEqual
        or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
        or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static ExpressionType FlipComparison(ExpressionType op) => op switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => op,
    };

    // 占位用：true / false 常量表达式。
    private static Filter AlwaysTrue() => new TrueFilter();
    private static Filter AlwaysFalse() => Filter.Not(new TrueFilter());

    private sealed class TrueFilter : Filter
    {
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload) => true;
    }
}
