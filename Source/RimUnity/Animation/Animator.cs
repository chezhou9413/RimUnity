using System;
using System.Collections.Generic;

namespace RimUnity;

//状态机在原生 Update 内推进，在 LateUpdate 前写回骨骼和组件属性。
public sealed partial class Animator : MonoBehaviour
{
    private RuntimeAnimatorController controller;
    private readonly Dictionary<string, float> values = new(StringComparer.Ordinal);
    private LayerPlayback[] playback = Array.Empty<LayerPlayback>();
    private readonly BridgeTime clock = new();
    public float speed { get; set; } = 1;
    public AnimatorUpdateMode updateMode { get; set; }
    public bool fireEvents { get; set; } = true;
    public bool keepAnimatorStateOnDisable { get; set; }
    public bool keepAnimatorControllerStateOnDisable { get => keepAnimatorStateOnDisable; set => keepAnimatorStateOnDisable = value; }
    //导出控制器的 Generic Avatar 路径由 Unity 编辑器解析为预制体内引用。
    public AnimatorBinding[] avatarBindings = Array.Empty<AnimatorBinding>();
    public int layerCount => playback.Length;
    public AnimatorControllerParameter[] parameters => controller?.parameters ?? Array.Empty<AnimatorControllerParameter>();
    public RuntimeAnimatorController runtimeAnimatorController { get => controller; set { controller = value; Rebind(); } }
    private sealed class LayerPlayback
    {
        internal AnimatorLayer definition;
        internal AnimatorState current, next;
        internal float currentTime, nextTime, fade, fadeDuration, weight;
    }

