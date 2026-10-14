using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//加载时生成强类型事件委托，点击和动画调用不使用 MethodInfo.Invoke。
internal static class EventBinder
{
    //支持零到四个动态参数以及 Unity 的常用常量参数模式。
    internal static void Bind(object ev, IEnumerable<EventCallData> calls, ValueReader reader,
        IReadOnlyDictionary<string, RimUnity.Object> objects)
    {
        MethodInfo add = ev.GetType().GetMethod("AddListener");
        Type delegateType = add.GetParameters()[0].ParameterType;
        ParameterExpression[] args = delegateType.GetMethod("Invoke").GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        foreach (EventCallData call in calls)
        {
            if (call.state == 0) continue;
            if (!objects.TryGetValue(call.target, out var target)) throw new InvalidOperationException($"事件目标不存在：{call.target}");
            Expression[] passed = call.mode switch
            {
                0 => args,
                1 => Array.Empty<Expression>(),
                2 => new[] { Expression.Constant(reader.Read(call.argument, TypeResolver.Resolve(call.argumentType)), TypeResolver.Resolve(call.argumentType)) },
                3 => new[] { Expression.Constant(reader.Read(call.argument, typeof(int))) },
                4 => new[] { Expression.Constant(reader.Read(call.argument, typeof(float))) },
                5 => new[] { Expression.Constant(reader.Read(call.argument, typeof(string)), typeof(string)) },
                6 => new[] { Expression.Constant(reader.Read(call.argument, typeof(bool))) },
                _ => throw new NotSupportedException($"事件参数模式 {call.mode} 不受支持")
            };
            MethodInfo method = target.GetType().GetMethod(call.method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, passed.Select(p => p.Type).ToArray(), null) ?? throw new MissingMethodException(target.GetType().FullName, call.method);
            if (method.ReturnType != typeof(void)) throw new NotSupportedException($"UnityEvent 方法必须返回 void：{method}");
            Delegate action = Expression.Lambda(delegateType, Expression.Call(Expression.Constant(target), method, passed), args).Compile();
            add.Invoke(ev, new object[] { action });
        }
    }
}
