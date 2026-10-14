using System.Collections.Generic;
using RimUnity.Serialization;
using UnityEngine;
using UnityEngine.Rendering;

namespace RimUnity.Editor
{
    //独立 Shader 的默认值随程序发布，支持游戏脚本直接 new Material(shader)。
    internal static class ShaderDefaultsWriter
    {
        internal static MemberData[] Read(Material material)
        {
            var result = new List<MemberData>();
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                var value = new ValueData();
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        Vector4 v = shader.GetPropertyType(i) == ShaderPropertyType.Color ? (Vector4)material.GetColor(name) : material.GetVector(name);
                        value.kind = "vector"; value.vector = new[] { v.x, v.y, v.z, v.w }; break;
                    case ShaderPropertyType.Texture:
                        value.kind = "textureDefault"; value.text = shader.GetPropertyTextureDefaultName(i);
                        //显式声明的同名向量保留材质值，不再追加自动缩放偏移默认项。
                        if (shader.FindPropertyIndex(name + "_ST") < 0)
                            result.Add(new MemberData { name = name + "_ST", value = new ValueData { kind = "vector", vector = new[] { 1f, 1f, 0f, 0f } } });
                        break;
                    case ShaderPropertyType.Int: value.kind = "integer"; value.number = material.GetInteger(name); break;
                    default: value.kind = "number"; value.number = material.GetFloat(name); break;
                }
                result.Add(new MemberData { name = name, value = value });
            }
            return result.ToArray();
        }
    }
}
