using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Waylith.Rendering;
using Verse;

namespace RimUnity.Diagnostics;

internal sealed partial class ParticleDebugWindow
{
    //仅保留当前采样网格的文本报告，关闭窗口时清空。
    private static readonly Dictionary<string, string> FinalReports = new();
    private static DateTime lastFinalSample;
    internal static void CaptureFinalFrame(MeshDrawFrame frame, MeshDrawFrame hdr)
    {
        if (!Capturing || !Prefs.DevMode || (DateTime.UtcNow - lastFinalSample).TotalSeconds < .5) return;
        lastFinalSample = DateTime.UtcNow;
        FinalReports.Clear();
        try
        {
            var wanted = snapshots.Values.Where(x => x.NativeMeshId != 0).Select(x => x.NativeMaterialId + ":" + x.NativeMeshId).ToHashSet();
            var seen = new HashSet<string>();
            foreach (var draw in frame.Draws)
            {
                int id = draw.Binding.MaterialId;
                string key = id + ":" + draw.Mesh.MeshId;
                if (!wanted.Contains(key) || !seen.Add(key)) continue;
                var report = new StringBuilder("最终帧 UTC：" + DateTime.UtcNow.ToString("O") + "\n已按材质和 meshId 精确关联所选实例：\n");
                DescribeFinal(report, draw, "宿主场景");
                if (hdr != null)
                {
                    var copy = hdr.Draws.FirstOrDefault(x => x.Binding.MaterialId == id && x.Mesh.MeshId == draw.Mesh.MeshId);
                    if (copy != null) DescribeFinal(report, copy, "HDR 接管");
                    else report.AppendLine("HDR：此网格没有匹配绘制项。");
                }
                else report.AppendLine("HDR：当前帧未接管。");
                FinalReports[key] = report.ToString();
            }
        }
        catch (Exception error)
        {
            //诊断失败必须报告原因，但不能中断宿主渲染。
            foreach (var snapshot in snapshots.Values) FinalReports[snapshot.NativeMaterialId + ":" + snapshot.NativeMeshId] = "最终帧诊断失败：" + error.Message;
        }
    }
    private static void DescribeFinal(StringBuilder report, FinalDrawItem draw, string stage)
    {
        report.AppendLine("--- " + stage + " ---");
        report.AppendLine("Shader=" + draw.Binding.Shader.Key + "; materialId=" + draw.Binding.MaterialId + "; meshId=" + draw.Mesh.MeshId + "; override=" + draw.HasOverride + "; pass=" + draw.PassId + "; layer=" + draw.Layer);
        var layout = draw.Binding.MaterialLayout;
        foreach (var slot in layout.TextureSlots)
        {
            var tex = draw.HasOverride ? draw.Resources[slot.ResourceSlot] : draw.Binding.Resources[slot.ResourceSlot];
            report.AppendLine("已解析资源 " + slot.PropertyName + " / binding=" + slot.Binding + " / resource=" + slot.ResourceSlot + "：" + (tex == null ? "空" : tex.Name + " " + tex.width + "×" + tex.height + " GPU=" + (tex.GpuResource != null)));
        }
        foreach (var slot in layout.TextureSlots)
        {
            var tex = draw.HasOverride ? draw.Resources[slot.ResourceSlot] : draw.Binding.Resources[slot.ResourceSlot];
            if (tex?.GpuResource is { } gpu) report.AppendLine("最终 Vulkan 图像 " + slot.PropertyName + "：view=" + gpu.ImageView + " / layout=" + gpu.CurrentLayout + " / swizzle=" + gpu.Swizzle + " / format=" + gpu.Format + " / disposed=" + gpu.IsDisposed);
        }
        var uniforms = draw.HasOverride ? draw.Uniforms : draw.Binding.Uniforms;
        foreach (var slot in layout.UniformSlots)
        {
            if (slot.PropertyName != "_Time" && !slot.PropertyName.Contains("MotionTex") && slot.PropertyName != "_PreviewTime"
                && slot.PropertyName != "_PlaybackSpeed" && slot.PropertyName != "_TimeOffset" && slot.PropertyName != "_CycleDuration") continue;
            if (slot.Type == MaterialUniformType.Float) report.AppendLine("最终 Uniform " + slot.PropertyName + "：" + uniforms.ReadFloat(slot.Offset));
            else if (slot.Type == MaterialUniformType.Vector4 || slot.Type == MaterialUniformType.TexelSize)
                report.AppendLine("最终 Uniform " + slot.PropertyName + "：" + uniforms.ReadVector4(slot.Offset));
        }
        foreach (var input in draw.Binding.Shader.VertexInputLayout.Slots)
        {
            bool found = draw.Mesh.Layout.TryGetAttribute(input.semantic, out var attribute);
            report.AppendLine("顶点输入 location=" + input.location + " / " + input.semantic + " / required=" + input.required + " / meshPresent=" + found + (found ? " / offset=" + attribute.OffsetBytes : " / fallback=" + input.defaultValue));
        }
        report.AppendLine("此处记录解析后的 CPU 绘制资源；仍未读回 Vulkan 描述符或像素。");
    }
}
