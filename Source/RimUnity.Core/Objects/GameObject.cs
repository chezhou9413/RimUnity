using System;
using System.Collections.Generic;

namespace RimUnity;

//维护对象层级、组件和激活状态。
public sealed class GameObject : Object
{
    internal readonly List<Component> Components = new();
    internal Func<GameObject> CloneFactory;
    internal object SceneOwner;
    private bool active = true;
    private int objectLayer;
    public Transform transform { get; }
    public bool IsAssetToken { get; }
    public bool activeSelf => active;
    public bool activeInHierarchy => !Destroyed && !Destroying && active && !IsAssetToken && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
    public int layer { get => objectLayer; set { if ((uint)value > 31) throw new ArgumentOutOfRangeException(nameof(value)); objectLayer = value; } }
    public GameObject(string name = "GameObject") : this(name, new Transform(), false) { }
    public GameObject(string name, params Type[] components) : this(name, CreateTransform(components), false)
    {
        using var transaction = Runtime.NativeRuntime.DeferLifecycle();
        foreach (Type type in components) if (!typeof(Transform).IsAssignableFrom(type)) AddComponent(type);
    }
    //构造时确定变换类型，让 RectTransform 与普通组件能使用同一个创建入口。
    private static Transform CreateTransform(Type[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        Type selected = typeof(Transform);
        foreach (Type type in components)
        {
            if (type == null) throw new ArgumentException("组件类型不能为空。", nameof(components));
            if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
                throw new ArgumentException("类型必须是可实例化组件：" + type.FullName, nameof(components));
            if (!typeof(Transform).IsAssignableFrom(type)) continue;
            if (selected.IsAssignableFrom(type)) selected = type;
            else if (!type.IsAssignableFrom(selected)) throw new ArgumentException("同一个对象不能包含不同的 Transform 类型。", nameof(components));
        }
        return (Transform)Activator.CreateInstance(selected, true);
    }
    //加载器明确传入 Transform 类型，避免先创建后替换导致引用失效。
    public GameObject(string name, Transform transform, bool isTemplate)
    {
        Runtime.NativeRuntime.AssertThread();
        ArgumentNullException.ThrowIfNull(transform);
        if (transform.Owner != null) throw new ArgumentException("Transform 已属于其他对象。", nameof(transform));
        this.name = name; this.transform = transform; IsAssetToken = isTemplate;
        transform.Owner = this; Components.Add(transform);
        Runtime.NativeRuntime.Register(this);
    }
    public static GameObject CreateAssetToken(string name) => new(name, new Transform(), true);
    public void RegisterCloneFactory(Func<GameObject> factory)
    {
        if (!IsAssetToken) throw new InvalidOperationException("只有资源模板可以注册克隆工厂。");
        CloneFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    public void SetActive(bool value)
    {
        Runtime.NativeRuntime.AssertThread();
        if (active == value) return;
        active = value;
        Runtime.NativeRuntime.RefreshTree(this);
        Runtime.NativeRuntime.NotifyChanged(transform);
    }
    public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
    public Component AddComponent(Type type)
    {
        Runtime.NativeRuntime.AssertThread();
        if (!this || Destroying) throw new InvalidOperationException("不能向已销毁的对象添加组件。");
        if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters || typeof(Transform).IsAssignableFrom(type))
            throw new ArgumentException("类型必须是可实例化的非 Transform 组件：" + type.FullName, nameof(type));
        using var transaction = Runtime.NativeRuntime.DeferLifecycle();
        return Add(type, new HashSet<Type>());
    }
    //循环依赖明确报错；非循环的已有必需组件直接复用。
    private Component Add(Type type, HashSet<Type> chain)
    {
        if (!chain.Add(type)) throw new InvalidOperationException("RequireComponent 存在循环依赖：" + type.FullName);
        foreach (RequireComponent requirement in type.GetCustomAttributes(typeof(RequireComponent), true))
            foreach (Type dependency in requirement.Types)
                if (GetComponent(dependency) == null) Add(dependency, chain);
        chain.Remove(type);
        if (Attribute.IsDefined(type, typeof(DisallowMultipleComponent), true) && GetComponent(type) != null)
            throw new InvalidOperationException("该组件不允许重复添加：" + type.FullName);
        if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract || typeof(Transform).IsAssignableFrom(type))
            throw new ArgumentException("无效的必需组件类型：" + type.FullName);
        var component = (Component)Activator.CreateInstance(type, true);
        component.Owner = this; Components.Add(component);
        try { if (component is MonoBehaviour mono) Runtime.NativeRuntime.Register(mono); }
        catch { Components.Remove(component); component.Owner = null; component.Destroyed = true; throw; }
        Runtime.NativeRuntime.NotifyChanged(transform);
        return component;
    }
    public T GetComponent<T>() { foreach (Component item in Components) if (item && item is T result) return result; return default; }
    public Component GetComponent(Type type) { foreach (Component item in Components) if (item && type.IsInstanceOfType(item)) return item; return null; }
    public bool TryGetComponent<T>(out T value) { value = GetComponent<T>(); return value is not null; }
    public T[] GetComponents<T>() { var list = new List<T>(); GetComponents(list); return list.ToArray(); }
    public Component[] GetComponents(Type type) { var list = new List<Component>(); foreach (Component item in Components) if (item && type.IsInstanceOfType(item)) list.Add(item); return list.ToArray(); }
    public void GetComponents<T>(List<T> results) { results.Clear(); foreach (Component item in Components) if (item && item is T value) results.Add(value); }
    public T GetComponentInChildren<T>(bool includeInactive = false)
    {
        foreach (T item in GetComponentsInChildren<T>(includeInactive)) return item;
        return default;
    }
    public T[] GetComponentsInChildren<T>(bool includeInactive = false) { var list = new List<T>(); GetComponentsInChildren(includeInactive, list); return list.ToArray(); }
    public void GetComponentsInChildren<T>(bool includeInactive, List<T> results)
    {
        results.Clear(); Collect(this, true);
        void Collect(GameObject go, bool root)
        {
            if (!go || (!root && !includeInactive && !go.activeInHierarchy)) return;
            foreach (Component item in go.Components) if (item && item is T value) results.Add(value);
            foreach (Transform child in go.transform.Children) Collect(child.gameObject, false);
        }
    }
    public T GetComponentInParent<T>(bool includeInactive = false)
    {
        for (Transform node = transform; node != null; node = node.parent)
            if (includeInactive || node == transform || node.gameObject.activeInHierarchy)
            { T value = node.GetComponent<T>(); if (value is not null) return value; }
        return default;
    }
    public static GameObject Find(string name) => Runtime.NativeRuntime.FindGameObject(name);
    //先停用整个子树，再销毁组件，回调期间树仍可读取。
    protected override void Release()
    {
        Runtime.NativeRuntime.RefreshTree(this);
        foreach (Transform child in transform.Children.ToArray()) DestroyImmediate(child.gameObject);
        foreach (Component component in Components.ToArray()) if (component is not Transform) DestroyImmediate(component);
        transform.Detach();
        transform.Destroyed = true; Components.Clear();
        Runtime.NativeRuntime.Unregister(this);
    }
    internal void Remove(Component component)
    {
        if (component is Transform) throw new InvalidOperationException("不能单独销毁 Transform，请销毁所属对象。");
        if (component is MonoBehaviour mono) Runtime.NativeRuntime.Unregister(mono);
        Components.Remove(component);
        Runtime.NativeRuntime.NotifyChanged(transform);
    }
}
