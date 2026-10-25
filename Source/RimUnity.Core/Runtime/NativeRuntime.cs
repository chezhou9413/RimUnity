using System;
using System.Collections.Generic;
using System.Threading;

namespace RimUnity.Runtime;

//主线程对象注册和生命周期入口。
public static partial class NativeRuntime
{
    private static int mainThread;
    private static readonly List<GameObject> Objects = new();
    private static readonly List<MonoBehaviour> Behaviours = new();
    private static readonly Queue<MonoBehaviour> Pending = new();
    private static readonly HashSet<MonoBehaviour> PendingSet = new();
    private static int deferDepth;
    private static bool draining;
    public static event Action<Transform> HierarchyChanged;
    public static event Action<Exception, Object> ExceptionRaised;
    public static Func<object> SceneOwnerProvider { get; set; }

    //由宿主在主线程显式初始化，类型首次加载不决定对象所属线程。
    public static void InitializeOnCurrentThread()
    {
        int current = Environment.CurrentManagedThreadId;
        int previous = Interlocked.CompareExchange(ref mainThread, current, 0);
        if (previous != 0 && previous != current)
            throw new InvalidOperationException($"RimUnity 已绑定线程 {previous}，不能改绑到线程 {current}。");
    }

    //所有对象和 GPU 资源的所有权变更都限制在宿主主线程。
    public static void AssertThread()
    {
        int expected = Volatile.Read(ref mainThread);
        if (expected == 0) throw new InvalidOperationException("RimUnity 运行时尚未在宿主主线程初始化。");
        int current = Environment.CurrentManagedThreadId;
        if (current != expected) throw new InvalidOperationException($"RimUnity 对象操作必须在主线程 {expected} 执行，当前线程为 {current}。");
    }
    public static IDisposable DeferLifecycle() { AssertThread(); deferDepth++; return new CreationScope(); }
    private sealed class CreationScope : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; if (--deferDepth == 0) Drain(); }
    }
    internal static void Register(GameObject go)
    {
        go.SceneOwner = go.IsAssetToken ? null : SceneOwnerProvider?.Invoke();
        Objects.Add(go);
    }
    internal static void Register(MonoBehaviour mono)
    {
        mono.Messages = MessageMethods.For(mono.GetType());
        if (mono.gameObject.IsAssetToken) return;
        Behaviours.Add(mono); Refresh(mono);
    }
    internal static void Unregister(GameObject go) { Objects.Remove(go); go.SceneOwner = null; }
    internal static void Unregister(MonoBehaviour mono)
    {
        Refresh(mono);
        if (mono.EnableSent) { mono.EnableSent = false; Invoke(mono, mono.Messages.Disable); }
        CoroutineScheduler.StopAll(mono);
        if (mono.Awakened) Invoke(mono, mono.Messages.Destroy);
        Behaviours.Remove(mono);
    }
    //先记录整棵子树再执行用户回调，避免回调改变正在遍历的集合。
    internal static void RefreshTree(GameObject go)
    {
        using var transaction = DeferLifecycle();
        Collect(go);
        void Collect(GameObject node)
        {
            foreach (Component component in node.Components)
                if (component is MonoBehaviour mono) Refresh(mono);
            foreach (Transform child in node.transform.Children) Collect(child.gameObject);
        }
    }
    internal static void Refresh(MonoBehaviour mono)
    {
        if (PendingSet.Add(mono)) Pending.Enqueue(mono);
        if (deferDepth == 0 && !draining) Drain();
    }
    private static void Drain()
    {
        if (draining) return;
        draining = true;
        try
        {
            while (Pending.TryDequeue(out var mono))
            {
                PendingSet.Remove(mono);
                if (!mono || mono.Owner == null) continue;
                bool alive = !mono.Destroying && mono.gameObject.activeInHierarchy;
                if (alive && !mono.Awakened)
                {
                    mono.Awakened = true; Invoke(mono, mono.Messages.Awake);
                    alive = mono && !mono.Destroying && mono.gameObject.activeInHierarchy;
                }
                bool hierarchyActive = alive;
                bool hierarchyDeactivated = mono.HierarchyActive && !hierarchyActive;
                mono.HierarchyActive = hierarchyActive;
                if (hierarchyDeactivated && mono && !mono.Destroying && mono.Owner != null && !mono.Owner.Destroying)
                {
                    Invoke(mono, static value => value.OnHierarchyDeactivated());
                    alive = mono && !mono.Destroying && mono.gameObject.activeInHierarchy;
                }
                bool enabled = alive && mono.enabled;
                if (enabled != mono.EnableSent)
                {
                    mono.EnableSent = enabled;
                    Invoke(mono, enabled ? mono.Messages.Enable : mono.Messages.Disable);
                }
                if (!alive) CoroutineScheduler.StopAll(mono);
            }
        }
        finally { draining = false; }
    }
    internal static void NotifyChanged(Transform transform) => HierarchyChanged?.Invoke(transform);
    internal static void Invoke(MonoBehaviour target, Action<MonoBehaviour> action)
    {
        if (action == null) return;
        try { action(target); }
        catch (Exception error) { Report(error, target); }
    }
    internal static void Report(Exception error, Object owner)
    {
        if (ExceptionRaised != null) ExceptionRaised(error, owner);
        else Console.Error.WriteLine($"[RimUnity] {owner?.name}: {error}");
    }
    internal static GameObject FindGameObject(string name)
    {
        AssertThread();
        foreach (GameObject go in Objects) if (go.activeInHierarchy && go.name == name) return go;
        return null;
    }
    internal static T[] FindObjects<T>(bool includeInactive, FindObjectsSortMode sortMode) where T : Object
    {
        AssertThread(); var result = new List<T>();
        foreach (GameObject go in Objects)
        {
            if (!go || go.IsAssetToken || (!includeInactive && !go.activeInHierarchy)) continue;
            if (go is T value) result.Add(value);
            foreach (Component component in go.Components) if (component && component is T item) result.Add(item);
        }
        if (sortMode == FindObjectsSortMode.InstanceID) result.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return result.ToArray();
    }
    //宿主退出当前游戏时释放场景树，资源模板由资源缓存单独管理。
    public static void ClearScene()
    {
        AssertThread();
        foreach (GameObject go in Objects.ToArray()) if (go && !go.IsAssetToken && go.transform.parent == null) Object.DestroyImmediate(go);
    }

    //只清理指定场景的实例，保留同帧中已创建的新场景对象。
    public static void ClearScene(object owner)
    {
        AssertThread();
        foreach (GameObject go in Objects.ToArray())
            if (go && !go.IsAssetToken && go.transform.parent == null && ReferenceEquals(go.SceneOwner, owner)) Object.DestroyImmediate(go);
    }
}
