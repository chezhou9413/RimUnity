using System;
using System.Text;
using System.Collections.Generic;

namespace RimUnity.Diagnostics;

internal static class RendererDebugCapture
{
    internal static string CompositeState = "场景合成：尚未采样。";
    internal static void Capture(Component owner, Mesh mesh, Material material, string kind, float width, IReadOnlyList<Color> colors)
    {
        if (!ParticleDebugWindow.Capturing) return;
        string key = kind + ":" + owner.GetInstanceID() + ":" + material.GetInstanceID();
        if (!ParticleDebugWindow.BeginSample(key)) return;
        try
        {
            float minAlpha = float.PositiveInfinity, maxAlpha = float.NegativeInfinity;
            for (int i = 0; i < Math.Min(colors.Count, 4096); i++) { minAlpha = Math.Min(minAlpha, colors[i].a); maxAlpha = Math.Max(maxAlpha, colors[i].a); }
            var report = new StringBuilder();
            report.AppendLine(kind + "：" + owner.gameObject.name + " #" + owner.GetInstanceID());
            report.AppendLine("层：" + owner.gameObject.layer + "；宽度乘数：" + width + "；顶点 Alpha 范围：[" + minAlpha + ", " + maxAlpha + "]");
            report.AppendLine("材质：" + material.name + "；Shader：" + material.shader.name);
            var clock = Shader.GetGlobalVector("_Time");
            report.AppendLine("全局 _Time：(" + clock.x + ", " + clock.y + ", " + clock.z + ", " + clock.w + ")");
            report.AppendLine("meshId：" + mesh.EngineMesh.Id + "；顶点：" + mesh.vertexCount);
            foreach (string property in new[] { "_CoreWidth", "_EdgeGlow", "_Intensity", "_AlphaMul", "_FadeStart", "_PulseMin", "_PulseMax", "_CycleDuration", "_PlaybackSpeed", "_TimeOffset", "_PreviewTime", "_RimUnitySrgb_MotionTex" })
                if (material.HasProperty(property)) report.AppendLine(property + "：" + material.GetFloat(property));
            foreach (string property in new[] { "_Color", "_EdgeColor", "_MainTex_ST", "_NoiseTex_ST", "_MotionTex_TexelSize" })
                if (material.HasProperty(property)) { var v = material.GetVector(property); report.AppendLine(property + "：(" + v.x + ", " + v.y + ", " + v.z + ", " + v.w + ")"); }
            var channel = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(1, channel);
            if (channel.Count > 0) { var first = channel[0]; report.AppendLine("UV1 首项：(" + first.x + ", " + first.y + ", " + first.z + ", " + first.w + ")"); }
            foreach (var slot in material.engineMaterial.Shader.MaterialLayout.TextureSlots)
            {
                var tex = material.engineMaterial.GetTexture(slot.PropertyId);
                report.AppendLine(slot.PropertyName + "：" + (tex == null ? "无显式绑定，default=" + slot.Default : tex.Name + " " + tex.width + "×" + tex.height));
            }
            ParticleDebugWindow.Record(key, owner.gameObject.name + " / " + kind + " #" + owner.GetInstanceID(), report.ToString(), material.engineMaterial.Id, mesh.EngineMesh.Id);
        }
        catch (Exception error) { ParticleDebugWindow.Record(key, owner.gameObject.name, "诊断失败：" + error.Message); }
    }
}
