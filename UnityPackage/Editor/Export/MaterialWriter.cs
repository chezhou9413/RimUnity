using System;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //仅映射语义明确的内置 Unlit；自定义源码必须经过独立编译流程。
    internal static class MaterialWriter
    {
        internal static void Write(Material material, ResourceData data, ValueWriter writer, string path, ResourceWriter resources, bool ui, bool cameraUi = false)
        {
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, path + "." + name) });
            string shader = material.shader.name;
            string target;
            string textureProperty = "_MainTex", colorProperty = "_Color";
            bool legacyParticle = false;
            if (shader == "Unlit/Texture" || shader == "Unlit/Color") target = "RimUnity_Unlit";
            else if (shader == "Unlit/Transparent") target = "RimUnity_Transparent";
            else if (shader == "UI/Unlit/Transparent" || shader == "UI/Default") target = ui ? "RimUnity_UI" : "RimUnity_ParticleAlpha";
            else if (shader == "Particles/Standard Unlit") target = StandardParticleMaterial.Target(material, path);
            else if (shader == "Legacy Shaders/Particles/Alpha Blended" || shader == "Particles/Alpha Blended" || shader == "Legacy Shaders/Particles/Additive" || shader == "Particles/Additive" || shader == "Mobile/Particles/Additive" || shader == "Mobile/Particles/Alpha Blended")
            {
                target = shader.EndsWith("Additive", StringComparison.Ordinal) ? "RimUnity_ParticleAdditive" : "RimUnity_ParticleAlpha";
                colorProperty = "_TintColor"; legacyParticle = !shader.StartsWith("Mobile/", StringComparison.Ordinal);
                if (material.IsKeywordEnabled("SOFTPARTICLES_ON")) throw new NotSupportedException(path + "：深度软粒子不在首阶段范围。");
            }
            else if (shader == "Universal Render Pipeline/Particles/Unlit")
            {
                if (material.GetFloat("_Surface") != 1 || material.GetFloat("_AlphaClip") != 0 || material.GetFloat("_ColorMode") != 0 || material.IsKeywordEnabled("_SOFTPARTICLES_ON") || material.IsKeywordEnabled("_FADING_ON") || material.IsKeywordEnabled("_FLIPBOOKBLENDING_ON"))
                    throw new NotSupportedException(path + "：粒子需要透明、Multiply 颜色、无软粒子／相机淡化／插帧设置。");
                int blend = (int)material.GetFloat("_Blend");
                if (blend != 0 && blend != 2) throw new NotSupportedException(path + "：粒子仅接通 Alpha／Additive 混合。");
                target = blend == 2 ? "RimUnity_ParticleAdditive" : "RimUnity_ParticleAlpha"; textureProperty = "_BaseMap"; colorProperty = "_BaseColor";
            }
            else if (shader == "Universal Render Pipeline/Unlit")
            {
                if (material.GetFloat("_AlphaClip") != 0) throw new NotSupportedException(path + "：URP AlphaClip 尚未接通。");
                bool transparent = material.GetFloat("_Surface") != 0;
                if (transparent && material.GetFloat("_Blend") != 0) throw new NotSupportedException(path + "：URP 当前只接通 Alpha 混合。");
                if (material.GetFloat("_Cull") != 2) throw new NotSupportedException(path + "：URP 非背面剔除尚未接通。");
                target = transparent ? "RimUnity_Transparent" : "RimUnity_Unlit";
                textureProperty = "_BaseMap"; colorProperty = "_BaseColor";
            }
            else
            {
                WriteCustom(material, data, writer, path, resources, ui, cameraUi);
                resources.WriteMaterialArrays(material, data, writer, path);
                return;
            }
            if (ui)
            {
                if (target == "RimUnity_ParticleAdditive") target = "RimUnity_UIAdditive";
                else if (target == "RimUnity_ParticleAlpha" || target == "RimUnity_Transparent" || target == "RimUnity_UI") target = "RimUnity_UI";
                else throw new NotSupportedException(path + "：该不透明材质的 UI 管线尚未接通。");
            }
            Put("shader", target);
            if (target.StartsWith("RimUnity_StandardParticle", StringComparison.Ordinal))
            {
                foreach (string name in new[] { "_Mode", "_FlipbookMode", "_CameraFadingEnabled", "_EmissionEnabled", "_Cutoff", "_CameraNearFadeDistance", "_CameraFarFadeDistance" }) Put(name, material.GetFloat(name));
                Put("_ColorAddSubDiff", material.GetVector("_ColorAddSubDiff"));
                Put("_ColorMode", material.IsKeywordEnabled("_COLOROVERLAY_ON") ? 2f : material.IsKeywordEnabled("_COLORCOLOR_ON") ? 3f : material.IsKeywordEnabled("_COLORADDSUBDIFF_ON") ? 1f : 0f);
                Put("_EmissionColor", (Vector4)material.GetColor("_EmissionColor"));
                Put("_EmissionMap", material.GetTexture("_EmissionMap"));
            }
            Put("_MainTex", material.HasProperty(textureProperty) ? material.GetTexture(textureProperty) : null);
            Color color = material.HasProperty(colorProperty) ? material.GetColor(colorProperty) : Color.white;
            if (legacyParticle) color *= 2;
            Put("_Color", (Vector4)color);
            Vector2 scale = material.HasProperty(textureProperty) ? material.GetTextureScale(textureProperty) : Vector2.one;
            Vector2 offset = material.HasProperty(textureProperty) ? material.GetTextureOffset(textureProperty) : Vector2.zero;
            Put("_MainTex_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
        }

        //自定义 Shader 保留每个公开属性与原始程序，内建 Lit 没有明文源时明确失败。
        private static void WriteCustom(Material material, ResourceData data, ValueWriter writer, string path, ResourceWriter resources, bool ui, bool cameraUi)
        {
            //Surface 的颜色程序由离线工具生成，自动光照 Pass 不作为额外颜色层重复绘制。
            string sourcePath = BuiltInSurfaceSource.Path(material);
            bool surface = sourcePath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                && System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(sourcePath), @"(?m)^\s*#pragma\s+surface\b");
            bool builtInGraph = sourcePath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                && System.IO.File.ReadAllText(sourcePath).Contains("BuiltInLitSubTarget");
            if (surface || builtInGraph)
                data.members.Add(new MemberData { name = "shader", value = new ValueData { kind = "asset", text = resources.AddShader(material, ui, path, 0, cameraUi: cameraUi) } });
            else if (material.passCount > 1)
            {
                var passes = new System.Collections.Generic.List<ValueData>();
                var subshader = UnityEditor.ShaderUtil.GetShaderData(material.shader).ActiveSubshader;
                if (subshader == null || subshader.PassCount != material.passCount)
                    throw new InvalidOperationException(path + "：Unity 当前 SubShader 的 Pass 清单与材质不一致。");
                int sourcePass = 0;
                for (int i = 0; i < material.passCount; i++)
                {
                    //GrabPass 占据 Unity Pass 槽位，转换器通过 sceneGrab 表达它。
                    if (subshader.GetPass(i).IsGrabPass) continue;
                    int programPass = sourcePass++;
                    if (!material.GetShaderPassEnabled(material.GetPassName(i))) continue;
                    //无投影的 Renderer 不提交 ShadowCaster，不能把它当作第二个颜色 Pass。
                    if (IsShadowPass(material, i)) continue;
                    passes.Add(new ValueData { kind = "asset", text = resources.AddShader(material, ui, path + ".pass[" + i + "]", programPass, cameraUi: cameraUi) });
                }
                if (passes.Count == 0) throw new NotSupportedException(path + "：材质没有启用的颜色 Pass。");
                if (ui && passes.Count > 1) throw new NotSupportedException(path + "：UI 自定义多颜色 Pass 尚未接通。");
                data.members.Add(new MemberData { name = "shader", value = passes[0] });
                data.members.Add(new MemberData { name = "shaderPasses", value = new ValueData { kind = "array", items = passes } });
            }
            else data.members.Add(new MemberData { name = "shader", value = new ValueData { kind = "asset", text = resources.AddShader(material, ui, path, cameraUi: cameraUi) } });
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, path + "." + name) });
            var shader = material.shader;
            for (int pass = 0; !ui && !cameraUi && pass < material.passCount; pass++)
                if (IsShadowPass(material, pass))
                {
                    data.members.Add(new MemberData { name = "shadowCaster", value = new ValueData { kind = "asset",
                        text = resources.AddShader(material, false, path + ".shadowCaster", surface || builtInGraph ? -2 : pass) } });
                    break;
                }
            if (BuiltInSurfaceSource.Supports(material)) Put("_RimUnityParticle", material.shader.name.StartsWith("Particles/", StringComparison.Ordinal) ? 1f : 0f);
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                //未启用淡化时 Unity 保存的倒数可能为无穷大；这些值不参与生成程序。
                if (BuiltInSurfaceSource.Supports(material) && (name == "_CameraFadeParams" || name == "_SoftParticleFadeParams")) continue;
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: Put(name, (Vector4)material.GetColor(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: Put(name, material.GetVector(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: Put(name, material.GetFloat(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int: Put(name, material.GetInteger(name)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(name);
                        //空纹理保持 Shader 的白／黑／法线默认值，不写入覆盖项。
                        if (texture != null) Put(name, texture);
                        else if (material.shader.GetPropertyTextureDimension(i) == UnityEngine.Rendering.TextureDimension.Cube)
                            data.members.Add(new MemberData { name = name, value = new ValueData { kind = "asset", text = resources.DefaultGrayCube() } });
                        else if (material.shader.GetPropertyTextureDefaultName(i) == "bump")
                            data.members.Add(new MemberData { name = name, value = new ValueData { kind = "asset", text = resources.DefaultBumpTexture() } });
                        Vector2 scale = material.GetTextureScale(name), offset = material.GetTextureOffset(name);
                        //显式声明的缩放偏移向量由属性分支读取，避免与自动生成的同名项重复。
                        if (shader.FindPropertyIndex(name + "_ST") < 0)
                            Put(name + "_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
                        break;
                    default: throw new NotSupportedException(path + "：材质属性类型未适配：" + name);
                }
            }
        }

        //部分 ScriptedImporter 的 Pass 标签查询为空，标准 ShadowCaster Pass 名仍可用于识别。
        internal static bool IsShadowPass(Material material, int pass)
        {
            string mode = material.shader.FindPassTagValue(pass, new UnityEngine.Rendering.ShaderTagId("LightMode")).name;
            return string.Equals(mode, "ShadowCaster", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(mode) || material.shader.name.StartsWith("Cartoon FX/Remaster/", StringComparison.Ordinal))
                    && string.Equals(material.GetPassName(pass), "ShadowCaster", StringComparison.OrdinalIgnoreCase);
        }
    }
}


