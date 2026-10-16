using System;
using System.Threading;

namespace RimUnity;

//统一维护原生对象身份、销毁状态与模板克隆入口。
public class Object
{
    private static int nextId;
    private readonly int instanceId = Interlocked.Increment(ref nextId);
    private string objectName = "";
    internal bool Destroyed, Destroying;
    public virtual string name { get => objectName; set => objectName = value ?? ""; }
    public int GetInstanceID() => instanceId;
    public static implicit operator bool(Object value) => !ReferenceEquals(value, null) && !value.Destroyed;
    public static bool operator ==(Object a, Object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (ReferenceEquals(a, null)) return b.Destroyed;
        if (ReferenceEquals(b, null)) return a.Destroyed;
        return false;
    }
    public static bool operator !=(Object a, Object b) => !(a == b);
    public override bool Equals(object other) => other is Object value ? this == value : other is null && Destroyed;
    public override int GetHashCode() => instanceId;
    public override string ToString() => Destroyed ? "null (destroyed)" : name;

    //延迟销毁统一在帧尾处理，不在调用者遍历组件时改变集合。
    public static void Destroy(Object obj) => Destroy(obj, 0);
    public static void Destroy(Object obj, float delay)
    {
        if (obj == null) return;
        if (!float.IsFinite(delay) || delay < 0) throw new ArgumentOutOfRangeException(nameof(delay));
        Runtime.NativeRuntime.QueueDestroy(obj, delay);
    }
    public static void DestroyImmediate(Object obj)
    {
        if (obj == null || obj.Destroying) return;
        Runtime.NativeRuntime.AssertThread();
        if (obj is Transform) throw new InvalidOperationException("不能单独销毁 Transform，请销毁所属对象。");
        obj.Destroying = true;
        try { obj.Release(); }
        finally { obj.Destroyed = true; obj.Destroying = false; }
    }
    //资源类型覆盖此入口释放自己拥有的原生资源。
    protected virtual void Release() { }

    //明文模板注册工厂后才能克隆，禁止静默返回模板或浅拷贝引用。
    public static T Instantiate<T>(T original) where T : Object
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (original is GameObject go && go.CloneFactory != null)
        {
            using var transaction = Runtime.NativeRuntime.DeferLifecycle();
            return (T)(Object)go.CloneFactory();
        }
        if (original is Component component && component.gameObject.CloneFactory != null)
        {
            using var transaction = Runtime.NativeRuntime.DeferLifecycle();
            int index = Array.IndexOf(component.gameObject.GetComponents(component.GetType()), component);
            var copy = component.gameObject.CloneFactory();
            return (T)(Object)copy.GetComponents(component.GetType())[index];
        }
        throw new NotSupportedException("请从 PrefabLoader.Load 返回的根模板实例化；此对象未注册克隆工厂。");
    }
    public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays = false) where T : Object
    {
        using var transaction = Runtime.NativeRuntime.DeferLifecycle();
        T result = Instantiate(original);
        GameObject go = result is Component component ? component.gameObject : result as GameObject;
        if (go == null) throw new NotSupportedException("挂接父节点需要 GameObject 或组件模板。");
        go.transform.SetParent(parent, worldPositionStays);
        return result;
    }
    public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
    {
        using var transaction = Runtime.NativeRuntime.DeferLifecycle();
        T result = Instantiate(original);
        GameObject go = result is Component component ? component.gameObject : result as GameObject;
        if (go == null) throw new NotSupportedException("设置空间位置需要 GameObject 或组件模板。");
        go.transform.SetPositionAndRotation(position, rotation);
        return result;
    }
    public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object => Runtime.NativeRuntime.FindObjects<T>(false, sortMode);
    //父节点和世界变换在生命周期回调前一并设置。
    public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object
    {
        using var transaction = Runtime.NativeRuntime.DeferLifecycle();
        T result = Instantiate(original, parent, false);
        (result is Component component ? component.gameObject : (GameObject)(Object)result).transform.SetPositionAndRotation(position, rotation);
        return result;
    }
    public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode) where T : Object => Runtime.NativeRuntime.FindObjects<T>(inactive == FindObjectsInactive.Include, sortMode);
}

//指定对象查询的范围与排序方式。
public enum FindObjectsSortMode { None, InstanceID }
public enum FindObjectsInactive { Exclude, Include }
