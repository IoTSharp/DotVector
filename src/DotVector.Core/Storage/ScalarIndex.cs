using DotVector.Query;

namespace DotVector.Storage;

/// <summary>
/// 进程内的标量 B-tree 风格 pre-filter 索引（M11）。
/// </summary>
/// <typeparam name="TKey">记录主键类型。</typeparam>
/// <remarks>
/// <para>
/// 维护每个 payload 字段到键集合的倒排：
/// <list type="bullet">
///   <item>数值字段（long / double）：使用 <see cref="SortedDictionary{TKey, TValue}"/>，
///         键为字段值（统一为 <see cref="double"/>），支持 Eq / Range 高效裁剪。</item>
///   <item>字符串字段：使用 <see cref="Dictionary{TKey, TValue}"/>，仅支持 Eq 裁剪。</item>
///   <item>布尔字段：使用两个 <see cref="HashSet{T}"/> 桶。</item>
/// </list>
/// </para>
/// <para>
/// 查询时对 <see cref="Filter"/> 做尽力分析，能够下推的子条件返回候选键集合；
/// 不能下推的形态（例如 <c>Or</c> 涉及未索引字段、Not 嵌套）返回 <see langword="null"/>，
/// 调用方回退到 post-filter。
/// </para>
/// <para>
/// 实现使用纯 BCL 集合，零第三方依赖；通过单个 <see cref="object"/> 锁保证并发一致性。
/// 适合中等规模（百万级）记录的内存索引；磁盘 B-tree 留待后续 milestone。
/// </para>
/// </remarks>
internal sealed class ScalarIndex<TKey>
    where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<string, FieldIndex> _fields;
    private readonly IEqualityComparer<TKey> _keyComparer;

    public ScalarIndex(IEqualityComparer<TKey>? keyComparer = null)
    {
        _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
        _fields = new Dictionary<string, FieldIndex>(StringComparer.Ordinal);
    }

    /// <summary>更新某条记录的 payload；旧/新均可为 <see langword="null"/>。</summary>
    public void Update(
        TKey key,
        IReadOnlyDictionary<string, object?>? oldPayload,
        IReadOnlyDictionary<string, object?>? newPayload)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            if (oldPayload is not null)
            {
                foreach (KeyValuePair<string, object?> kv in oldPayload)
                {
                    if (_fields.TryGetValue(kv.Key, out FieldIndex? fi))
                    {
                        fi.Remove(key, kv.Value);
                    }
                }
            }
            if (newPayload is not null)
            {
                foreach (KeyValuePair<string, object?> kv in newPayload)
                {
                    if (!_fields.TryGetValue(kv.Key, out FieldIndex? fi))
                    {
                        fi = new FieldIndex(_keyComparer);
                        _fields[kv.Key] = fi;
                    }
                    fi.Add(key, kv.Value);
                }
            }
        }
    }

    /// <summary>从索引中完全移除一条记录（删除时调用）。</summary>
    public void Remove(TKey key, IReadOnlyDictionary<string, object?>? payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (payload is null) return;
        lock (_lock)
        {
            foreach (KeyValuePair<string, object?> kv in payload)
            {
                if (_fields.TryGetValue(kv.Key, out FieldIndex? fi))
                {
                    fi.Remove(key, kv.Value);
                }
            }
        }
    }

    /// <summary>清空整个索引（重启重建前调用）。</summary>
    public void Clear()
    {
        lock (_lock) { _fields.Clear(); }
    }

    /// <summary>
    /// 尝试把 <paramref name="filter"/> 解析为候选键集合。
    /// </summary>
    /// <param name="filter">要下推的过滤表达式。</param>
    /// <param name="candidates">候选键集合（不为 <see langword="null"/> 即表示成功下推）。</param>
    /// <returns>能下推则返回 <see langword="true"/>；否则 <see langword="false"/> 且
    /// <paramref name="candidates"/> 为 <see langword="null"/>。</returns>
    public bool TryResolveCandidates(Filter filter, out HashSet<TKey>? candidates)
    {
        ArgumentNullException.ThrowIfNull(filter);
        lock (_lock)
        {
            candidates = ResolveCore(filter);
            return candidates is not null;
        }
    }

    private HashSet<TKey>? ResolveCore(Filter filter)
    {
        object? view = FilterIntrospectionAccessor.View(filter);
        switch (view)
        {
            case FilterIntrospection.EqualsView eq:
                return ResolveEq(eq.Field, eq.Value);
            case FilterIntrospection.RangeView range:
                return ResolveRange(range);
            case FilterIntrospection.AndView and:
                {
                    HashSet<TKey>? acc = null;
                    foreach (Filter sub in and.Children)
                    {
                        HashSet<TKey>? part = ResolveCore(sub);
                        if (part is null)
                        {
                            return null;
                        }
                        if (acc is null)
                        {
                            acc = new HashSet<TKey>(part, _keyComparer);
                        }
                        else
                        {
                            acc.IntersectWith(part);
                            if (acc.Count == 0) return acc;
                        }
                    }
                    return acc ?? new HashSet<TKey>(_keyComparer);
                }
            default:
                return null;
        }
    }

    private HashSet<TKey>? ResolveEq(string field, object? value)
    {
        if (!_fields.TryGetValue(field, out FieldIndex? fi)) return null;
        return fi.GetEq(value);
    }

    private HashSet<TKey>? ResolveRange(FilterIntrospection.RangeView range)
    {
        if (!_fields.TryGetValue(range.Field, out FieldIndex? fi)) return null;
        return fi.GetRange(range.Min, range.Max, range.MinInclusive, range.MaxInclusive);
    }

    private sealed class FieldIndex
    {
        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly SortedDictionary<double, HashSet<TKey>> _numeric;
        private readonly Dictionary<string, HashSet<TKey>> _strings;
        private readonly HashSet<TKey> _boolTrue;
        private readonly HashSet<TKey> _boolFalse;

        public FieldIndex(IEqualityComparer<TKey> keyComparer)
        {
            _keyComparer = keyComparer;
            _numeric = new SortedDictionary<double, HashSet<TKey>>();
            _strings = new Dictionary<string, HashSet<TKey>>(StringComparer.Ordinal);
            _boolTrue = new HashSet<TKey>(keyComparer);
            _boolFalse = new HashSet<TKey>(keyComparer);
        }

        public void Add(TKey key, object? value)
        {
            switch (value)
            {
                case null: return;
                case bool b: (b ? _boolTrue : _boolFalse).Add(key); return;
                case string s:
                    if (!_strings.TryGetValue(s, out HashSet<TKey>? bucket))
                    {
                        bucket = new HashSet<TKey>(_keyComparer);
                        _strings[s] = bucket;
                    }
                    bucket.Add(key);
                    return;
                default:
                    if (TryToDouble(value, out double d))
                    {
                        if (!_numeric.TryGetValue(d, out HashSet<TKey>? nb))
                        {
                            nb = new HashSet<TKey>(_keyComparer);
                            _numeric[d] = nb;
                        }
                        nb.Add(key);
                    }
                    return;
            }
        }

        public void Remove(TKey key, object? value)
        {
            switch (value)
            {
                case null: return;
                case bool b: (b ? _boolTrue : _boolFalse).Remove(key); return;
                case string s:
                    if (_strings.TryGetValue(s, out HashSet<TKey>? bucket))
                    {
                        bucket.Remove(key);
                        if (bucket.Count == 0) _strings.Remove(s);
                    }
                    return;
                default:
                    if (TryToDouble(value, out double d) && _numeric.TryGetValue(d, out HashSet<TKey>? nb))
                    {
                        nb.Remove(key);
                        if (nb.Count == 0) _numeric.Remove(d);
                    }
                    return;
            }
        }

        public HashSet<TKey>? GetEq(object? value)
        {
            HashSet<TKey> result = new(_keyComparer);
            switch (value)
            {
                case null:
                    return result; // 索引不跟踪缺失字段，保守返回空集 → 让 post-filter 处理
                case bool b:
                    result.UnionWith(b ? _boolTrue : _boolFalse);
                    return result;
                case string s:
                    if (_strings.TryGetValue(s, out HashSet<TKey>? sb)) result.UnionWith(sb);
                    return result;
                default:
                    if (TryToDouble(value, out double d) && _numeric.TryGetValue(d, out HashSet<TKey>? nb))
                    {
                        result.UnionWith(nb);
                    }
                    return result;
            }
        }

        public HashSet<TKey>? GetRange(IComparable? min, IComparable? max, bool minIncl, bool maxIncl)
        {
            // Range 仅在数值字段上下推
            double lo = double.NegativeInfinity;
            double hi = double.PositiveInfinity;
            bool loIncl = minIncl;
            bool hiIncl = maxIncl;
            if (min is not null)
            {
                if (!TryToDouble(min, out lo)) return null;
            }
            else { loIncl = true; }
            if (max is not null)
            {
                if (!TryToDouble(max, out hi)) return null;
            }
            else { hiIncl = true; }

            HashSet<TKey> result = new(_keyComparer);
            foreach (KeyValuePair<double, HashSet<TKey>> kv in _numeric)
            {
                double k = kv.Key;
                if (loIncl ? k < lo : k <= lo) continue;
                if (hiIncl ? k > hi : k >= hi) break;
                result.UnionWith(kv.Value);
            }
            return result;
        }

        private static bool TryToDouble(object value, out double result)
        {
            switch (value)
            {
                case byte u8: result = u8; return true;
                case sbyte i8: result = i8; return true;
                case short i16: result = i16; return true;
                case ushort u16: result = u16; return true;
                case int i32: result = i32; return true;
                case uint u32: result = u32; return true;
                case long i64: result = i64; return true;
                case float f32: result = f32; return true;
                case double f64: result = f64; return true;
                default: result = 0; return false;
            }
        }
    }
}
