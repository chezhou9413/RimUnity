using System;
using System.Collections;

namespace RimUnity;

//Legacy 组件实际驱动已有动画采样器；默认剪辑在 Start 自动播放。
public sealed class Animation : MonoBehaviour, IEnumerable
{
    public AnimationClip clip;
    public AnimationState[] states = Array.Empty<AnimationState>();
    public bool playAutomatically = true;
    public WrapMode wrapMode;
    private readonly BridgeTime clock = new();
    private Animator sampler;
    public bool isPlaying => Array.Exists(states, state => state.enabled);
    public AnimationState this[string name] => Array.Find(states, state => state.name == name);

    private void OnEnable() => clock.Reset();
    private void Start() { if (playAutomatically && clip != null) Play(); }
    private void Update()
    {
        float dt = clock.Step(false);
        Advance(dt);
    }

    //当前资源只使用单段 Legacy 播放；混合状态不能静默覆盖同一属性。
    internal void Advance(float dt)
    {
        if (dt < 0 || !float.IsFinite(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
        AnimationState active = ActiveState();
        if (active == null) return;
        active.time += dt * active.speed;
        SampleState(active);
        WrapMode mode = Mode(active);
        if (mode == WrapMode.Once && (active.speed >= 0 ? active.time >= active.length : active.time <= 0))
        { active.enabled = false; active.time = 0; }
    }

    public bool Play() => clip != null && Play(DefaultName());
    public bool Play(string name)
    {
        AnimationState target = this[name];
        if (target == null) return false;
        foreach (AnimationState state in states) state.enabled = false;
        target.enabled = true; target.time = target.speed < 0 ? target.length : 0;
        SampleState(target); return true;
    }
    public void Stop() { foreach (AnimationState state in states) { state.enabled = false; state.time = 0; } }
    public void Stop(string name) { AnimationState state = this[name]; if (state != null) { state.enabled = false; state.time = 0; } }
    public bool IsPlaying(string name) => this[name]?.enabled == true;
    public void Rewind() { foreach (AnimationState state in states) state.time = 0; }
    public void Rewind(string name) { if (this[name] is { } state) state.time = 0; }
    public int GetClipCount() => states.Length;
    public AnimationClip GetClip(string name) => this[name]?.clip;
    public IEnumerator GetEnumerator() => states.GetEnumerator();

    //脚本添加或移除剪辑时保留名称，而非强制使用资源文件名。
    public void AddClip(AnimationClip value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("动画状态名称不能为空。", nameof(name));
        AnimationState state = this[name];
        if (state == null) { Array.Resize(ref states, states.Length + 1); states[^1] = state = new AnimationState { name = name }; }
        state.clip = value; state.wrapMode = value.isLooping ? WrapMode.Loop : WrapMode.Default;
    }
    public void RemoveClip(string name) => states = Array.FindAll(states, state => state.name != name);
    public void RemoveClip(AnimationClip value) => states = Array.FindAll(states, state => state.clip != value);
    public void Sample() { AnimationState state = ActiveState(); if (state != null) SampleState(state); }

    private string DefaultName()
    {
        AnimationState state = Array.Find(states, value => value.clip == clip);
        if (state != null) return state.name;
        AddClip(clip, clip.name); return clip.name;
    }
    private AnimationState ActiveState()
    {
        AnimationState result = null;
        foreach (AnimationState state in states)
            if (state.enabled)
            {
                if (result != null) throw new NotSupportedException("Legacy Animation 多状态混合尚未接通。");
                result = state;
            }
        return result;
    }
    private WrapMode Mode(AnimationState state) => state.wrapMode != WrapMode.Default ? state.wrapMode
        : wrapMode != WrapMode.Default ? wrapMode : state.clip.isLooping ? WrapMode.Loop : WrapMode.Once;

    //循环与往返只转换采样时间，状态时间仍可通过 normalizedTime 查询。
    private void SampleState(AnimationState state)
    {
        if (state.clip == null) throw new InvalidOperationException("Legacy 动画状态缺少 Clip：" + state.name);
        if (state.layer != 0 || state.weight != 1) throw new NotSupportedException("Legacy Animation 分层和权重混合尚未接通。");
        if (state.clip.events.Length != 0) throw new NotSupportedException("Legacy Animation 事件尚未接通。");
        if (sampler == null) { sampler = gameObject.AddComponent<Animator>(); sampler.enabled = false; }
        float t = state.time, duration = state.length;
        t = Mode(state) switch
        {
            WrapMode.Loop => duration > 0 ? Mathf.Repeat(t, duration) : 0,
            WrapMode.PingPong => duration > 0 ? Mathf.PingPong(t, duration) : 0,
            _ => Mathf.Clamp(t, 0, duration)
        };
        sampler.SampleLegacyClip(state.clip, t);
    }
    private void OnDestroy() { if (sampler != null) Destroy(sampler); }
}
