using System;
using System.Collections;
using System.Collections.Generic;
using RimUnity.Runtime;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//恢复完整对象图后统一执行脚本生命周期。
internal static class PrefabBuilder
{
    //弱引用查找表仅用于子树克隆闭包保留外层对象 ID 映射，避免兄弟节点或宿主销毁后被闭包强引用导致内存泄漏。
    private sealed class WeakObjectLookup : IReadOnlyDictionary<string, RimUnity.Object>
    {
        private readonly Dictionary<string, WeakReference<RimUnity.Object>> _entries;

        internal WeakObjectLookup(Dictionary<string, WeakReference<RimUnity.Object>> entries)
        {
            _entries = entries;
        }

        internal Dictionary<string, WeakReference<RimUnity.Object>> Entries => _entries;

        public RimUnity.Object this[string key]
        {
            get
            {
                if (TryGetValue(key, out RimUnity.Object value))
                {
                    return value;
                }
                throw new KeyNotFoundException($"预制体对象引用未找到：{key}");
            }
        }

        public IEnumerable<string> Keys => _entries.Keys;

        public IEnumerable<RimUnity.Object> Values
        {
            get
            {
                foreach (WeakReference<RimUnity.Object> weakRef in _entries.Values)
                {
                    yield return weakRef.TryGetTarget(out RimUnity.Object target) && target != null ? target : null;
                }
            }
        }

        public int Count => _entries.Count;

        public bool ContainsKey(string key) => _entries.ContainsKey(key);

