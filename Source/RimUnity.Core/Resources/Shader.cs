using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity;

//保存 Waylith Shader 身份和 RimUnity 材质默认值入口。
public sealed partial class Shader : Object
{
    private static readonly Dictionary<string, Shader> Cache = new(StringComparer.Ordinal);
    public Waylith.Rendering.Shader EngineShader { get; }
    public int renderQueue => EngineShader.DefaultRenderQueue;
    public bool isSupported => !EngineShader.HasErrors;
    public Shader[] Passes { get; private set; }
    internal int PassVersion { get; private set; }
    public int passCount => Passes.Length;
    public bool triangleEdges { get; set; }
    private Shader(string key, Waylith.Rendering.Shader shader) { name = key; EngineShader = shader; Passes = new[] { this }; }
    public static Shader FromEngineShader(string name, Waylith.Rendering.Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        if (Cache.TryGetValue(name, out Shader result) && ReferenceEquals(result.EngineShader, shader)) return result;
        result = new(name, shader); Cache[name] = result; return result;
    }
    public static Shader Find(string name) => ShaderRegistry.TryResolve(name, out var shader) ? FromEngineShader(name, shader) : null;
    public static int PropertyToID(string name) => ShaderPropertyId.GetOrCreate(name);
    public bool HasProperty(int id)
    {
        if (EngineShader.HasProperty(id)) return true;
        //宿主的普通属性清单不包含存储缓冲，扩展遮罩也需要通过属性查询识别。
        foreach (var storage in EngineShader.MaterialLayout.StorageBufferSlots)
            if (storage.PropertyId == id) return true;
        foreach (var block in EngineShader.MaterialLayout.UniformBlocks)
            foreach (var member in block.Members) if (member.PropertyId == id) return true;
        return false;
    }
    public void SetPasses(Shader[] passes)
    {
        if (passes == null || passes.Length == 0 || !ReferenceEquals(passes[0], this)) throw new ArgumentException("第一个 Pass 必须是当前 Shader。", nameof(passes));
        foreach (Shader pass in passes) if (pass == null) throw new ArgumentException("Pass 不能为空。", nameof(passes));
        Passes = (Shader[])passes.Clone();
        PassVersion++;
    }
}
