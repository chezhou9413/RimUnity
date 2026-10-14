using System;
using RimUnity.Runtime;

namespace RimUnity;

//创建具有矩形变换的引擎对象，供程序化 UI 使用。
public static class Ugui
{
    //同时用于明文加载器，避免创建重复的 Transform。
    internal static GameObject Create(string name, bool rect, bool token)
    {
        var result = new GameObject(name, rect ? new RectTransform() : new Transform(), token);
        return result;
    }

    //添加组件会遵守引擎的普通动态创建生命周期。
    public static GameObject CreateObject(string name, params Type[] components)
    {
        using var creation = NativeRuntime.DeferLifecycle();
        GameObject result = Create(name, true, false);
        foreach (Type type in components) result.AddComponent(type);
        return result;
    }

    //查询当前组件所在对象及其父级上的组件。
    public static T GetComponentInParent<T>(this Component component) where T : Component
        => GetComponentInParent<T>(component.gameObject);

    //查找包括自身在内的最近活动父级。
    public static T GetComponentInParent<T>(this GameObject obj) where T : Component
    {
        for (Transform t = obj.transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeInHierarchy && t != obj.transform) continue;
            T value = t.GetComponent<T>();
            if (value != null) return value;
        }
        return null;
    }
}
