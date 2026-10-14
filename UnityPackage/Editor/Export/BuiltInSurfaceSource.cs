using System;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //内置受光材质仍用 Standard 表面模型，保留原材质的纹理和混合状态。
    internal static class BuiltInSurfaceSource
    {
        internal static bool Supports(Material material) => material.shader.name == "Standard" || material.shader.name == "Particles/Standard Surface";
        internal static string Path(Material material)
        {
            if (material.shader.name == "Legacy Shaders/Particles/Alpha Blended Premultiply")
            {
                if (material.IsKeywordEnabled("SOFTPARTICLES_ON")) throw new NotSupportedException(material.name + "：预乘粒子的相机深度采样尚未接通。");
                return System.IO.Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuiltInSurfaceSource).Assembly).resolvedPath, "Editor/Shaders/LegacyParticlePremultiply.shader");
            }
            if (!Supports(material)) return AssetDatabase.GetAssetPath(material.shader);
            foreach (string flag in new[] { "_DistortionEnabled", "_SoftParticlesEnabled", "_CameraFadingEnabled", "_ColorMode" })
                if (material.HasProperty(flag) && material.GetFloat(flag) != 0)
                    throw new NotSupportedException(material.name + "：受光粒子尚未接通 " + flag);
            foreach (string keyword in material.shaderKeywords)
                if (keyword == "_PARALLAXMAP" || keyword == "_DETAIL_MULX2" || keyword == "_ALPHAPREMULTIPLY_ON")
                    throw new NotSupportedException(material.name + "：内置受光材质尚未接通 " + keyword);
            return System.IO.Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuiltInSurfaceSource).Assembly).resolvedPath,
                "Editor/Shaders/StandardSurface.shader");
        }
    }
}
