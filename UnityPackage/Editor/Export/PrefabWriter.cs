using System;
using System.Collections.Generic;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    //通过 PrefabUtility 得到已解析嵌套与覆盖值的最终对象树。
    internal static class PrefabWriter
    {
        internal static PrefabData Read(GameObject prefab, ResourceWriter resources, string id, string path,
            Action<PrefabData, ExportContext> write)
        {
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath) || !PrefabUtility.IsPartOfPrefabAsset(prefab)) throw new ArgumentException("必须选择工程中的 Prefab 资产。");
            //FBX 导入模型本身就是只读对象树，不能传给只接收 .prefab 的 LoadPrefabContents。
            bool importedModel = PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Model;
            // Imported models have no LoadPrefabContents handle. Clone them when the
            // export policy mutates render state; never alter the imported asset.
            bool clonedModel = importedModel;
            GameObject root = importedModel ? (clonedModel ? Object.Instantiate(prefab) : prefab) : PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                if (clonedModel) { root.name = prefab.name; root.hideFlags = HideFlags.HideAndDontSave; }
                var context = resources.Context(prefab, path, id);
                context.ExportAsset = root;
                return resources.Processors.Run(context, current =>
                {
                var exportRoot = (GameObject)current.ExportAsset;
                resources.Options.Prepare(exportRoot, assetPath);
                var objects = new Dictionary<Object, string>();
                Collect(exportRoot.transform, "root", objects);
                var writer = new ValueWriter(objects, resources);
                var data = new PrefabData();
                Walk(exportRoot.transform, null, objects, writer, data);
                write(data, current);
                return data;
                });
            }
            finally { if (clonedModel) Object.DestroyImmediate(root); else if (!importedModel) PrefabUtility.UnloadPrefabContents(root); }
        }

        //引用编号只依赖层级和组件槽位，不保存编辑器实例 ID。
        private static void Collect(Transform transform, string id, Dictionary<Object, string> objects)
        {
            objects.Add(transform.gameObject, id);
            objects.Add(transform, id + "/transform");
            Component[] components = transform.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue;
                if (!(components[i] is Transform)) objects.Add(components[i], id + "/component/" + i);
            }
            for (int i = 0; i < transform.childCount; i++) Collect(transform.GetChild(i), id + "/" + i, objects);
        }

        //CanvasRenderer 和默认鼠标输入模块由桥接调度器承担，不序列化成空组件。
        private static void Walk(Transform transform, string parent, Dictionary<Object, string> objects, ValueWriter writer, PrefabData data)
        {
            var node = new NodeData { id = objects[transform.gameObject], transformId = objects[transform], parent = parent,
                name = transform.name, active = transform.gameObject.activeSelf, layer = transform.gameObject.layer,
                position = V3(transform.localPosition), rotation = new[] { transform.localRotation.x, transform.localRotation.y, transform.localRotation.z, transform.localRotation.w },
                scale = V3(transform.localScale), rect = transform is RectTransform };
            if (transform is RectTransform rect)
            {
                node.anchorMin = V2(rect.anchorMin); node.anchorMax = V2(rect.anchorMax); node.pivot = V2(rect.pivot);
                node.sizeDelta = V2(rect.sizeDelta); node.anchoredPosition = V2(rect.anchoredPosition);
            }
            Component[] components = transform.GetComponents<Component>();
            for (int slot = 0; slot < components.Length; slot++)
            {
                Component component = components[slot];
                if (component == null)
                {
                    string warning = node.id + " (" + node.name + ")/component/" + slot + "：源资源缺失脚本，已跳过此组件，其脚本行为不可用。";
                    data.warnings.Add(warning);
                    Debug.LogWarning("[RimUnity] 降级导出：" + warning);
                    continue;
                }
                if (component is Transform || component is CanvasRenderer) continue;
                if (component is UnityEngine.EventSystems.StandaloneInputModule) continue;
                node.components.Add(ComponentWriter.Write(component, objects[component], writer, node.id + " (" + transform.name + ")/" + component.GetType().Name, parent == null));
            }
            data.nodes.Add(node);
            for (int i = 0; i < transform.childCount; i++) Walk(transform.GetChild(i), node.id, objects, writer, data);
        }

        private static float[] V2(Vector2 v) => new[] { v.x, v.y };
        private static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