    //绑定放在字段与引用恢复之后，避免首次赋值时读取半成品层级。
    private void OnEnable()
    {
        clock.Reset(); BindTracks();
        //Apply the entry pose before this instance can be drawn again. Do not
        //advance transitions or dispatch events just because it was re-enabled.
        if (controller != null) Sample();
    }
    internal override void OnHierarchyDeactivated()
    {
        //Keep bindings and their authored initial values: rebuilding them from
        //the last animated pose would contaminate additive/blended layers.
        //Disabling only this component pauses it; pooling its hierarchy resets
        //controller state even when the component was already disabled.
        if (!keepAnimatorStateOnDisable) ResetControllerState();
    }
    private void Update() => Update(clock.Step(updateMode == AnimatorUpdateMode.UnscaledTime));
    public void Update(float deltaTime)
    {
        if (controller == null) return;
        if (deltaTime < 0 || !float.IsFinite(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        BindTracks();
        float dt = deltaTime * speed;
        for (int i = 0; i < playback.Length; i++) Advance(playback[i], dt);
        Sample();
    }
    //控制器或对象层级改变后，清空有效与缺失目标的绑定结果。
    public void Rebind()
    {
        trackBindings.Clear(); poses.Clear(); rotationBindings.Clear(); clipRotations.Clear(); boundClips.Clear(); tracksBound = false;
        ResetControllerState();
    }
    private void ResetControllerState()
    {
        values.Clear();
        if (controller == null) { playback = Array.Empty<LayerPlayback>(); return; }
        foreach (var p in controller.parameters) values.Add(p.name, p.type == AnimatorControllerParameterType.Float ? p.defaultFloat : p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultBool ? 1 : 0);
        playback = new LayerPlayback[controller.layers.Length];
        for (int i = 0; i < playback.Length; i++)
        {
            var layer = controller.layers[i];
            playback[i] = new LayerPlayback { definition = layer, weight = i == 0 ? 1 : layer.defaultWeight, current = FindState(layer, layer.defaultState) };
        }
        clock.Reset();
    }
    //所有参数入口检查存在性，拼写错误不会变成隐式新参数。
    public void SetFloat(string name, float value) => Set(name, value);
    public void SetFloat(int id, float value) => SetFloat(ParameterName(id), value);
    public void SetFloat(string name, float value, float dampTime, float deltaTime) => Set(name, Mathf.Lerp(GetFloat(name), value, dampTime <= 0 ? 1 : 1 - Mathf.Exp(-deltaTime / dampTime)));
    public float GetFloat(string name) => values.TryGetValue(name, out float v) ? v : throw new ArgumentException("不存在 Animator 参数：" + name);
    public float GetFloat(int id) => GetFloat(ParameterName(id));
    public void SetInteger(string name, int value) => Set(name, value);
    public void SetInteger(int id, int value) => SetInteger(ParameterName(id), value);
    public int GetInteger(string name) => (int)GetFloat(name);
    public int GetInteger(int id) => GetInteger(ParameterName(id));
    public void SetBool(string name, bool value) => Set(name, value ? 1 : 0);
    public void SetBool(int id, bool value) => SetBool(ParameterName(id), value);
    public bool GetBool(string name) => GetFloat(name) != 0;
    public bool GetBool(int id) => GetBool(ParameterName(id));
    public void SetTrigger(string name) => SetBool(name, true);
    public void SetTrigger(int id) => SetTrigger(ParameterName(id));
    public void ResetTrigger(string name) => SetBool(name, false);
    public void ResetTrigger(int id) => ResetTrigger(ParameterName(id));
    public float GetLayerWeight(int layerIndex) => playback[layerIndex].weight;
    public void SetLayerWeight(int layerIndex, float weight) => playback[layerIndex].weight = Mathf.Clamp01(weight);
    public string GetLayerName(int layerIndex) => playback[layerIndex].definition.name;
    public int GetLayerIndex(string layerName) => Array.FindIndex(playback, p => p.definition.name == layerName);
    public bool IsInTransition(int layerIndex) => playback[layerIndex].next != null;
    public bool HasState(int layerIndex, int stateID) => Array.Exists(playback[layerIndex].definition.states, s => StringToHash(s.name) == stateID || StringToHash(GetLayerName(layerIndex) + "." + s.name) == stateID);
    public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex) => Info(playback[layerIndex], false);
    public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex) => Info(playback[layerIndex], true);
    public void Play(string stateName, int layer = -1, float normalizedTime = float.NegativeInfinity)
    {
        int index = ResolveLayer(stateName, layer); var p = playback[index];
        var state = FindState(p.definition, stateName);
        p.currentTime = float.IsNegativeInfinity(normalizedTime) && state == p.current ? p.currentTime : float.IsNegativeInfinity(normalizedTime) ? 0 : normalizedTime * Duration(state.motion);
        p.current = state; p.next = null; p.fade = 0;
    }
    public void Play(int stateNameHash, int layer = -1, float normalizedTime = float.NegativeInfinity) => Play(StateName(stateNameHash, layer), layer, normalizedTime);
    public void CrossFade(string stateName, float normalizedTransitionDuration, int layer = -1, float normalizedTimeOffset = 0, float normalizedTransitionTime = 0)
    {
        int index = ResolveLayer(stateName, layer); var p = playback[index];
        Transition(p, FindState(p.definition, stateName), normalizedTransitionDuration * Duration(p.current.motion), normalizedTimeOffset);
        p.fade = p.fadeDuration * normalizedTransitionTime;
    }
    public void CrossFade(int stateNameHash, float duration, int layer = -1, float offset = 0) => CrossFade(StateName(stateNameHash, layer), duration, layer, offset);
    public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer = -1, float fixedTimeOffset = 0)
    { int index = ResolveLayer(stateName, layer); var p = playback[index]; var state = FindState(p.definition, stateName); Transition(p, state, fixedTransitionDuration, fixedTimeOffset / Duration(state.motion)); }
    public static int StringToHash(string text) { unchecked { uint hash = 2166136261; foreach (char c in text ?? "") { hash ^= c; hash *= 16777619; } return (int)hash; } }
    private void Set(string name, float value) { if (!values.ContainsKey(name)) throw new ArgumentException("不存在 Animator 参数：" + name); values[name] = value; }
    private string ParameterName(int id) { foreach (var p in parameters) if (p.nameHash == id) return p.name; throw new ArgumentException("不存在 Animator 参数哈希：" + id); }
    private string StateName(int id, int layer) { for (int i = 0; i < playback.Length; i++) if (layer < 0 || layer == i) foreach (var s in playback[i].definition.states) if (StringToHash(s.name) == id || StringToHash(GetLayerName(i) + "." + s.name) == id) return GetLayerName(i) + "." + s.name; throw new ArgumentException("不存在 Animator 状态哈希：" + id); }
    private int ResolveLayer(string state, int layer) { if (layer >= 0) return layer; for (int i = 0; i < playback.Length; i++) foreach (var s in playback[i].definition.states) if (s.name == state || GetLayerName(i) + "." + s.name == state) return i; throw new ArgumentException("不存在 Animator 状态：" + state); }
    private static AnimatorState FindState(AnimatorLayer layer, string name) { foreach (var s in layer.states) if (s.name == name || layer.name + "." + s.name == name) return s; throw new ArgumentException("不存在 Animator 状态：" + layer.name + "/" + name); }
    private AnimatorStateInfo Info(LayerPlayback layer, bool next)
    {
        var state = next ? layer.next : layer.current; if (state == null) return default;
        float length = Duration(state.motion);
        return new AnimatorStateInfo { shortNameHash = StringToHash(state.name), fullPathHash = StringToHash(layer.definition.name + "." + state.name), tagHash = StringToHash(state.tag), normalizedTime = (next ? layer.nextTime : layer.currentTime) / length, length = length, speed = StateSpeed(state), loop = Loops(state.motion) };
    }
}
