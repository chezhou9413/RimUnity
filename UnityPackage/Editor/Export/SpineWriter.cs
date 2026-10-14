using System;
using System.Collections;
using System.Reflection;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RimUnity.Editor
{
    //通过完整类型名接入可选 Spine 包，普通 Unity 工程不需要安装 Spine。
    internal static class SpineWriter
    {
        internal static bool Component(Component component, ComponentData data, ValueWriter writer, string path)
        {
            string type = component.GetType().FullName;
            if (type == "Spine.Unity.SkeletonSubmeshGraphic") return true;
            if (type != "Spine.Unity.SkeletonGraphic") return false;
            var serialized = new SerializedObject(component);
            foreach (string setting in new[] { "enableSeparatorSlots", "meshGenerator.settings.tintBlack", "meshGenerator.settings.canvasGroupCompatible" })
                if (serialized.FindProperty(setting)?.boolValue == true) throw new NotSupportedException(path + "：Spine 设置尚未接通：" + setting);
            if (serialized.FindProperty("layoutScaleMode").enumValueIndex != 0 || serialized.FindProperty("meshGenerator.settings.zSpacing").floatValue != 0)
                throw new NotSupportedException(path + "：Spine 自动布局或槽深度偏移尚未接通。");
            foreach (string name in new[] { "skeletonDataAsset", "initialSkinName", "initialFlipX", "initialFlipY", "startingAnimation", "startingLoop", "timeScale", "freeze", "color", "raycastTarget", "maskable" })
                data.members.Add(writer.Property(component, name, path));
            foreach (string name in new[] { "pivotOffset", "physicsPositionInheritanceFactor", "physicsRotationInheritanceFactor", "physicsMovementRelativeTo", "unscaledTime" })
                Put(name, Field(component, name));
            Put("pmaVertexColors", serialized.FindProperty("meshGenerator.settings.pmaVertexColors").boolValue);
            Canvas canvas = component.GetComponentInParent<Canvas>();
            bool ui = !writer.CameraUi && (canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay);
            foreach (string name in new[] { "material", "additiveMaterial", "multiplyMaterial", "screenMaterial" })
            {
                Material material = name == "material" ? ((Graphic)component).material : (Material)Field(component, name);
                data.members.Add(new MemberData { name = name, property = true, value = material == null ? writer.Write(null, path) : writer.Material(material, path + "." + name, ui, !ui) });
            }
            return true;
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, path + "." + name) });
        }

        //骨骼源数据、图集、混合表保留原始内容，不导出制作端缓存和控制器对象。
        internal static bool Resource(UnityEngine.Object asset, ResourceData data, ValueWriter writer, string path)
        {
            string type = asset.GetType().FullName;
            string[] fields;
            if (type == "Spine.Unity.SkeletonDataAsset")
            {
                if (((IList)Field(asset, "skeletonDataModifiers")).Count != 0 || (UnityEngine.Object)Field(asset, "controller") != null)
                    throw new NotSupportedException(path + "：Spine 数据修改器和 Mecanim 控制器尚未接通。");
                fields = new[] { "atlasAssets", "skeletonJSON", "scale", "defaultMix", "fromAnimation", "toAnimation", "duration" };
                var text = (TextAsset)Field(asset, "skeletonJSON");
                data.members.Add(new MemberData { name = "isBinary", value = writer.Write(AssetDatabase.GetAssetPath(text).EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase), path) });
            }
            else if (type == "Spine.Unity.SpineAtlasAsset")
            {
                //SkeletonGraphic 使用自己的 UI 材质；图集材质只提供每页图片。
                fields = new[] { "atlasFile" };
                var materials = (Material[])Field(asset, "materials");
                var textures = new Texture[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                    textures[i] = materials[i].mainTexture ?? throw new InvalidOperationException(path + "：Spine 图集页缺少贴图。");
                data.members.Add(new MemberData { name = "textures", value = writer.Write(textures, path + ".textures") });
            }
            else return false;
            data.type = type;
            foreach (string name in fields) data.members.Add(new MemberData { name = name, value = writer.Write(Field(asset, name), path + "." + name) });
            return true;
        }

        //只读取版本明确的序列化字段，字段缺失时报告真实的不兼容。
        private static object Field(object value, string name)
        {
            for (Type type = value.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(value);
            }
            throw new MissingFieldException(value.GetType().FullName, name);
        }
    }
}
