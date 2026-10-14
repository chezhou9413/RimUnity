using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//不创建引擎对象的载荷核对，可在游戏关闭时检查字段和引用适配。
public static class PrefabInspector
{
    //编译及静态核对不能替代游戏生命周期和画面验收。
    public static int ValidateDirectory(string directory)
    {
        var catalog = new AssetCatalog(directory);
        int count = 0;
        foreach (AssetRecord record in catalog.Manifest.assets.Where(a => a.type == "Prefab"))
        {
            PrefabData prefab = catalog.Read<PrefabData>(record.file);
            PrefabStructure.Validate(prefab);
            var types = new Dictionary<string, Type>();
            foreach (NodeData node in prefab.nodes)
            {
                types.Add(node.id, typeof(GameObject));
                types.Add(node.transformId, node.rect ? typeof(RectTransform) : typeof(Transform));
                foreach (ComponentData component in node.components) types.Add(component.id, TypeResolver.Component(component.type));
            }
            if (prefab.nodes.Count(n => n.parent == null) != 1) throw new InvalidDataException(record.path + "：根节点数量错误。");
            foreach (NodeData node in prefab.nodes)
            {
                if (node.parent != null && !types.ContainsKey(node.parent)) throw new InvalidDataException(node.name + "：父节点缺失。");
                foreach (ComponentData component in node.components)
                    InspectMembers(types[component.id], component.members, types, catalog.Manifest, record.path + "/" + node.name);
            }
            Console.WriteLine(record.path + "：" + prefab.nodes.Count + " 个节点，类型／字段／引用静态核对通过");
            count++;
        }
        return count;
    }

    //复用运行时的字段选择规则，但不实例化 MonoBehaviour。
    private static void InspectMembers(Type owner, IEnumerable<MemberData> members, Dictionary<string, Type> objects, Manifest manifest, string path)
    {
        foreach (var member in members)
        {
            PropertyInfo property = null;
            if (member.property)
                for (Type current = owner; current != null && property == null; current = current.BaseType)
                    property = current.GetProperty(member.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            FieldInfo field = null;
            if (property == null)
                for (Type current = owner; current != null && field == null; current = current.BaseType)
                    field = current.GetField(member.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Type type = property?.PropertyType ?? field?.FieldType ?? throw new MissingMemberException(owner.FullName, member.name);
            if (property != null && property.SetMethod == null && member.value.kind != "event") throw new InvalidDataException(path + "." + member.name + "：属性不可写。");
            InspectValue(type, member.value, objects, manifest, path + "." + member.name);
        }
    }

    //递归检查跨节点引用与事件签名，包括私有 SerializeField。
    private static void InspectValue(Type type, ValueData value, Dictionary<string, Type> objects, Manifest manifest, string path)
    {
        switch (value.kind)
        {
            case "object":
                if (!objects.TryGetValue(value.text, out Type target) || !type.IsAssignableFrom(target)) throw new InvalidDataException(path + "：对象引用类型不匹配：" + value.text);
                break;
            case "asset":
                AssetRecord asset = manifest.assets.Single(a => a.id == value.text);
                Type assetType = asset.type == "Prefab" ? typeof(GameObject) : TypeResolver.Resolve((asset.type == "TMP_FontAsset" ? "RimUnity.UI." : "RimUnity.") + asset.type);
                if (!type.IsAssignableFrom(assetType)) throw new InvalidDataException(path + "：资源引用类型不匹配。");
                break;
            case "data": InspectMembers(type, value.fields, objects, manifest, path); break;
            case "array":
                Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                foreach (ValueData item in value.items) InspectValue(element, item, objects, manifest, path + "[]");
                break;
            case "event":
                foreach (EventCallData call in value.calls)
                {
                    Type eventTarget = objects[call.target];
                    Type[] arguments = call.mode switch
                    {
                        0 => type.GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(),
                        1 => Type.EmptyTypes,
                        2 => new[] { TypeResolver.Resolve(call.argumentType) },
                        3 => new[] { typeof(int) }, 4 => new[] { typeof(float) }, 5 => new[] { typeof(string) }, 6 => new[] { typeof(bool) },
                        _ => throw new InvalidDataException(path + "：未知事件模式。")
                    };
                    if (eventTarget.GetMethod(call.method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null) == null)
                        throw new MissingMethodException(eventTarget.FullName, call.method);
                }
                break;
        }
    }
}
