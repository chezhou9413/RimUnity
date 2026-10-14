using System;
using System.Text;
using System.Globalization;
using Waylith.Rendering;
using RimUnity.Diagnostics;

namespace RimUnity;

public sealed partial class ParticleSystemRenderer
{
    private static string FormatVector(Vector4 v) => string.Format(CultureInfo.InvariantCulture, "({0:G9}, {1:G9}, {2:G9}, {3:G9})", v.x, v.y, v.z, v.w);
    private string DiagnosticPath()
    {
        string path = gameObject.name; var current = transform.parent; int depth = 0;
        while (current != null && depth++ < 32) { path = current.gameObject.name + "/" + path; current = current.parent; }
        return path;
    }
    private void AppendNativeBindings(StringBuilder report, Material mat)
    {
        int blockCount = propertyBlock?.PropertyCount ?? 0;
        var overrides = new DrawProperty[Math.Min(blockCount, 256)];
        if (blockCount <= 256 && blockCount > 0) propertyBlock.CopyTo(overrides);
        report.AppendLine("绘制属性块：" + blockCount + " 项" + (blockCount > 256 ? "（超过诊断上限，未展开）" : ""));
        for (int pass = 0; pass < Math.Min(8, mat.passCount); pass++)
        {
            var native = mat.GetNativePass(pass); var shader = native.Shader; var layout = shader.MaterialLayout;
            report.AppendLine("--- Pass " + pass + "：" + shader.Key + " ---");
            report.AppendLine("UseTexture=" + shader.UseTexture + "；FlipTextureST=" + shader.FlipTextureST + "；布局错误=" + layout.HasErrors);
            report.AppendLine("Shader 文件：" + shader.VertexShaderFileName + " | " + shader.FragmentShaderFileName);
            report.AppendLine("纹理槽数：" + layout.TextureSlots.Length + "；材质属性数：" + native.Properties.Length);
            foreach (var slot in layout.TextureSlots)
            {
                var tex = native.GetTexture(slot.PropertyId); bool overridden = false;
                if (blockCount <= 256) foreach (var value in overrides)
                    if (value.PropertyId == slot.PropertyId && value.Type == DrawPropertyType.Texture) { tex = value.ReferenceValue as Waylith.Rendering.Texture; overridden = true; break; }
                report.AppendLine("槽 " + slot.PropertyName + " / id=" + slot.PropertyId + " / binding=" + slot.Binding + " / resource=" + slot.ResourceSlot
                    + " / kind=" + slot.Kind + " / vertex=" + slot.VertexVisible + " / default=" + slot.Default);
                report.AppendLine("  原生绑定" + (overridden ? "（属性块覆盖）" : "（材质）") + "：" + (tex == null ? "空，宿主可能采用默认纹理" : tex.Name + " " + tex.width + "×" + tex.height
                    + " / filter=" + tex.filterMode + " / wrap=" + tex.wrapMode + " / readable=" + tex.IsReadable + " / GPU分配=" + (tex.GpuResource != null) + " / residency=" + tex.ResidencyGeneration));
                if (tex?.GpuResource is { } gpu) report.AppendLine("  Vulkan 图像：view=" + gpu.ImageView + " / layout=" + gpu.CurrentLayout + " / format=" + gpu.Format + " / swizzle=" + gpu.Swizzle + " / disposed=" + gpu.IsDisposed);
            }
            int printed = 0;
            foreach (var uniform in layout.UniformSlots)
            {
                if (printed++ >= 128) { report.AppendLine("Uniform 超过 128 项，余项省略。"); break; }
                report.AppendLine("Uniform " + uniform.PropertyName + " / type=" + uniform.Type + " / byteOffset=" + uniform.Offset + " / textureResource=" + uniform.TextureResourceSlot);
                foreach (var value in native.Properties.Span) if (value.PropertyId == uniform.PropertyId) report.AppendLine("  材质原生值：" + DescribeProperty(value));
                if (blockCount <= 256) foreach (var value in overrides) if (value.PropertyId == uniform.PropertyId) report.AppendLine("  属性块覆盖：" + DescribeProperty(value));
            }
        }
    }
    private unsafe void AppendUploadedUV(StringBuilder report, Mesh mesh)
    {
        var native = mesh.EngineMesh; var storage = native.CpuStorage; var layout = storage.Layout;
        report.AppendLine("精确 meshId：" + native.Id + "；网格版本：" + native.Version);
        if (!layout.TryGetAttribute(Waylith.Graphics.Meshes.VertexAttributeSemantic.TexCoord0, out var slot)) { report.AppendLine("上传网格没有 UV0。"); return; }
        int stride = layout.GetStreamStride(slot.Stream); byte* pointer = (byte*)storage.GetVertexStream(slot.Stream).Pointer;
        report.AppendLine("上传字节 UV0：stream=" + slot.Stream + " / stride=" + stride + " / offset=" + slot.OffsetBytes);
        for (int i = 0; i < Math.Min(8, mesh.vertexCount); i++)
        {
            float* uv = (float*)(pointer + i * stride + slot.OffsetBytes);
            report.AppendLine("上传 UV0[" + i + "]：" + FormatVector(new Vector4(uv[0], uv[1], uv[2], uv[3])));
        }
    }
    private static string DescribeProperty(DrawProperty value) => value.Type + " / float=" + value.FloatValue.ToString("G9", CultureInfo.InvariantCulture)
        + " / int=" + value.IntValue + " / vector=" + value.VectorValue.ToString();
    private void CaptureDiagnostics(Material mat, Mesh submittedMesh)
    {
        if (!ParticleDebugWindow.Capturing) return;
        string key = GetInstanceID() + ":" + mat.GetInstanceID();
        if (!ParticleDebugWindow.BeginSample(key)) return;
        try { CaptureDiagnosticReport(mat, key, submittedMesh); }
        catch (Exception error) { ParticleDebugWindow.Record(key, gameObject.name + " #" + GetInstanceID(), "诊断采样失败（未中断粒子绘制）：" + error); }
    }
    private void CaptureDiagnosticReport(Material mat, string key, Mesh submittedMesh)
    {
        var texture = mat.GetTexture("_MainTex");
        bool white = ReferenceEquals(texture, Texture2D.whiteTexture);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        int count = Math.Min(packedUV.Count, 4096); bool finite = true;
        for (int i = 0; i < count; i++)
        {
            var v = packedUV[i]; finite &= float.IsFinite(v.x) && float.IsFinite(v.y);
            minX = Math.Min(minX, v.x); maxX = Math.Max(maxX, v.x); minY = Math.Min(minY, v.y); maxY = Math.Max(maxY, v.y);
        }
        var report = new StringBuilder();
        report.AppendLine("实例：" + gameObject.name + " / Renderer #" + GetInstanceID());
        report.AppendLine("提交：" + (uiEntry == null ? "世界网格" : "UI 网格") + "，层 " + gameObject.layer + "，模式 " + renderMode + "，朝向 " + alignment);
        report.AppendLine("材质：" + mat.name + " #" + mat.GetInstanceID());
        report.AppendLine("Shader：" + mat.shader?.name + "，Pass 数 " + mat.passCount);
        report.AppendLine("_MainTex：" + (texture == null ? "缺失" : texture.name + " #" + texture.GetInstanceID()) + (white ? "（默认白图）" : ""));
        if (texture is Texture2D image) report.AppendLine("贴图尺寸：" + image.width + " × " + image.height);
        report.AppendLine("_MainTex_ST（Unity scale.xy / offset.zw）：" + FormatVector(mat.GetVector("_MainTex_ST")));
        report.AppendLine("UV 输入为 TEXCOORD0.xy；Shader 是否变换此 UV 需对照程序。");
        report.AppendLine("Shader 支持：" + mat.shader.isSupported + "；材质版本：" + mat.engineMaterial.Version + "；队列：" + mat.renderQueue);
        report.AppendLine("对象路径：" + DiagnosticPath());
        report.AppendLine("颜色 RGBA：" + FormatVector((Vector4)mat.color));
        foreach (string property in new[] { "_SingleChannel", "_HdrMultiply", "_SrcBlend", "_DstBlend", "_DissolveAmount" })
            if (mat.HasProperty(property)) report.AppendLine(property + "：" + mat.GetFloat(property));
        report.AppendLine("顶点 / 索引：" + vertices.Count + " / " + indices.Count);
        report.AppendLine("顶点流：" + string.Join(", ", streams));
        report.AppendLine("TEXCOORD0.xy：X [" + minX + ", " + maxX + "] Y [" + minY + ", " + maxY + "]；采样 " + count + " / " + packedUV.Count);
        report.AppendLine("UV 有限：" + finite + "；UV 恒定：" + (minX == maxX && minY == maxY));
        if (texture == null || white) report.AppendLine("提示：当前提交使用缺失贴图或默认白图，请检查材质加载与实例复制。");
        if (!finite || minX == maxX && minY == maxY) report.AppendLine("提示：UV 非有限或恒定，请检查顶点流与网格生成。");
        for (int i = 0; i < Math.Min(8, packedUV.Count); i++) report.AppendLine("UV0[" + i + "]：" + FormatVector(packedUV[i]));
        if (texture is Texture2D readable && readable.isReadable)
        {
            report.AppendLine("CPU 贴图格式：" + readable.format);
            foreach (var point in new[] { (0, 0), (readable.width / 2, readable.height / 2), (readable.width - 1, readable.height - 1) })
                report.AppendLine("CPU RGBA [" + point.Item1 + ", " + point.Item2 + "]：" + FormatVector((Vector4)readable.GetPixel(point.Item1, point.Item2)));
        }
        if (submittedMesh != null) AppendUploadedUV(report, submittedMesh);
        AppendNativeBindings(report, mat);
        report.AppendLine("证据边界：以上为原生材质、Shader 布局及绘制属性块；GPU 描述符与最终像素未读回。GPU 分配存在不等于采样正确。");
        ParticleDebugWindow.Record(key, gameObject.name + " #" + GetInstanceID() + " / " + mat.name, report.ToString(), mat.engineMaterial.Id, submittedMesh?.EngineMesh.Id ?? 0);
    }
}