        public bool TryGetValue(string key, out RimUnity.Object value)
        {
            if (_entries.TryGetValue(key, out WeakReference<RimUnity.Object> weakRef))
            {
                value = weakRef.TryGetTarget(out RimUnity.Object target) && target != null ? target : null;
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, RimUnity.Object>> GetEnumerator()
        {
            foreach (KeyValuePair<string, WeakReference<RimUnity.Object>> pair in _entries)
            {
                RimUnity.Object resolved = pair.Value.TryGetTarget(out RimUnity.Object target) && target != null ? target : null;
                yield return new KeyValuePair<string, RimUnity.Object>(pair.Key, resolved);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    //构建期间合并当前新实例强引用与外层弱引用，克隆完成后丢弃不再被闭包持有。
    private sealed class BuildObjectLookup : IReadOnlyDictionary<string, RimUnity.Object>
    {
        private readonly Dictionary<string, RimUnity.Object> _local;
        private readonly WeakObjectLookup _outer;

        internal BuildObjectLookup(Dictionary<string, RimUnity.Object> local, WeakObjectLookup outer)
        {
            _local = local;
            _outer = outer;
        }

        public RimUnity.Object this[string key]
        {
            get
            {
                if (TryGetValue(key, out RimUnity.Object value))
                {
                    return value;
                }
                throw new KeyNotFoundException($"预制体对象引用未找到：{key}");
            }
        }

        public IEnumerable<string> Keys => _local.Keys;
        public IEnumerable<RimUnity.Object> Values => _local.Values;
        public int Count => _local.Count + (_outer != null ? _outer.Count : 0);

        public bool ContainsKey(string key) => _local.ContainsKey(key) || (_outer != null && _outer.ContainsKey(key));

        public bool TryGetValue(string key, out RimUnity.Object value)
        {
            if (_local.TryGetValue(key, out value))
            {
                return true;
            }
            if (_outer != null && _outer.TryGetValue(key, out value))
            {
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, RimUnity.Object>> GetEnumerator() => _local.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    //模板始终失活，资源对象不会产生 Awake、输入或渲染。
    internal static GameObject Template(AssetCatalog catalog, PrefabData data)
    {
        PrefabStructure.Validate(data);
        foreach (string warning in data.warnings)
        {
            Verse.Log.Warning("[RimUnity] 资源降级加载：" + data.nodes[0].name + "；" + warning);
        }
        foreach (NodeData node in data.nodes)
        {
            foreach (ComponentData component in node.components)
            {
                TypeResolver.Component(component.type);
            }
        }
        GameObject template = Build(catalog, data, true);
        template.RegisterCloneFactory(() => Build(catalog, data, false));
        return template;
    }

    //先建全部对象，再建组件、绑定字段，最后恢复激活状态。
    private static GameObject Build(AssetCatalog catalog, PrefabData data, bool token)
        => _BuildRange(catalog, data, data.nodes, null, token, null);

    //按导出的预制体结构单遍索引并克隆任意局部子树，子树内引用重映射到新实例，外层引用沿用宿主弱引用上下文。
    private static GameObject _BuildSubtree(AssetCatalog catalog, PrefabData data, string subtreeRootId, WeakObjectLookup outerObjects)
    {
        Dictionary<string, List<NodeData>> childrenByParent = new(StringComparer.Ordinal);
        NodeData subtreeRoot = null;
        for (int index = 0; index < data.nodes.Count; index++)
        {
            NodeData node = data.nodes[index];
            if (node.id == subtreeRootId)
            {
                subtreeRoot = node;
            }
            if (node.parent != null)
            {
                if (!childrenByParent.TryGetValue(node.parent, out List<NodeData> bucket))
                {
                    bucket = new List<NodeData>();
                    childrenByParent.Add(node.parent, bucket);
                }
                bucket.Add(node);
            }
        }
        if (subtreeRoot == null)
        {
            throw new InvalidOperationException($"预制体中未找到子树根节点：{subtreeRootId}");
        }

        List<NodeData> subtreeNodes = new();
        Queue<NodeData> pending = new();
        pending.Enqueue(subtreeRoot);
        while (pending.Count > 0)
        {
            NodeData current = pending.Dequeue();
            subtreeNodes.Add(current);
            if (childrenByParent.TryGetValue(current.id, out List<NodeData> children))
            {
                for (int index = 0; index < children.Count; index++)
                {
                    pending.Enqueue(children[index]);
                }
            }
        }
        return _BuildRange(catalog, data, subtreeNodes, subtreeRootId, false, outerObjects);
    }

    private static GameObject _BuildRange(AssetCatalog catalog, PrefabData data, IReadOnlyList<NodeData> nodes,
        string subtreeRootId, bool token, WeakObjectLookup outerObjects)
    {
        using var creation = NativeRuntime.DeferLifecycle();
        Dictionary<string, RimUnity.Object> objects = new(StringComparer.Ordinal);
        List<(Component instance, ComponentData data)> components = new();
        List<GameObject> created = new();
        GameObject root = null;
        try
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeData node = nodes[index];
                GameObject gameObject = Ugui.Create(node.name, node.rect, token);
                created.Add(gameObject);
                gameObject.SetActive(false);
                gameObject.layer = node.layer;
                objects.Add(node.id, gameObject);
                objects.Add(node.transformId, gameObject.transform);
                bool isRoot = subtreeRootId == null ? node.parent == null : node.id == subtreeRootId;
                if (isRoot)
                {
                    root = gameObject;
                }
            }

            IReadOnlyDictionary<string, RimUnity.Object> buildLookup = outerObjects == null
                ? objects
                : new BuildObjectLookup(objects, outerObjects);

            for (int index = 0; index < nodes.Count; index++)
            {
                NodeData node = nodes[index];
                GameObject gameObject = (GameObject)objects[node.id];
                Transform nodeTransform = gameObject.transform;
                bool isRoot = subtreeRootId == null ? node.parent == null : node.id == subtreeRootId;
                if (!isRoot && node.parent != null)
                {
                    nodeTransform.SetParent(((GameObject)objects[node.parent]).transform, false);
                }
                nodeTransform.localPosition = V3(node.position);
                nodeTransform.localRotation = new Quaternion(node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]);
                nodeTransform.localScale = V3(node.scale);
                if (nodeTransform is RectTransform rectTransform)
                {
                    rectTransform.anchorMin = V2(node.anchorMin); rectTransform.anchorMax = V2(node.anchorMax); rectTransform.pivot = V2(node.pivot);
                    rectTransform.sizeDelta = V2(node.sizeDelta); rectTransform.anchoredPosition = V2(node.anchoredPosition);
                }
                foreach (ComponentData item in node.components)
                {
                    Component component = gameObject.AddComponent(TypeResolver.Component(item.type));
                    if (component is Behaviour behaviour)
                    {
                        behaviour.enabled = item.enabled;
                    }
                    objects.Add(item.id, component);
                    components.Add((component, item));
                }
            }

            //所有对象与组件建齐后再构造单份弱引用查找表（含模板），确保克隆模板子节点时外层引用仍可解析且不引发强引用泄漏。
            WeakObjectLookup weakLookup = _CreateWeakLookup(objects, outerObjects);
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeData node = nodes[index];
                GameObject gameObject = (GameObject)objects[node.id];
                string capturedId = node.id;
                if (node.parent == null)
                {
                    gameObject.RegisterSubtreeCloneFactory(() => Build(catalog, data, false));
                }
                else
                {
                    gameObject.RegisterSubtreeCloneFactory(() => _BuildSubtree(catalog, data, capturedId, weakLookup));
                }
            }

            ValueReader reader = new(catalog, buildLookup);
            for (int index = 0; index < components.Count; index++)
            {
                reader.Apply(components[index].instance, components[index].data.members);
            }

            if (!token)
            {
                //先恢复子物体再恢复根物体，事务退出之前不会运行脚本。
                NodeData rootData = null;
                for (int index = 0; index < nodes.Count; index++)
                {
                    NodeData node = nodes[index];
                    bool isRoot = subtreeRootId == null ? node.parent == null : node.id == subtreeRootId;
                    if (isRoot)
                    {
                        rootData = node;
                    }
                    else
                    {
                        ((GameObject)objects[node.id]).SetActive(node.active);
                    }
                }
                root.SetActive(rootData.active);
                root.name += "(Clone)";
            }
            return root;
        }
        catch
        {
            for (int index = 0; index < created.Count; index++)
            {
                if (created[index] != null)
                {
                    RimUnity.Object.DestroyImmediate(created[index]);
                }
            }
            throw;
        }
    }

    //构造扁平弱引用字典，自动剔除已销毁或已被回收的外层弱引用，避免跨次克隆堆积。
    private static WeakObjectLookup _CreateWeakLookup(Dictionary<string, RimUnity.Object> local, WeakObjectLookup outer)
    {
        int capacity = local.Count + (outer != null ? outer.Count : 0);
        Dictionary<string, WeakReference<RimUnity.Object>> merged = new(capacity, StringComparer.Ordinal);
        if (outer != null)
        {
            foreach (KeyValuePair<string, WeakReference<RimUnity.Object>> pair in outer.Entries)
            {
                if (pair.Value.TryGetTarget(out RimUnity.Object target) && target != null)
                {
                    merged[pair.Key] = pair.Value;
                }
            }
        }
        foreach (KeyValuePair<string, RimUnity.Object> pair in local)
        {
            merged[pair.Key] = new WeakReference<RimUnity.Object>(pair.Value);
        }
        return new WeakObjectLookup(merged);
    }

    private static Vector2 V2(float[] v) => new(v[0], v[1]);
    private static Vector3 V3(float[] v) => new(v[0], v[1], v[2]);
}
