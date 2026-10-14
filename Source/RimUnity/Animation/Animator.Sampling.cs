using System;
using System.Collections.Generic;
using System.Reflection;
using RimUnity.Assets;

namespace RimUnity;

//属性绑定按对象路径复用，多个 Clip 混合到同一目标后只写回一次。
public sealed partial class Animator
{
    private readonly Dictionary<AnimationTrack, AnimatedFloat> trackBindings = new();
    private readonly Dictionary<string, AnimatedFloat> poses = new(StringComparer.Ordinal);
    private readonly Dictionary<AnimatedFloat, float> sampled = new(), accumulated = new();
    private bool tracksBound;
    private void BindTracks()
    {
        if (controller == null || tracksBound) return;
        foreach (var layer in controller.layers) foreach (var state in layer.states) BindMotion(state.motion);
        tracksBound = true;
    }
    private void BindMotion(AnimationMotion motion)
    {
        if (motion == null) return;
        if (motion.clip != null)
        {
            foreach (var track in motion.clip.tracks)
            {
                if (trackBindings.ContainsKey(track)) continue;
                Transform target = string.IsNullOrEmpty(track.path) ? transform : transform.Find(track.path);
                if (target == null) throw new InvalidOperationException("动画节点不存在：" + track.path);
                if (track.property.StartsWith("m_LocalRotation.", StringComparison.Ordinal))
                { BindRotation(motion.clip, track, target); continue; }
                object component = track.type == "RimUnity.GameObject" ? target.gameObject : target.GetComponent(TypeResolver.Component(track.type));
                if (component == null) throw new InvalidOperationException("动画目标组件不存在：" + track.path + "/" + track.type);
                string key = track.path + "|" + track.type + "|" + track.property;
                if (!poses.TryGetValue(key, out var binding)) poses.Add(key, binding = new AnimatedFloat(component, track.property, track.path));
                trackBindings.Add(track, binding);
            }
        }
        else foreach (var child in motion.children) BindMotion(child.motion);
    }
    private void Sample()
    {
        accumulated.Clear();
        foreach (var rotation in rotationBindings.Values) rotation.accumulated = rotation.initial;
        foreach (var binding in poses.Values) accumulated[binding] = binding.initial;
        foreach (var layer in playback)
        {
            if (layer.weight <= 0) continue;
            sampled.Clear();
            foreach (var rotation in rotationBindings.Values) { rotation.sum = Vector4.zero; rotation.weight = 0; }
            float blend = layer.next == null ? 0 : Mathf.Clamp01(layer.fade / layer.fadeDuration);
            SampleMotion(layer.current.motion, layer.currentTime, 1 - blend, layer.current.cycleOffset);
            if (layer.next != null) SampleMotion(layer.next.motion, layer.nextTime, blend, layer.next.cycleOffset);
            foreach (var pair in sampled)
            {
                if (!Masked(layer.definition, pair.Key.path)) continue;
                float original = accumulated[pair.Key];
                accumulated[pair.Key] = layer.definition.additive
                    ? pair.Key.scale && Mathf.Abs(pair.Key.initial) > .000001f
                        ? original * Mathf.LerpUnclamped(1, pair.Value / pair.Key.initial, layer.weight)
                        : original + (pair.Value - pair.Key.initial) * layer.weight
                    : Mathf.LerpUnclamped(original, pair.Value, layer.weight);
            }
            ApplyRotationLayer(layer);
        }
        foreach (var pair in accumulated) pair.Key.write(pair.Value);
        foreach (var rotation in rotationBindings.Values) rotation.target.localRotation = rotation.accumulated;
    }
    private void SampleMotion(AnimationMotion motion, float time, float weight, float offset)
    {
        if (motion == null || weight <= 0) return;
        if (motion.clip != null)
        {
            AnimationClip clip = motion.clip;
            float t = time + offset * clip.length;
            t = clip.isLooping && clip.length > 0 ? Mathf.Repeat(t, clip.length) : Mathf.Clamp(t, 0, clip.length);
            foreach (var track in clip.tracks)
            {
                if (track.property.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) continue;
                var binding = trackBindings[track];
                if (!sampled.TryGetValue(binding, out float previous)) previous = binding.initial;
                sampled[binding] = previous + (track.curve.Evaluate(t) - binding.initial) * weight;
            }
            SampleRotations(clip, t, weight);
        }
        else
        {
            Blend(motion, out var a, out var b, out float blend);
            float normalized = time / Duration(motion);
            SampleMotion(a.motion, normalized * Duration(a.motion) * Math.Sign(a.timeScale), weight * (1 - blend), offset + a.cycleOffset);
            if (b != null) SampleMotion(b.motion, normalized * Duration(b.motion) * Math.Sign(b.timeScale), weight * blend, offset + b.cycleOffset);
        }
    }
    private static bool Masked(AnimatorLayer layer, string path)
    {
        if (layer.mask.Length == 0) return true;
        bool active = false; int longest = -1;
        foreach (var entry in layer.mask) if ((path == entry.path || path.StartsWith(entry.path + "/", StringComparison.Ordinal)) && entry.path.Length > longest) { active = entry.active; longest = entry.path.Length; }
        return active;
    }
    private void Events(AnimationMotion motion, float before, float after, float weight, float offset)
    {
        if (motion == null || weight <= 0 || before == after) return;
        if (motion.clip == null)
        {
            Blend(motion, out var a, out var b, out float blend);
            float duration = Duration(motion);
            Events(a.motion, before / duration * Duration(a.motion), after / duration * Duration(a.motion), weight * (1 - blend), offset + a.cycleOffset);
            if (b != null) Events(b.motion, before / duration * Duration(b.motion), after / duration * Duration(b.motion), weight * blend, offset + b.cycleOffset);
            return;
        }
        var clip = motion.clip; if (clip.length <= 0) return;
        before += offset * clip.length; after += offset * clip.length;
        int first = clip.isLooping ? (int)Math.Floor(Math.Min(before, after) / clip.length) : 0;
        int last = clip.isLooping ? (int)Math.Floor(Math.Max(before, after) / clip.length) : 0;
        for (int cycle = first; cycle <= last; cycle++) foreach (var ev in clip.events)
        {
            float at = cycle * clip.length + ev.time;
            if (after > before ? at > before && at <= after : at >= after && at < before) Dispatch(ev);
        }
    }
    private void Dispatch(AnimationEvent ev)
    {
        bool found = false;
        foreach (var component in GetComponents<MonoBehaviour>())
            foreach (var method in component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != ev.functionName) continue;
                var args = method.GetParameters();
                if (args.Length == 0) { method.Invoke(component, null); found = true; }
                else if (args.Length == 1)
                {
                    Type type = args[0].ParameterType;
                    object value = type == typeof(float) ? ev.floatParameter : type == typeof(int) ? ev.intParameter : type == typeof(string) ? ev.stringParameter : type == typeof(AnimationEvent) ? ev : typeof(RimUnity.Object).IsAssignableFrom(type) ? ev.objectReferenceParameter : throw new NotSupportedException("动画事件参数类型不支持：" + type);
                    method.Invoke(component, new[] { value }); found = true;
                }
            }
        if (!found) throw new MissingMethodException("动画事件接收者不存在：" + ev.functionName);
    }
}
