using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity;

//每实例绘制参数直接实现 Waylith 数据提供接口。
public sealed class MaterialPropertyBlock : IDrawPropertyProvider
{
    private readonly Dictionary<int, DrawProperty> values = new();
    private readonly Dictionary<int, ComputeBuffer> buffers = new();
    private readonly Waylith.Rendering.MaterialPropertyBlock arrays = new();
    private System.Numerics.Vector4[] vectorScratch = Array.Empty<System.Numerics.Vector4>();
    //创建空的实例绘制参数。
    public MaterialPropertyBlock() { }

    //复制参数容器；纹理和缓冲区继续引用同一资源。
    public MaterialPropertyBlock(MaterialPropertyBlock source)
    {
        ArgumentNullException.ThrowIfNull(source);
        values = new(source.values);
        buffers = new(source.buffers);
        arrays = source.arrays.Clone();
    }

    public int PropertyCount => values.Count;
    public bool HasStorageBindings => buffers.Count > 0 || arrays.HasStorageBindings;
    public void Clear() { values.Clear(); buffers.Clear(); arrays.Clear(); }
    //数组按值交给宿主保存，绘制提交时由宿主生成独立快照。
    public void SetVectorArray(string name, Vector4[] value) => SetVectorArray(Shader.PropertyToID(name), value);
    public void SetVectorArray(int id, Vector4[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (vectorScratch.Length != value.Length) vectorScratch = new System.Numerics.Vector4[value.Length];
        for (int i = 0; i < value.Length; i++) vectorScratch[i] = Vector4.ToNumerics(value[i]);
        arrays.SetVectorArray(id, vectorScratch);
    }
    public void SetFloat(string name, float value) => SetFloat(Shader.PropertyToID(name), value);
    public void SetFloat(int id, float value) => values[id] = DrawProperty.Float(id, value);
    public void SetInt(string name, int value) => SetInt(Shader.PropertyToID(name), value);
    public void SetInt(int id, int value) => values[id] = DrawProperty.Int(id, value);
    public void SetVector(string name, Vector4 value) => SetVector(Shader.PropertyToID(name), value);
    public void SetVector(int id, Vector4 value)
    {
        if (ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", StringComparison.Ordinal)) value = new(value.z, value.w, value.x, value.y);
        values[id] = DrawProperty.Vector(id, Vector4.ToNumerics(value));
    }
    public void SetColor(string name, Color value) => SetColor(Shader.PropertyToID(name), value);
    public void SetColor(int id, Color value) => values[id] = DrawProperty.Color(id, Vector4.ToNumerics(value));
    public void SetMatrix(string name, Matrix4x4 value) => SetMatrix(Shader.PropertyToID(name), value);
    public void SetMatrix(int id, Matrix4x4 value) => values[id] = DrawProperty.Matrix(id, Matrix4x4.ToNumerics(value));
    public void SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);
    public void SetTexture(int id, Texture value) => values[id] = DrawProperty.Texture(id, value?.NativeTexture);
    public void SetBuffer(string name, ComputeBuffer value) => SetBuffer(Shader.PropertyToID(name), value);
    public void SetBuffer(int id, ComputeBuffer value) { if (value is null) buffers.Remove(id); else buffers[id] = value; }
    public float GetFloat(int id) => values.TryGetValue(id, out var property) ? property.FloatValue : 0;
    public Vector4 GetVector(int id)
    {
        var p = values.TryGetValue(id, out var property) ? property.VectorValue : default;
        return ShaderPropertyId.TryGetName(id, out string name) && name.EndsWith("_ST", StringComparison.Ordinal)
            ? new(p.Z, p.W, p.X, p.Y) : new(p.X, p.Y, p.Z, p.W);
    }
    public int CopyTo(Span<DrawProperty> destination)
    {
        if (destination.Length < values.Count) throw new ArgumentException("绘制参数目标空间不足。", nameof(destination));
        int index = 0; foreach (DrawProperty value in values.Values) destination[index++] = value; return index;
    }
    //数组使用宿主的帧存储池，复用属性块不会改变已经排队的其他实例。
    public void CollectStorageBindings(List<InstanceStorageBinding> output)
    {
        foreach (var pair in buffers) output.Add(pair.Value.Binding(pair.Key));
        arrays.CollectStorageBindings(output);
    }
}
