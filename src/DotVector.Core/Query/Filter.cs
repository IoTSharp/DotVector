namespace DotVector.Query;

/// <summary>
/// 标量过滤条件的抽象基类（M6 — Payload Filter）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Filter"/> 描述一个布尔表达式，作用于向量记录的标量 payload
/// （<see cref="Model.VectorRecord{TKey}.Payload"/>）。
/// 在 <c>Collection.Search(query, topK, filter)</c> 中使用，对候选结果进行过滤。
/// </para>
/// <para>
/// 通过工厂方法构建表达式树：<see cref="Eq"/> / <see cref="Ne"/> / <see cref="Range"/> /
/// <see cref="Exists"/> / <see cref="Missing"/> / <see cref="And"/> / <see cref="Or"/> / <see cref="Not"/>。
/// </para>
/// <para>
/// 实现注意：所有匹配判定均不使用反射，AOT 友好。比较逻辑基于 <see cref="object.Equals(object, object)"/>
/// 与 <see cref="IComparable"/>；类型不可比时该字段视为不匹配。
/// </para>
/// </remarks>
public abstract class Filter
{
    /// <summary>
    /// 判定指定 payload 是否满足当前过滤条件。
    /// </summary>
    /// <param name="payload">向量记录的 payload；可为 <see langword="null"/>（表示无 payload）。</param>
    /// <returns>满足条件返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    public abstract bool Matches(IReadOnlyDictionary<string, object?>? payload);

    /// <summary>构造字段相等过滤：<c>payload[field] == value</c>。</summary>
    public static Filter Eq(string field, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return new FieldEqualsFilter(field, value);
    }

    /// <summary>构造字段不等过滤：<c>payload[field] != value</c>。</summary>
    public static Filter Ne(string field, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return new FieldNotEqualsFilter(field, value);
    }

    /// <summary>
    /// 构造范围过滤。<paramref name="min"/> 与 <paramref name="max"/> 至少一个不为 <see langword="null"/>。
    /// </summary>
    /// <param name="field">字段名。</param>
    /// <param name="min">下界，<see langword="null"/> 表示无下界。</param>
    /// <param name="max">上界，<see langword="null"/> 表示无上界。</param>
    /// <param name="minInclusive">下界是否包含。</param>
    /// <param name="maxInclusive">上界是否包含。</param>
    public static Filter Range(
        string field,
        IComparable? min = null,
        IComparable? max = null,
        bool minInclusive = true,
        bool maxInclusive = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        if (min is null && max is null)
        {
            throw new ArgumentException("Range 过滤的 min 与 max 不能同时为 null。", nameof(min));
        }
        return new FieldRangeFilter(field, min, max, minInclusive, maxInclusive);
    }

    /// <summary>构造字段存在过滤：payload 中存在指定 key 且值不为 <see langword="null"/>。</summary>
    public static Filter Exists(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return new FieldExistsFilter(field);
    }

    /// <summary>构造字段缺失过滤：payload 不存在指定 key 或对应值为 <see langword="null"/>。</summary>
    public static Filter Missing(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        return new FieldMissingFilter(field);
    }

    /// <summary>构造逻辑与（AND）：所有子条件均满足时为真。</summary>
    public static Filter And(params Filter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Length == 0)
        {
            throw new ArgumentException("And 过滤至少需要一个子条件。", nameof(filters));
        }
        return new AndFilter(filters);
    }

    /// <summary>构造逻辑或（OR）：任一子条件满足时为真。</summary>
    public static Filter Or(params Filter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Length == 0)
        {
            throw new ArgumentException("Or 过滤至少需要一个子条件。", nameof(filters));
        }
        return new OrFilter(filters);
    }

    /// <summary>构造逻辑非（NOT）。</summary>
    public static Filter Not(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new NotFilter(filter);
    }

    private static bool TryCompare(object lhs, IComparable rhs, out int result)
    {
        try
        {
            // IComparable.CompareTo 在类型不一致时通常抛出 ArgumentException。
            result = rhs.CompareTo(lhs);
            // rhs.CompareTo(lhs) 与 lhs.CompareTo(rhs) 符号相反，统一翻转。
            result = -result;
            return true;
        }
        catch (ArgumentException)
        {
            result = 0;
            return false;
        }
        catch (InvalidCastException)
        {
            result = 0;
            return false;
        }
    }

    private sealed class FieldEqualsFilter : Filter
    {
        private readonly string _field;
        private readonly object? _value;
        public FieldEqualsFilter(string field, object? value) { _field = field; _value = value; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            if (payload is null) return _value is null;
            if (!payload.TryGetValue(_field, out object? actual))
            {
                return _value is null;
            }
            return Equals(actual, _value);
        }
    }

    private sealed class FieldNotEqualsFilter : Filter
    {
        private readonly string _field;
        private readonly object? _value;
        public FieldNotEqualsFilter(string field, object? value) { _field = field; _value = value; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            if (payload is null) return _value is not null;
            if (!payload.TryGetValue(_field, out object? actual))
            {
                return _value is not null;
            }
            return !Equals(actual, _value);
        }
    }

    private sealed class FieldRangeFilter : Filter
    {
        private readonly string _field;
        private readonly IComparable? _min;
        private readonly IComparable? _max;
        private readonly bool _minInclusive;
        private readonly bool _maxInclusive;

        public FieldRangeFilter(string field, IComparable? min, IComparable? max, bool minInclusive, bool maxInclusive)
        {
            _field = field;
            _min = min;
            _max = max;
            _minInclusive = minInclusive;
            _maxInclusive = maxInclusive;
        }

        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            if (payload is null) return false;
            if (!payload.TryGetValue(_field, out object? actual) || actual is null) return false;

            if (_min is not null)
            {
                if (!TryCompare(actual, _min, out int cmp)) return false;
                if (_minInclusive ? cmp < 0 : cmp <= 0) return false;
            }
            if (_max is not null)
            {
                if (!TryCompare(actual, _max, out int cmp)) return false;
                if (_maxInclusive ? cmp > 0 : cmp >= 0) return false;
            }
            return true;
        }
    }

    private sealed class FieldExistsFilter : Filter
    {
        private readonly string _field;
        public FieldExistsFilter(string field) { _field = field; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
            => payload is not null
               && payload.TryGetValue(_field, out object? v)
               && v is not null;
    }

    private sealed class FieldMissingFilter : Filter
    {
        private readonly string _field;
        public FieldMissingFilter(string field) { _field = field; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            if (payload is null) return true;
            return !payload.TryGetValue(_field, out object? v) || v is null;
        }
    }

    private sealed class AndFilter : Filter
    {
        private readonly Filter[] _filters;
        public AndFilter(Filter[] filters) { _filters = filters; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                if (!_filters[i].Matches(payload)) return false;
            }
            return true;
        }
    }

    private sealed class OrFilter : Filter
    {
        private readonly Filter[] _filters;
        public OrFilter(Filter[] filters) { _filters = filters; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                if (_filters[i].Matches(payload)) return true;
            }
            return false;
        }
    }

    private sealed class NotFilter : Filter
    {
        private readonly Filter _inner;
        public NotFilter(Filter inner) { _inner = inner; }
        public override bool Matches(IReadOnlyDictionary<string, object?>? payload)
            => !_inner.Matches(payload);
    }
}
