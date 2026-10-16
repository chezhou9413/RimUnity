using System;
using System.Collections.Generic;

namespace RimUnity;

//紧凑粒子数组负责模拟；原生 GameObject 仅承担层级、生命周期和变换。
public sealed partial class ParticleSystem : MonoBehaviour
{
    internal static readonly HashSet<ParticleSystem> Active = new();
    internal Particle[] particles = Array.Empty<Particle>();
    internal int count;
    private readonly BridgeTime clock = new();
    private float elapsed, carry, distanceCarry, delay;
    private Vector3 previousPosition;
    private uint rng = 1;
    private bool playing, emitting, paused, started, completed;
    public uint randomSeed { get; set; } = 1;
    public bool useAutoRandomSeed { get; set; } = true;
    public int particleCount => count;
    public bool isPlaying => playing && !paused;
    public bool isPaused => paused;
    public bool isStopped => !playing && !paused;
    public bool isEmitting => emitting && !paused;
    public float time { get => elapsed; set => elapsed = value; }

    //注册时不重放已暂停或手动停止的系统。
    private void OnEnable() { Active.Add(this); clock.Reset(); previousPosition = transform.position; }
    private void Start()
    {
        if (started) return;
        if (IsSubEmitter()) { ResumeSubEmitter(); return; }
        started = true;
        if (main.playOnAwake) BeginPlayback();
    }
    private void OnDisable() { Active.Remove(this); ReleaseParticleLights(); }
    private void OnDestroy() { Active.Remove(this); ReleaseShapeMesh(); ReleaseParticleLights(); GetComponent<ParticleSystemRenderer>()?.ReleaseGeometry(); }
    //粒子全部模拟完成后更新地图照明，避免多个子步反复更新 GlowGrid。
    private void LateUpdate() => UpdateParticleLights();
    private void Update() => SimulateFrame();

    //Play 恢复暂停；停止后的 Play 从新一轮发射时间开始。
    public void Play(bool withChildren = true) => PlaySystems(withChildren);

    //层级子系统与事件引用一起暂停，手动暂停后不会再被 Start 自动启动。
    public void Pause(bool withChildren = true)
    {
        foreach (ParticleSystem system in CollectSimulationSystems(withChildren, withChildren))
        {
            if (!system.started) system.ResetPlayback();
            system.paused = true; system.started = true;
        }
    }

    //先收集依赖再取消事件，保留对已脱离层级的事件子系统的控制。
    public void Stop(bool withChildren = true, ParticleSystemStopBehavior stopBehavior = ParticleSystemStopBehavior.StopEmitting)
    {
        foreach (ParticleSystem system in CollectSimulationSystems(withChildren, withChildren))
        {
            system.emitting = system.playing = system.paused = system.prewarmPending = false;
            system.started = true; system.subEmissionEvents.Clear();
            if (stopBehavior == ParticleSystemStopBehavior.StopEmittingAndClear) system.ClearParticles();
        }
    }

    //清理整个目标集合，不因先移除父事件而漏掉外部子引用。
    public void Clear(bool withChildren = true)
    {
        foreach (ParticleSystem system in CollectSimulationSystems(withChildren, withChildren)) system.ClearParticles();
    }

    //单系统粒子、拖尾与事件状态一起释放，播放时钟由调用方决定。
    private void ClearParticles()
    {
        ClearPhysicsState(); ReleaseParticleLights();
        Array.Clear(particles, 0, count);
        count = 0; ClearTrails(); customOverrides[0].Clear(); customOverrides[1].Clear();
        subEmissionEvents.Clear(); subParentMotions.Clear();
    }

    //事件依赖仍有粒子或延迟发射时，整组效果仍然存活。
    public bool IsAlive(bool withChildren = true)
    {
        foreach (ParticleSystem system in CollectSimulationSystems(withChildren, withChildren))
            if (system.emitting || system.count > 0 || system.trailStates.Count > 0 || system.subEmissionEvents.Count > 0) return true;
        return false;
    }

    //先重置整个目标集合，再按父子依赖逐步模拟，结束后统一暂停。
    public void Simulate(float t, bool withChildren = true, bool restart = true, bool fixedTimeStep = true)
    {
        if (t < 0 || !float.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));
        SimulateSystems(t, withChildren, restart, fixedTimeStep);
    }
    public void Emit(int amount) => Emit(default, amount);
    public void Emit(EmitParams emitParams, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        EnsureCapacity();
        EmitParticles(emitParams, amount);
    }

    private void EnsureCapacity()
    {
        if (main.maxParticles < 0) throw new InvalidOperationException("maxParticles 不能为负。");
        if (particles.Length == main.maxParticles) return;
        for (int i = main.maxParticles; i < count; i++) ReleaseParticle(particles[i].identity);
        Array.Resize(ref particles, main.maxParticles); count = Math.Min(count, main.maxParticles);
    }
    private float Random01() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
    internal static float Random(uint seed, uint salt) { uint v = seed ^ (salt * 0x9e3779b9u); v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; return (v & 0xFFFFFF) / 16777216f; }
}
