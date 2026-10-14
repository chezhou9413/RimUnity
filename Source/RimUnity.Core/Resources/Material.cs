using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity;

//自己管理材质和多 Pass 的生命周期，参数直接写入 Waylith。
public sealed class Material : Object
{
    private Shader currentShader;
    public Shader shadowCaster { get; set; }
    private Material shadowMaterial;
    private int shadowVersion = -1;
    //阴影与颜色通道共享参数快照，但分别持有管线状态。
    public Material GetShadowMaterial()
    {
        if (shadowCaster == null) return null;
        if (shadowMaterial == null || shadowMaterial.shader != shadowCaster)
        {
            if (shadowMaterial != null) Destroy(shadowMaterial);
            shadowMaterial = new Material(shadowCaster); shadowVersion = -1;
        }
        if (shadowVersion != engineMaterial.Version)
        {
            shadowMaterial.CopyPropertiesFromMaterial(this);
            shadowMaterial.renderQueue = 2000;
            shadowVersion = engineMaterial.Version;
        }
        return shadowMaterial;
    }
    private readonly List<Waylith.Rendering.Material> passes = new();
    private int passVersion = -1;
    private int shaderPassVersion = -1;
    private Shader[] overridePasses;
    private Shader[] EffectivePasses => overridePasses ?? shader.Passes;
    public static event Action<Material> Created;
    public static event Action<Material, int, Texture> TextureChanged;
    public Waylith.Rendering.Material engineMaterial { get; }
    public override string name { get => engineMaterial.DebugName; set => engineMaterial.DebugName = value; }
    public Shader shader
    {
        get => currentShader;
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(currentShader, value)) return;
            ReleasePasses(); overridePasses = null; currentShader = value; engineMaterial.Shader = value.EngineShader;
        }
    }
    public Material(Shader shader)
    {
        currentShader = shader ?? throw new ArgumentNullException(nameof(shader));
        engineMaterial = new(shader.EngineShader);
        SetColor("_Color", Color.white); Created?.Invoke(this);
    }
    public Material(Material source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        currentShader = source.shader; engineMaterial = new(source.engineMaterial);
        overridePasses = source.overridePasses;
        shadowCaster = source.shadowCaster;
    }
    public int renderQueue { get => engineMaterial.RenderQueue; set => engineMaterial.RenderQueue = value; }
    public int passCount => EffectivePasses.Length;
    public bool enableInstancing { get; set; }
    public Color color { get => GetColor("_Color"); set => SetColor("_Color", value); }
    public Texture mainTexture { get => GetTexture("_MainTex"); set => SetTexture("_MainTex", value); }
    public Vector2 mainTextureOffset { get => GetTextureOffset("_MainTex"); set => SetTextureOffset("_MainTex", value); }
    public Vector2 mainTextureScale { get => GetTextureScale("_MainTex"); set => SetTextureScale("_MainTex", value); }
    public void SetFloat(string name, float value) => SetFloat(Shader.PropertyToID(name), value);
    public void SetFloat(int id, float value) => engineMaterial.SetFloat(id, value);
    //材质数组直接写入设备参数，并使附加 Pass 在下次绘制时同步。
    public void SetFloatArray(string name, float[] values) => SetFloatArray(Shader.PropertyToID(name), values);
    public void SetFloatArray(int id, float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        engineMaterial.SetFloatArray(id, values); passVersion = -1;
    }
    //向量逐分量转换，保留 Unity 数组顺序和完整精度。
    public void SetVectorArray(string name, Vector4[] values) => SetVectorArray(Shader.PropertyToID(name), values);
    public void SetVectorArray(int id, Vector4[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Span<System.Numerics.Vector4> converted = values.Length <= 128
            ? stackalloc System.Numerics.Vector4[values.Length] : new System.Numerics.Vector4[values.Length];
        for (int i = 0; i < values.Length; i++) converted[i] = Vector4.ToNumerics(values[i]);
        engineMaterial.SetVectorArray(id, converted); passVersion = -1;
    }
    public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
    public float GetFloat(int id) => engineMaterial.GetFloat(id);
    public void SetInt(string name, int value) => SetInt(Shader.PropertyToID(name), value);
    public void SetInt(int id, int value) => engineMaterial.SetInt(id, value);
    public int GetInt(string name) => GetInt(Shader.PropertyToID(name));
    public int GetInt(int id) => engineMaterial.GetInt(id);
    public void SetColor(string name, Color value) => SetColor(Shader.PropertyToID(name), value);
    public void SetColor(int id, Color value) => engineMaterial.SetColor(id, new System.Numerics.Vector4(value.r, value.g, value.b, value.a));
    public Color GetColor(string name) => GetColor(Shader.PropertyToID(name));
    public Color GetColor(int id) { var v = engineMaterial.GetColor(id); return new(v.r, v.g, v.b, v.a); }
    public void SetVector(string name, Vector4 value) => SetVector(Shader.PropertyToID(name), value);
    public void SetVector(int id, Vector4 value)
    {
        if (ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", StringComparison.Ordinal))
        {
            string texture = name[..^3];
            SetTextureOffset(texture, new(value.z, value.w)); SetTextureScale(texture, new(value.x, value.y));
            return;
        }
        engineMaterial.SetVector(id, Vector4.ToNumerics(value));
    }
    public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));
    public Vector4 GetVector(int id)
    {
        if (ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", StringComparison.Ordinal)) { Vector2 o = GetTextureOffset(name[..^3]), s = GetTextureScale(name[..^3]); return new(s.x, s.y, o.x, o.y); }
        var v = engineMaterial.GetVector(id); return new(v.X, v.Y, v.Z, v.W);
    }
    public void SetMatrix(string name, Matrix4x4 value) => SetMatrix(Shader.PropertyToID(name), value);
    public void SetMatrix(int id, Matrix4x4 value) => engineMaterial.SetMatrix(id, Matrix4x4.ToNumerics(value));
    public void SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);
    public void SetTexture(int id, Texture value)
    {
        engineMaterial.SetTexture(id, value?.NativeTexture);
        //即使首次使用恒等变换也显式初始化 _ST，避免全零 UV。
        if (ShaderPropertyId.TryGetName(id, out string name)) UpdateTextureTransform(name);
        TextureChanged?.Invoke(this, id, value);
    }
    public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
    public Texture GetTexture(int id) => Texture.Resolve(engineMaterial.GetTexture(id));
    public bool HasProperty(string name) => HasProperty(Shader.PropertyToID(name));
    public bool HasProperty(int id)
    {
        if (engineMaterial.HasProperty(id)) return true;
        foreach (Shader pass in EffectivePasses) if (pass.HasProperty(id)) return true;
        return false;
    }
    public void SetTextureOffset(string name, Vector2 offset) { engineMaterial.SetTextureOffset(Shader.PropertyToID(name), Vector2.ToNumerics(offset)); UpdateTextureTransform(name); }
    public void SetTextureScale(string name, Vector2 scale) { engineMaterial.SetTextureScale(Shader.PropertyToID(name), Vector2.ToNumerics(scale)); UpdateTextureTransform(name); }
    public Vector2 GetTextureOffset(string name) { var v = engineMaterial.GetTextureOffset(Shader.PropertyToID(name)); return new(v.X, v.Y); }
    public Vector2 GetTextureScale(string name) { var v = engineMaterial.GetTextureScale(Shader.PropertyToID(name)); return new(v.X, v.Y); }
    private void UpdateTextureTransform(string name)
    {
        Vector2 offset = GetTextureOffset(name), scale = GetTextureScale(name);
        engineMaterial.SetVector(Shader.PropertyToID(name + "_ST"), new(offset.x, offset.y, scale.x, scale.y));
    }
    public void EnableKeyword(string keyword) => engineMaterial.EnableKeyword(keyword);
    public void DisableKeyword(string keyword) => engineMaterial.DisableKeyword(keyword);
    public bool IsKeywordEnabled(string keyword) => engineMaterial.IsKeywordEnabled(keyword);
    public void SetBuffer(string name, ComputeBuffer buffer) => engineMaterial.SetStorageBuffer(Shader.PropertyToID(name), buffer.Bytes);
    public void CopyPropertiesFromMaterial(Material source)
    {
        engineMaterial.CopyPropertiesFrom(source.engineMaterial); engineMaterial.CopyArraysFrom(source.engineMaterial);
        renderQueue = source.renderQueue;
    }
    //导出材质可以保存独立的 Pass 组合，克隆时共享组合并独立持有参数。
    public void SetPasses(Shader[] shaders)
    {
        if (shaders == null || shaders.Length == 0 || !ReferenceEquals(shaders[0], shader))
            throw new ArgumentException("第一个 Pass 必须是当前材质的 Shader。", nameof(shaders));
        foreach (Shader pass in shaders) if (pass == null) throw new ArgumentException("Pass 不能为空。", nameof(shaders));
        ReleasePasses(); overridePasses = (Shader[])shaders.Clone();
    }

    //附加 Pass 仅在源参数变动时同步，避免每次绘制分配包装对象。
    public Waylith.Rendering.Material GetNativePass(int index)
    {
        if ((uint)index >= passCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0) return engineMaterial;
        if (passes.Count != passCount - 1 || overridePasses == null && shaderPassVersion != shader.PassVersion)
        {
            ReleasePasses();
            for (int i = 1; i < passCount; i++) passes.Add(new(EffectivePasses[i].EngineShader));
            shaderPassVersion = shader.PassVersion;
        }
        if (passVersion != engineMaterial.Version)
        {
            foreach (var pass in passes) { pass.CopyPropertiesFrom(engineMaterial); pass.CopyArraysFrom(engineMaterial); pass.RenderQueue = renderQueue; pass.WrapModeOverride = engineMaterial.WrapModeOverride; }
            passVersion = engineMaterial.Version;
        }
        return passes[index - 1];
    }
    private void ReleasePasses() { foreach (var pass in passes) pass.Dispose(); passes.Clear(); passVersion = -1; }
    protected override void Release() { ReleasePasses(); if (shadowMaterial != null) Destroy(shadowMaterial); engineMaterial.Dispose(); }
}
