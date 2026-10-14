using System;
using System.Collections.Generic;

namespace RimUnity;

public enum ParticleSystemCustomData { Custom1, Custom2 }
public enum ParticleSystemCustomDataMode { Disabled, Vector, Color }

//Custom1／Custom2 在模拟曲线上求值，也允许脚本按粒子覆盖。
public sealed partial class ParticleSystem
{
    public CustomDataModule customData = new();
    private readonly Dictionary<ulong, Vector4>[] customOverrides = { new(), new() };
    public sealed class CustomDataModule
    {
        public bool enabled;
        public CustomStream[] streams = { new(), new() };
        public void SetMode(ParticleSystemCustomData stream, ParticleSystemCustomDataMode mode) => streams[(int)stream].mode = mode;
        public ParticleSystemCustomDataMode GetMode(ParticleSystemCustomData stream) => streams[(int)stream].mode;
        public void SetVectorComponentCount(ParticleSystemCustomData stream, int count) => streams[(int)stream].components = count;
        public int GetVectorComponentCount(ParticleSystemCustomData stream) => streams[(int)stream].components;
        public void SetVector(ParticleSystemCustomData stream, int component, MinMaxCurve curve) => streams[(int)stream].curves[component] = curve;
        public MinMaxCurve GetVector(ParticleSystemCustomData stream, int component) => streams[(int)stream].curves[component];
        public void SetColor(ParticleSystemCustomData stream, MinMaxGradient color) => streams[(int)stream].color = color;
        public MinMaxGradient GetColor(ParticleSystemCustomData stream) => streams[(int)stream].color;
    }
    public sealed class CustomStream
    {
        public ParticleSystemCustomDataMode mode;
        public int components = 4;
        public MinMaxCurve[] curves = { 0, 0, 0, 0 };
        public MinMaxGradient color = Color.white;
    }
    public int GetCustomParticleData(List<Vector4> target, ParticleSystemCustomData stream)
    { target.Clear(); for (int i = 0; i < count; i++) target.Add(CustomAt(particles[i], (int)stream)); return count; }
    public void SetCustomParticleData(List<Vector4> source, ParticleSystemCustomData stream)
    {
        if (source.Count != count) throw new ArgumentException("自定义数据数量必须与当前粒子数量相同。", nameof(source));
        var values = customOverrides[(int)stream]; values.Clear();
        for (int i = 0; i < count; i++) values[particles[i].identity] = source[i];
    }
    internal Vector4 CustomAt(Particle p, int channel)
    {
        if (customOverrides[channel].TryGetValue(p.identity, out var value)) return value;
        if (!customData.enabled) return default;
        var stream = customData.streams[channel];
        float age = Age(p), r = Random(p.randomSeed, (uint)(70 + channel));
        if (stream.mode == ParticleSystemCustomDataMode.Color) return (Vector4)stream.color.Evaluate(age, r);
        if (stream.mode == ParticleSystemCustomDataMode.Vector) for (int i = 0; i < stream.components; i++) value[i] = stream.curves[i].Evaluate(age, r);
        return value;
    }
}
