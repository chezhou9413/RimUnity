using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using RimUnity.Runtime;

namespace RimUnity;

//缓存开放目标的数值访问器，不捕获场景对象或材质实例。
internal sealed class AnimatedFloatAccessor
{
    private static readonly Dictionary<(Type, string), AnimatedFloatAccessor> cache = new();
    internal readonly Func<object, float> Read;
    internal readonly Action<object, float> Write;

    //动画绑定和对象生命周期都在主线程，同一属性只编译一次。
    internal static AnimatedFloatAccessor For(Type targetType, string propertyPath)
    {
        NativeRuntime.AssertThread();
        var key = (targetType, propertyPath);
        if (!cache.TryGetValue(key, out var accessor)) cache.Add(key, accessor = new(targetType, propertyPath));
        return accessor;
    }

    //嵌套结构体仍逐层写回，保持 Vector/Color 属性的原有赋值语义。
    private AnimatedFloatAccessor(Type targetType, string propertyPath)
    {
        var target = Expression.Parameter(typeof(object), "target");
        Expression root = Expression.Convert(target, targetType);
        var members = new List<MemberInfo>();
        Type type = targetType;
        foreach (string raw in propertyPath.Split('.'))
        {
            string name = AnimatedFloat.Alias(raw);
            MemberInfo member = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) as MemberInfo
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (member == null) throw new MissingMemberException(type.FullName, name);
            members.Add(member); type = MemberType(member);
        }
        Expression get = root;
        foreach (var member in members) get = Expression.MakeMemberAccess(get, member);
        if (type != typeof(float) && type != typeof(double) && type != typeof(int) && type != typeof(bool))
            throw new NotSupportedException("曲线目标不是数值：" + propertyPath);
        Read = Expression.Lambda<Func<object, float>>(type == typeof(bool)
            ? Expression.Condition(get, Expression.Constant(1f), Expression.Constant(0f)) : Expression.Convert(get, typeof(float)), target).Compile();
        var input = Expression.Parameter(typeof(float), "value");
        var variables = new List<ParameterExpression>();
        var body = new List<Expression>();
        Expression owner = root;
        for (int i = 0; i < members.Count - 1; i++)
        {
            var temp = Expression.Variable(MemberType(members[i]), "part" + i); variables.Add(temp);
            body.Add(Expression.Assign(temp, Expression.MakeMemberAccess(owner, members[i]))); owner = temp;
        }
        body.Add(Expression.Assign(Expression.MakeMemberAccess(owner, members[^1]), type == typeof(bool)
            ? Expression.GreaterThan(input, Expression.Constant(.5f)) : Expression.Convert(input, type)));
        for (int i = variables.Count - 1; i >= 0; i--)
            if (variables[i].Type.IsValueType)
                body.Add(Expression.Assign(Expression.MakeMemberAccess(i == 0 ? root : variables[i - 1], members[i]), variables[i]));
        body.Add(Expression.Empty());
        Write = Expression.Lambda<Action<object, float>>(Expression.Block(variables, body), target, input).Compile();
    }

    //字段和属性使用同一套结构体写回逻辑。
    private static Type MemberType(MemberInfo member) => member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
}
