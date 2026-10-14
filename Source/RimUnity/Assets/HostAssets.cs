using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RimUnity.Assets;

//让 Verse 和 CL 使用标准导出的资源，共享底层资源且保留材质默认值。
public static class HostAssets
{
    private static readonly AccessTools.FieldRef<List<Waylith.Rendering.MeshDrawCommand>> Immediate =
        AccessTools.StaticFieldRefAccess<List<Waylith.Rendering.MeshDrawCommand>>(AccessTools.Field(typeof(UnityEngine.Graphics), "ImmediateCapture"));
    private static readonly Dictionary<Shader, UnityEngine.Shader> Shaders = new();
    private static readonly Dictionary<UnityEngine.Shader, Material> Defaults = new();
    private static readonly Dictionary<Texture2D, UnityEngine.Texture2D> Textures = new();
    private static readonly Dictionary<Material, UnityEngine.Shader> MaterialShaders = new();

    //复制宿主贴图的像素，调用方拥有新贴图；不接管原版资源的释放责任。
    public static Texture2D CopyTexture(UnityEngine.Texture2D source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, source.CoreTexture.CpuData?.Linear ?? false)
        { name = source.name, filterMode = (FilterMode)source.filterMode, wrapMode = (TextureWrapMode)source.wrapMode };
        result.SetPixels32(Array.ConvertAll(source.GetPixels32(), c => new Color32(c.r, c.g, c.b, c.a)));
        result.Apply(false, false);
        return result;
    }

    //宿主包装不承担共享 Shader 的销毁权，默认参数来自同一份导出描述。
    public static UnityEngine.Shader ToHost(Shader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Shaders.TryGetValue(source, out var cached)) return cached;
        var shader = UnityEngine.Shader.FromEngineShader(source.name, source.EngineShader);
        Shaders.Add(source, shader);
        Defaults.Add(shader, new Material(source));
        return shader;
    }

    //从标准材质创建调用方持有的宿主实例，保留参数、贴图变换、关键词和队列。
    public static UnityEngine.Material CreateMaterial(Material source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var material = new UnityEngine.Material(ToHost(source))
        { name = source.name, renderQueue = source.renderQueue, enableInstancing = source.enableInstancing };
        material.engineMaterial.CopyPropertiesFrom(source.engineMaterial);
        foreach (string keyword in source.engineMaterial.Keywords) material.EnableKeyword(keyword);
        return material;
    }

    //材质的颜色通道组合保存在独立宿主 Shader 中，避免污染共享的首通道。
    public static UnityEngine.Shader ToHost(Material source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.passCount == 1) return ToHost(source.shader);
        if (MaterialShaders.TryGetValue(source, out var cached)) return cached;
        var shader = UnityEngine.Shader.FromEngineShader(source.shader.name, source.shader.EngineShader);
        var passes = new Waylith.Rendering.Shader[source.passCount - 1];
        for (int i = 1; i < source.passCount; i++) passes[i - 1] = source.GetNativePass(i).Shader;
        AccessTools.Property(typeof(UnityEngine.Shader), "ExtraPasses").SetValue(shader, passes);
        MaterialShaders.Add(source, shader);
        Defaults.Add(shader, source);
        return shader;
    }

    //共享 CPU 像素和设备贴图，不重新解码或改变色彩空间。
    public static UnityEngine.Texture2D ToHost(Texture2D source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Textures.TryGetValue(source, out var cached)) return cached;
        var texture = (UnityEngine.Texture2D)AccessTools.Constructor(typeof(UnityEngine.Texture2D),
            new[] { typeof(Waylith.Rendering.Texture2D) }).Invoke(new object[] { source.CoreTexture });
        texture.name = source.name;
        Textures.Add(source, texture);
        return texture;
    }

    //宿主 DrawMeshNow 固定写入第零层，此入口为 Pawn 预览保留真实图层和属性快照。
    public static void DrawMeshNow(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix,
        UnityEngine.Material material, int layer, UnityEngine.MaterialPropertyBlock properties)
    {
        if ((uint)layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
        var commands = Immediate() ?? throw new InvalidOperationException("当前没有宿主即时绘制捕获上下文。");
        var range = Waylith.Rendering.MeshDrawSubmission.CopyProperties(properties);
        commands.Add(new Waylith.Rendering.MeshDrawCommand(mesh.EngineMesh, material.engineMaterial,
            range, UnityEngine.Matrix4x4.ToNumerics(matrix), 0, layer: layer,
            storage: Waylith.Rendering.MeshDrawSubmission.CollectDrawStorage(properties)));
    }

    //只为显式桥接的 Shader 补齐默认参数，CL 随后仍可应用自身 XML 参数。
    [HarmonyPatch(typeof(UnityEngine.Material), MethodType.Constructor, new[] { typeof(UnityEngine.Shader) })]
    private static class MaterialDefaults
    {
        private static void Postfix(UnityEngine.Material __instance, UnityEngine.Shader shader)
        {
            if (shader != null && Defaults.TryGetValue(shader, out var defaults))
                __instance.engineMaterial.CopyPropertiesFrom(defaults.engineMaterial);
        }
    }
}
