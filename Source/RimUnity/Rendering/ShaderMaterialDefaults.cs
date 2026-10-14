using System.Collections.Generic;
using RimUnity.Serialization;

namespace RimUnity.Rendering;

//独立 Shader 的默认属性按进程缓存，仅作用于 RimUnity 已注册的程序。
internal static class ShaderMaterialDefaults
{
    private static readonly Dictionary<string, MemberData[]> Defaults = new();
    internal static void Install() => Material.Created += Apply;
    internal static void Register(string key, MemberData[] defaults) => Defaults[key] = defaults;

    //在构造后补齐 Unity Properties 默认值，后续脚本与导出材质仍可覆盖。
    private static void Apply(Material material)
    {
        Shader shader = material.shader;
        if (shader == null || !Defaults.TryGetValue(shader.name, out var defaults)) return;
        foreach (var property in defaults)
        {
            var v = property.value;
            if (v.kind == "vector") Assets.ResourceLoader.SetMaterialVector(material, property.name, new Vector4(v.vector[0], v.vector[1], v.vector[2], v.vector[3]));
            else if (v.kind == "integer") material.SetInt(property.name, (int)v.number);
            else if (v.kind == "textureDefault")
                material.SetTexture(property.name, v.text switch { "black" => Texture2D.blackTexture, "gray" => Texture2D.grayTexture, "bump" => Texture2D.normalTexture, _ => Texture2D.whiteTexture });
            else material.SetFloat(property.name, (float)v.number);
        }
    }
}
