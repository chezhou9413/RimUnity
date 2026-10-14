using System;
using System.Collections.Generic;
using System.Linq;

namespace RimUnity.Assets;

//显式映射已接通的引擎组件，业务脚本按完整类型名解析。
public static class TypeResolver
{
    private static readonly Dictionary<string, Type> Aliases = new(StringComparer.Ordinal);

    //为重命名的业务脚本登记导出类型名；加载前调用。
    public static void Register(string exportedName, Type runtimeType) => Aliases.Add(exportedName, runtimeType);

    //不按程序集版本绑定，允许业务脚本改引用后重新编译。
    internal static Type Resolve(string name)
    {
        if (Aliases.TryGetValue(name, out Type alias)) return alias;
        if (name.StartsWith("UnityEngine.", StringComparison.Ordinal))
            throw new TypeLoadException($"资源仍包含制作端类型 {name}；请用当前导出器重新导出，脚本字段使用 RimUnity 类型。");
        Type[] found = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).Where(t => t != null).ToArray();
        if (found.Length != 1) throw new TypeLoadException($"类型 {name} 匹配 {found.Length} 项；请重新编译对应脚本或通过 TypeResolver.Register 指定类型。");
        return found[0];
    }

    //导出器已经为受支持的内置组件写入桥接类型名。
    internal static Type Component(string name)
    {
        Type type = Resolve(name);
        if (!typeof(RimUnity.Component).IsAssignableFrom(type) || type.IsAbstract)
            throw new TypeLoadException($"{name} 不是可实例化组件。");
        return type;
    }
}
