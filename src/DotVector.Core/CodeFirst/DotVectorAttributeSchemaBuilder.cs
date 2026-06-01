using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using DotVector.Model;

namespace DotVector.CodeFirst;

internal static class DotVectorAttributeSchemaBuilder
{
    [RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    public static DotVectorEntitySchema<TEntity, TKey> Build<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
    {
        Type entityType = typeof(TEntity);
        PropertyInfo[] properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo keyProperty = FindKeyProperty(entityType, properties);
        if (keyProperty.PropertyType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"{entityType.FullName}.{keyProperty.Name} 的类型 {keyProperty.PropertyType.FullName} 与 TKey={typeof(TKey).FullName} 不一致。");
        }

        var vectorFields = new List<DotVectorVectorFieldMetadata>();
        var vectorGetters = new Dictionary<string, Func<TEntity, ReadOnlyMemory<float>>>(StringComparer.Ordinal);
        var payloadProperties = new List<PropertyInfo>();

        foreach (PropertyInfo property in properties)
        {
            DotVectorVectorAttribute? vectorAttribute = property.GetCustomAttribute<DotVectorVectorAttribute>();
            if (vectorAttribute is not null)
            {
                ValidateVectorProperty(property);
                string vectorName = string.IsNullOrWhiteSpace(vectorAttribute.Name)
                    ? property.Name
                    : vectorAttribute.Name!;
                DotVectorIndexAttribute? indexAttribute = property.GetCustomAttribute<DotVectorIndexAttribute>();
                DotVectorVectorFieldMetadata metadata = new(
                    vectorName,
                    vectorAttribute.CollectionName,
                    vectorAttribute.Dimensions,
                    vectorAttribute.Metric,
                    indexAttribute?.IndexKind ?? IndexKind.Flat,
                    indexAttribute?.ToOptions() ?? new DotVectorIndexOptions());
                vectorFields.Add(metadata);
                vectorGetters.Add(vectorName, CompileVectorGetter<TEntity>(property));
                continue;
            }

            if (property != keyProperty && IsPayloadProperty(property))
            {
                payloadProperties.Add(property);
            }
        }

        if (vectorFields.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entityType.FullName} 必须至少包含一个标注 [DotVectorVector] 的属性。");
        }

        var accessors = new DotVectorEntityAccessors<TEntity, TKey>(
            CompileKeyGetter<TEntity, TKey>(keyProperty),
            vectorGetters,
            CompilePayloadGetter<TEntity>(payloadProperties));

        return DotVectorEntitySchema<TEntity, TKey>.Create(
            accessors,
            vectorFields,
            setName: null);
    }

    [RequiresUnreferencedCode("Code-First Attribute 发现会反射扫描实体属性。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    public static Type FindKeyType(Type entityType)
    {
        PropertyInfo[] properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return FindKeyProperty(entityType, properties).PropertyType;
    }

    private static PropertyInfo FindKeyProperty(Type entityType, PropertyInfo[] properties)
    {
        PropertyInfo? key = null;
        foreach (PropertyInfo property in properties)
        {
            if (property.GetCustomAttribute<DotVectorKeyAttribute>() is null)
            {
                continue;
            }

            if (key is not null)
            {
                throw new InvalidOperationException(
                    $"{entityType.FullName} 声明了多个 [DotVectorKey] 属性。");
            }

            key = property;
        }

        if (key is null)
        {
            throw new InvalidOperationException(
                $"{entityType.FullName} 必须包含一个标注 [DotVectorKey] 的属性。");
        }

        Type keyType = key.PropertyType;
        if (keyType != typeof(int)
            && keyType != typeof(long)
            && keyType != typeof(Guid)
            && keyType != typeof(string))
        {
            throw new NotSupportedException(
                $"{entityType.FullName}.{key.Name} 的主键类型 {keyType.FullName} 不受支持。仅支持 int / long / Guid / string。");
        }

        return key;
    }

    private static bool IsPayloadProperty(PropertyInfo property)
    {
        Type type = property.PropertyType;
        if (type == typeof(string) || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(float) || type == typeof(double))
        {
            return true;
        }

        Type? underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null
            && (underlying == typeof(bool) || underlying == typeof(byte) || underlying == typeof(sbyte)
                || underlying == typeof(short) || underlying == typeof(ushort) || underlying == typeof(int)
                || underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(float)
                || underlying == typeof(double));
    }

    private static void ValidateVectorProperty(PropertyInfo property)
    {
        Type type = property.PropertyType;
        if (type != typeof(float[])
            && type != typeof(ReadOnlyMemory<float>)
            && type != typeof(Memory<float>))
        {
            throw new NotSupportedException(
                $"{property.DeclaringType?.FullName}.{property.Name} 的向量类型 {type.FullName} 不受支持。仅支持 float[] / Memory<float> / ReadOnlyMemory<float>。");
        }
    }

    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    private static Func<TEntity, TKey> CompileKeyGetter<TEntity, TKey>(PropertyInfo property)
        where TEntity : class
        where TKey : notnull
    {
        ParameterExpression entity = Expression.Parameter(typeof(TEntity), "entity");
        MemberExpression propertyAccess = Expression.Property(entity, property);
        return Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, entity).Compile();
    }

    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    private static Func<TEntity, ReadOnlyMemory<float>> CompileVectorGetter<TEntity>(PropertyInfo property)
        where TEntity : class
    {
        ParameterExpression entity = Expression.Parameter(typeof(TEntity), "entity");
        MemberExpression propertyAccess = Expression.Property(entity, property);
        Expression body;
        if (property.PropertyType == typeof(float[]))
        {
            body = Expression.New(
                typeof(ReadOnlyMemory<float>).GetConstructor(new[] { typeof(float[]) })
                    ?? throw new InvalidOperationException("无法找到 ReadOnlyMemory<float>(float[]) 构造函数。"),
                propertyAccess);
        }
        else if (property.PropertyType == typeof(Memory<float>))
        {
            body = Expression.Convert(propertyAccess, typeof(ReadOnlyMemory<float>));
        }
        else
        {
            body = propertyAccess;
        }

        return Expression.Lambda<Func<TEntity, ReadOnlyMemory<float>>>(body, entity).Compile();
    }

    [RequiresDynamicCode("Code-First Attribute 发现会编译表达式访问器。Native AOT 下请使用显式 DotVectorEntitySchema 注册。")]
    private static Func<TEntity, IReadOnlyDictionary<string, object?>?> CompilePayloadGetter<TEntity>(
        IReadOnlyList<PropertyInfo> payloadProperties)
        where TEntity : class
    {
        if (payloadProperties.Count == 0)
        {
            return static _ => null;
        }

        Func<TEntity, object?>[] getters = new Func<TEntity, object?>[payloadProperties.Count];
        string[] names = new string[payloadProperties.Count];
        for (int i = 0; i < payloadProperties.Count; i++)
        {
            PropertyInfo property = payloadProperties[i];
            ParameterExpression entity = Expression.Parameter(typeof(TEntity), "entity");
            UnaryExpression convert = Expression.Convert(Expression.Property(entity, property), typeof(object));
            getters[i] = Expression.Lambda<Func<TEntity, object?>>(convert, entity).Compile();
            names[i] = property.Name;
        }

        return entity =>
        {
            Dictionary<string, object?>? payload = null;
            for (int i = 0; i < getters.Length; i++)
            {
                object? value = getters[i](entity);
                if (value is null)
                {
                    continue;
                }

                payload ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                payload[names[i]] = value;
            }
            return payload;
        };
    }
}
