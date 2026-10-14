using System;
using System.Collections.Generic;
using System.Reflection;

namespace RimUnity;

//按脚本类型缓存粒子回调入口。
internal static class ParticleMessages
{
    private static readonly Dictionary<(Type, string, Type), MethodInfo> methods = new();

    //只向当前对象发送消息，用户回调异常保留原始调用堆栈并交由宿主日志处理。
    internal static void Send(GameObject target, string name, object argument = null)
    {
        Type argumentType = argument?.GetType();
        foreach (MonoBehaviour script in target.GetComponents<MonoBehaviour>())
        {
            var key = (script.GetType(), name, argumentType);
            if (!methods.TryGetValue(key, out var method))
            {
                for (Type type = key.Item1; type != null && method == null; type = type.BaseType)
                    foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (candidate.Name != name) continue;
                        var parameters = candidate.GetParameters();
                        if (argument == null ? parameters.Length == 0 : parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
                        { method = candidate; break; }
                    }
                methods.Add(key, method);
            }
            method?.Invoke(script, argument == null ? null : new[] { argument });
        }
    }
}
