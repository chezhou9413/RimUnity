using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Waylith.Rendering;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//只对已检查过完整程序的 Shader 合并世界空间几何，未知宿主程序保持原提交方式。
internal static class ParticleBatchShaders
{
    private sealed record Support(bool Allowed);
    private static readonly ConditionalWeakTable<NativeShader, Support> shaders = new();
    private static readonly string[] drawBuiltins =
    {
        "gl_VertexID", "gl_VertexIndex", "gl_InstanceID", "gl_InstanceIndex", "gl_PrimitiveID",
        "gl_DrawID", "gl_BaseVertex", "gl_BaseInstance", "gl_Subgroup", "subgroup", "##", "atomic", "imageAtomic", "imageStore", "subpassLoad"
    };

    //编号和存储缓冲可能依赖原始 Draw 边界，不能因减少提交而改变其语义。
    internal static void Register(NativeShader shader, string vertex, string fragment)
    {
        if (shaders.TryGetValue(shader, out _)) return;
        bool allowed = shader.MaterialLayout.StorageBufferSlots.Length == 0 && shader.SceneGrab == null;
        if (Regex.IsMatch(vertex, @"(?m)^\s*#\s*include\b") || Regex.IsMatch(fragment, @"(?m)^\s*#\s*include\b")) allowed = false;
        foreach (string token in drawBuiltins)
            if (vertex.Contains(token, StringComparison.Ordinal) || fragment.Contains(token, StringComparison.Ordinal)) allowed = false;
        shaders.Add(shader, new Support(allowed));
    }

    //内置映射只在加载阶段读取源码，不在逐帧绘制中做磁盘访问。
    internal static void RegisterMapped(NativeShader shader)
    {
        if (shaders.TryGetValue(shader, out _) || !shader.Key.StartsWith("RimUnity_", StringComparison.Ordinal)) return;
        Register(shader, ShaderSourceLoader.ReadShaderSource(shader.VertexShaderFileName), ShaderSourceLoader.ReadShaderSource(shader.FragmentShaderFileName));
    }

    //多 Pass 保留原来的逐对象 Pass 顺序；实例参数和 UI 遮罩在提交处另外隔离。
    internal static bool Allows(Material material) => material.passCount == 1
        && shaders.TryGetValue(material.shader.EngineShader, out var support) && support.Allowed;
}
