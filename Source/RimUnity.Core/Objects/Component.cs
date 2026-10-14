using System;
using System.Collections.Generic;

namespace RimUnity;

//组件只持有所属对象，查询与名称共享对象树的真实状态。
public class Component : Object
{
    internal GameObject Owner;
    public GameObject gameObject => Owner ?? throw new InvalidOperationException("组件尚未通过 AddComponent 挂接。");
    public Transform transform => gameObject.transform;
    public override string name { get => Owner is null ? base.name : Owner.name; set { if (Owner is null) base.name = value; else Owner.name = value; } }
    public T GetComponent<T>() => gameObject.GetComponent<T>();
    public Component GetComponent(Type type) => gameObject.GetComponent(type);
    public T[] GetComponents<T>() => gameObject.GetComponents<T>();
    public void GetComponents<T>(List<T> results) => gameObject.GetComponents(results);
    public Component[] GetComponents(Type type) => gameObject.GetComponents(type);
    public bool TryGetComponent<T>(out T value) => gameObject.TryGetComponent(out value);
    public T GetComponentInChildren<T>(bool includeInactive = false) => gameObject.GetComponentInChildren<T>(includeInactive);
    public T[] GetComponentsInChildren<T>(bool includeInactive = false) => gameObject.GetComponentsInChildren<T>(includeInactive);
    public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) => gameObject.GetComponentsInChildren(includeInactive, results);
    public T GetComponentInParent<T>(bool includeInactive = false) => gameObject.GetComponentInParent<T>(includeInactive);

    //属性真正改变时通知所属对象，同值写入不使布局缓存失效。
    protected bool SetProperty<T>(ref T field, T value)
    {
        Runtime.NativeRuntime.AssertThread();
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        NotifyPropertyChanged();
        return true;
    }

    //构造阶段尚未挂接对象，后续首次布局会读取初始属性。
    protected void NotifyPropertyChanged()
    {
        if (Owner != null) Runtime.NativeRuntime.NotifyChanged(Owner.transform);
    }
    //销毁组件不会连带销毁同对象上的其他组件。
    protected override void Release() => Owner?.Remove(this);
}
