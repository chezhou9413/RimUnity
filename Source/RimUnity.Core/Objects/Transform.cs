using System;
using System.Collections;
using System.Collections.Generic;

namespace RimUnity;

//维护变换层级并缓存局部与世界矩阵。
public class Transform : Component, IEnumerable<Transform>
{
    internal readonly List<Transform> Children = new();
    private Transform parentTransform;
    private Vector3 localPos, localSize = Vector3.one;
    private Quaternion localRot = Quaternion.identity;
    private Matrix4x4 world;
    private bool dirty = true;
    public bool hasChanged { get; set; } = true;
    public Transform parent { get => parentTransform; set => SetParent(value); }
    public Transform root { get { Transform value = this; while (value.parent != null) value = value.parent; return value; } }
    public int childCount => Children.Count;
    public Vector3 localPosition { get => localPos; set { Runtime.NativeRuntime.AssertThread(); if (localPos.Equals(value)) return; localPos = value; Invalidate(); } }
    public Quaternion localRotation { get => localRot; set { Runtime.NativeRuntime.AssertThread(); var q = value.normalized; if (localRot.Equals(q)) return; localRot = q; Invalidate(); } }
    public Vector3 localScale { get => localSize; set { Runtime.NativeRuntime.AssertThread(); if (localSize.Equals(value)) return; localSize = value; Invalidate(true); } }
    public Vector3 position { get => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero); set => localPosition = parent == null ? value : parent.InverseTransformPoint(value); }
    public Quaternion rotation { get => parent == null ? localRot : parent.rotation * localRot; set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; }
    public Vector3 localEulerAngles { get => localRotation.eulerAngles; set => localRotation = Quaternion.Euler(value); }
    public Vector3 eulerAngles { get => rotation.eulerAngles; set => rotation = Quaternion.Euler(value); }
    public Vector3 lossyScale => localToWorldMatrix.lossyScale;
    public Vector3 forward { get => rotation * Vector3.forward; set => rotation = Quaternion.LookRotation(value); }
    public Vector3 up { get => rotation * Vector3.up; set => rotation = Quaternion.FromToRotation(Vector3.up, value); }
    public Vector3 right { get => rotation * Vector3.right; set => rotation = Quaternion.FromToRotation(Vector3.right, value); }
    public Matrix4x4 localToWorldMatrix
    {
        get
        {
            if (dirty) { world = Matrix4x4.TRS(localPos, localRot, localSize); if (parent != null) world = parent.localToWorldMatrix * world; dirty = false; }
            return world;
        }
    }
    public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
    public Transform GetChild(int index) => Children[index];
    public Transform Find(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int slash = path.IndexOf('/'); string head = slash < 0 ? path : path[..slash];
        foreach (Transform child in Children)
            if (child.name == head) return slash < 0 ? child : child.Find(path[(slash + 1)..]);
        return null;
    }
    public bool IsChildOf(Transform potentialParent) { for (Transform node = this; node != null; node = node.parent) if (ReferenceEquals(node, potentialParent)) return true; return false; }
    //换父节点前保存世界姿态，并拒绝循环和场景／模板之间的混合挂接。
    public void SetParent(Transform value, bool worldPositionStays = true)
    {
        Runtime.NativeRuntime.AssertThread();
        if (!ReferenceEquals(value, null) && (!value || value.gameObject.Destroying))
            throw new ArgumentException("不能挂到已销毁的父节点。", nameof(value));
        if (ReferenceEquals(parentTransform, value)) return;
        if (value != null && value.IsChildOf(this)) throw new InvalidOperationException("Transform 层级不能包含循环。");
        if (value != null && value.gameObject.IsAssetToken != gameObject.IsAssetToken) throw new InvalidOperationException("资源模板不能挂到场景对象，场景对象也不能挂到资源模板。");
        Matrix4x4 saved = localToWorldMatrix; Quaternion savedRotation = rotation;
        Runtime.NativeRuntime.NotifyChanged(this);
        parentTransform?.Children.Remove(this); parentTransform = value; value?.Children.Add(this);
        if (worldPositionStays)
        {
            Matrix4x4 relative = value == null ? saved : value.worldToLocalMatrix * saved;
            localPos = relative.MultiplyPoint3x4(Vector3.zero);
            localRot = value == null ? savedRotation : Quaternion.Inverse(value.rotation) * savedRotation;
            Matrix4x4 unrotated = Matrix4x4.Rotate(Quaternion.Inverse(localRot)) * relative;
            localSize = new(unrotated.m00, unrotated.m11, unrotated.m22);
        }
        Invalidate(true); Runtime.NativeRuntime.RefreshTree(gameObject);
    }
    public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    public void SetLocalPositionAndRotation(Vector3 p, Quaternion q) { localPosition = p; localRotation = q; }
    public int GetSiblingIndex() => parent == null ? 0 : parent.Children.IndexOf(this);
    public void SetSiblingIndex(int index)
    {
        Runtime.NativeRuntime.AssertThread();
        if (parent == null) return;
        parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this);
        Runtime.NativeRuntime.NotifyChanged(this);
    }
    public void SetAsFirstSibling() => SetSiblingIndex(0);
    public void SetAsLastSibling() => SetSiblingIndex(int.MaxValue);
    public Vector3 TransformPoint(Vector3 value) => localToWorldMatrix.MultiplyPoint3x4(value);
    public Vector3 TransformPoint(float x, float y, float z) => TransformPoint(new(x, y, z));
    public Vector3 InverseTransformPoint(Vector3 value) => worldToLocalMatrix.MultiplyPoint3x4(value);
    public Vector3 TransformVector(Vector3 value) => localToWorldMatrix.MultiplyVector(value);
    public Vector3 InverseTransformVector(Vector3 value) => worldToLocalMatrix.MultiplyVector(value);
    public Vector3 TransformDirection(Vector3 value) => rotation * value;
    public Vector3 InverseTransformDirection(Vector3 value) => Quaternion.Inverse(rotation) * value;
    public void Translate(Vector3 delta, Space relativeTo = Space.Self) => position += relativeTo == Space.Self ? TransformDirection(delta) : delta;
    public void Rotate(Vector3 angles, Space relativeTo = Space.Self) { if (relativeTo == Space.Self) localRotation *= Quaternion.Euler(angles); else rotation = Quaternion.Euler(angles) * rotation; }
    public void LookAt(Vector3 target) => rotation = Quaternion.LookRotation(target - position);
    public void LookAt(Transform target) => LookAt(target.position);
    //脏标记沿子树传播，矩阵只在下次读取时计算。
    private void Invalidate(bool layout = false)
    {
        MarkDirty();
        if (layout) Runtime.NativeRuntime.NotifyChanged(this);
    }
    private void MarkDirty() { dirty = true; hasChanged = true; foreach (Transform child in Children) child.MarkDirty(); }
    internal void Detach() { Runtime.NativeRuntime.NotifyChanged(this); parentTransform?.Children.Remove(this); parentTransform = null; }
    public IEnumerator<Transform> GetEnumerator() => Children.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

//指定变换操作采用自身空间还是世界空间。
public enum Space { World, Self }
