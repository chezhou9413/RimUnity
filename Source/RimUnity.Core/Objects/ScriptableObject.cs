using System;

namespace RimUnity;

//非场景资源基类，不参与 Mono 生命周期和更新。
public class ScriptableObject : Object
{
    public static T CreateInstance<T>() where T : ScriptableObject => (T)CreateInstance(typeof(T));
    public static ScriptableObject CreateInstance(Type type)
    {
        if (!typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract) throw new ArgumentException("不是可实例化的资源类型。", nameof(type));
        return (ScriptableObject)Activator.CreateInstance(type, true);
    }
}
