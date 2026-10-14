using System;

namespace RimUnity;

public enum ParticleSystemSubEmitterType { Birth = 0, Collision = 1, Death = 2, Trigger = 3, Manual = 4 }
[Flags]
public enum ParticleSystemSubEmitterProperties { InheritNothing = 0, InheritColor = 1, InheritSize = 2, InheritRotation = 4, InheritLifetime = 8, InheritDuration = 16, InheritEverything = 31 }

//Birth 和 Death 子发射器保留对象引用及继承设置。
public sealed partial class ParticleSystem
{
    public SubEmittersModule subEmitters = new();
    public sealed class SubEmittersModule
    {
        public bool enabled;
        public SubEmitter[] entries = Array.Empty<SubEmitter>();
        public int subEmittersCount => entries.Length;
        public void AddSubEmitter(ParticleSystem system, int type, int properties, float emitProbability = 1)
        { ValidateType(type); Array.Resize(ref entries, entries.Length + 1); entries[^1] = new SubEmitter { system = system, type = type, properties = properties, probability = emitProbability }; }
        public void AddSubEmitter(ParticleSystem system, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties, float emitProbability = 1)
            => AddSubEmitter(system, (int)type, (int)properties, emitProbability);
        public void RemoveSubEmitter(int index) { var list = new System.Collections.Generic.List<SubEmitter>(entries); list.RemoveAt(index); entries = list.ToArray(); }
        public ParticleSystem GetSubEmitterSystem(int index) => entries[index].system;
        public ParticleSystemSubEmitterType GetSubEmitterType(int index) => (ParticleSystemSubEmitterType)entries[index].type;
        public ParticleSystemSubEmitterProperties GetSubEmitterProperties(int index) => (ParticleSystemSubEmitterProperties)entries[index].properties;
        public float GetSubEmitterEmitProbability(int index) => entries[index].probability;
        public void SetSubEmitterSystem(int index, ParticleSystem value) => entries[index].system = value;
        public void SetSubEmitterType(int index, ParticleSystemSubEmitterType value) { ValidateType((int)value); entries[index].type = (int)value; }
        public void SetSubEmitterProperties(int index, ParticleSystemSubEmitterProperties value) => entries[index].properties = (int)value;
        public void SetSubEmitterEmitProbability(int index, float value)
        { if (value < 0 || value > 1 || !float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); entries[index].probability = value; }
        private static void ValidateType(int type) { if (type < 0 || type > 4) throw new ArgumentOutOfRangeException(nameof(type)); }
    }
    public sealed class SubEmitter { public ParticleSystem system; public int type, properties; public float probability = 1; }
    [ThreadStatic] private static int subDepth;

    //层级引用在 Start 前已经还原，子发射器不会再被 playOnAwake 或父级 Play 重复启动。
    private bool IsSubEmitter()
    {
        foreach (ParticleSystem owner in Active)
            foreach (SubEmitter item in owner.subEmitters.entries)
                if (item.system == this) return true;
        Transform root = transform;
        while (root.parent != null) root = root.parent;
        foreach (ParticleSystem owner in root.GetComponentsInChildren<ParticleSystem>(true))
            foreach (SubEmitter item in owner.subEmitters.entries)
                if (item.system == this) return true;
        return false;
    }

    //空目标槽保留在模块中，但不触发事件，也不消耗概率随机数。
    private void SubEmit(Particle parent, int type, float offset = 0)
    {
        if (!subEmitters.enabled) return;
        foreach (var item in subEmitters.entries)
        {
            if (item.system == null || item.type != type || Random01() >= item.probability) continue;
            StartSubEmission(parent, item, offset);
        }
    }

    //手动触发保留选定父粒子的颜色、姿态和速度继承；不修改自动事件设置。
    public void TriggerSubEmitter(int subEmitterIndex, ref Particle particle)
    {
        SubEmitter entry = subEmitters.entries[subEmitterIndex];
        if (entry.type != (int)ParticleSystemSubEmitterType.Manual) throw new InvalidOperationException("手动触发要求 Manual 子发射器。");
        if (entry.system != null && Random01() < entry.probability) StartSubEmission(particle, entry, 0);
    }

    //不提供粒子时，对当前所有存活粒子触发指定子发射器。
    public void TriggerSubEmitter(int subEmitterIndex)
    {
        for (int i = 0; i < count; i++) { Particle particle = particles[i]; TriggerSubEmitter(subEmitterIndex, ref particle); }
    }

    //显式列表与 GetParticles 得到的快照可以直接用于手动发射。
    public void TriggerSubEmitter(int subEmitterIndex, System.Collections.Generic.List<Particle> source)
    {
        foreach (Particle item in source) { Particle particle = item; TriggerSubEmitter(subEmitterIndex, ref particle); }
    }
}
