using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Serialization;
using RimUnity.Runtime;

namespace RimUnity.Assets;

//恢复完整对象图后统一执行脚本生命周期。
internal static class PrefabBuilder
{
    //模板始终失活，资源对象不会产生 Awake、输入或渲染。
    internal static GameObject Template(AssetCatalog catalog, PrefabData data)
    {
        PrefabStructure.Validate(data);
        foreach (string warning in data.warnings) Verse.Log.Warning("[RimUnity] 资源降级加载：" + data.nodes[0].name + "；" + warning);
        foreach (var node in data.nodes)
            foreach (var component in node.components) TypeResolver.Component(component.type);
        GameObject template = Build(catalog, data, true);
        template.RegisterCloneFactory(() => Build(catalog, data, false));
        return template;
    }

    //先建全部对象，再建组件、绑定字段，最后恢复激活状态。
    private static GameObject Build(AssetCatalog catalog, PrefabData data, bool token)
    {
        using var creation = NativeRuntime.DeferLifecycle();
        var objects = new Dictionary<string, RimUnity.Object>(StringComparer.Ordinal);
        var components = new List<(Component instance, ComponentData data)>();
        var created = new List<GameObject>();
        GameObject root = null;
        try
        {
            foreach (NodeData node in data.nodes)
            {
                GameObject go = Ugui.Create(node.name, node.rect, token);
                created.Add(go);
                go.SetActive(false);
                go.layer = node.layer;
                objects.Add(node.id, go);
                objects.Add(node.transformId, go.transform);
                if (node.parent == null) root = go;
            }
            foreach (NodeData node in data.nodes)
            {
                GameObject go = (GameObject)objects[node.id];
                Transform t = go.transform;
                if (node.parent != null) t.SetParent(((GameObject)objects[node.parent]).transform, false);
                t.localPosition = V3(node.position);
                t.localRotation = new Quaternion(node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]);
                t.localScale = V3(node.scale);
                if (t is RectTransform rect)
                {
                    rect.anchorMin = V2(node.anchorMin); rect.anchorMax = V2(node.anchorMax); rect.pivot = V2(node.pivot);
                    rect.sizeDelta = V2(node.sizeDelta); rect.anchoredPosition = V2(node.anchoredPosition);
                }
                foreach (ComponentData item in node.components)
                {
                    Component component = go.AddComponent(TypeResolver.Component(item.type));
                    if (component is Behaviour behaviour) behaviour.enabled = item.enabled;
                    objects.Add(item.id, component);
                    components.Add((component, item));
                }
            }
            var reader = new ValueReader(catalog, objects);
            foreach (var item in components) reader.Apply(item.instance, item.data.members);
            if (!token)
            {
                //先恢复子物体再恢复根物体，事务退出之前不会运行脚本。
                foreach (NodeData node in data.nodes.Where(n => n.parent != null)) ((GameObject)objects[node.id]).SetActive(node.active);
                root.SetActive(data.nodes.Single(n => n.parent == null).active);
                root.name += "(Clone)";
            }
            return root;
        }
        catch
        {
            foreach (GameObject go in created) if (go != null) RimUnity.Object.DestroyImmediate(go);
            throw;
        }
    }

    private static Vector2 V2(float[] v) => new(v[0], v[1]);
    private static Vector3 V3(float[] v) => new(v[0], v[1], v[2]);
}
