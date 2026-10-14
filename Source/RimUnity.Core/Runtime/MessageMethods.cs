using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace RimUnity.Runtime;

//每种脚本只反射一次，帧更新调用缓存委托而不反复查找方法。
internal sealed class MessageMethods
{
    private static readonly Dictionary<Type, MessageMethods> Cache = new();
    internal readonly Action<MonoBehaviour> Awake, Enable, Disable, Destroy, Update, LateUpdate, FixedUpdate;
    internal readonly Func<MonoBehaviour, IEnumerator> Start;
    private MessageMethods(Type type)
    {
        Awake = Action(type, "Awake"); Enable = Action(type, "OnEnable"); Disable = Action(type, "OnDisable");
        Destroy = Action(type, "OnDestroy"); Update = Action(type, "Update"); LateUpdate = Action(type, "LateUpdate"); FixedUpdate = Action(type, "FixedUpdate");
        MethodInfo start = Find(type, "Start");
        if (start != null)
        {
            var parameter = Expression.Parameter(typeof(MonoBehaviour));
            var call = Expression.Call(Expression.Convert(parameter, type), start);
            Expression body = start.ReturnType == typeof(void) ? Expression.Block(call, Expression.Constant(null, typeof(IEnumerator))) :
                typeof(IEnumerator).IsAssignableFrom(start.ReturnType) ? Expression.Convert(call, typeof(IEnumerator)) : throw new InvalidOperationException(type.FullName + ".Start 必须返回 void 或 IEnumerator。");
            Start = Expression.Lambda<Func<MonoBehaviour, IEnumerator>>(body, parameter).Compile();
        }
    }
    internal static MessageMethods For(Type type)
    {
        if (!Cache.TryGetValue(type, out var result)) Cache.Add(type, result = new(type));
        return result;
    }
    private static MethodInfo Find(Type type, string name)
    {
        for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method != null) return method;
        }
        return null;
    }
    private static Action<MonoBehaviour> Action(Type type, string name)
    {
        MethodInfo method = Find(type, name);
        if (method == null) return null;
        if (method.ReturnType != typeof(void)) throw new InvalidOperationException(type.FullName + "." + name + " 必须返回 void。");
        var parameter = Expression.Parameter(typeof(MonoBehaviour));
        return Expression.Lambda<Action<MonoBehaviour>>(Expression.Call(Expression.Convert(parameter, type), method), parameter).Compile();
    }
}
