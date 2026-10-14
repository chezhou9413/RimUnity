using System;
using System.Collections.Generic;

namespace RimUnity;

//旋转按四元数组混合，先统一半球，避免等价的 q 与 -q 相互抵消。
public sealed partial class Animator
{
    private sealed class RotationBinding
    {
        internal Transform target;
        internal string path;
        internal Quaternion initial, reference, accumulated;
        internal Vector4 sum;
        internal float weight;
    }
    private sealed class RotationTrack
    {
        internal RotationBinding binding;
        internal readonly AnimationCurve[] curves = new AnimationCurve[4];
    }
    private readonly Dictionary<Transform, RotationBinding> rotationBindings = new();
    private readonly Dictionary<AnimationClip, List<RotationTrack>> clipRotations = new();

    //缺少分量的曲线保留绑定姿态对应分量。
    private void BindRotation(AnimationClip clip, AnimationTrack track, Transform target)
    {
        if (!rotationBindings.TryGetValue(target, out var binding))
            rotationBindings.Add(target, binding = new RotationBinding { target = target, path = track.path, initial = target.localRotation });
        if (!clipRotations.TryGetValue(clip, out var tracks)) clipRotations.Add(clip, tracks = new());
        RotationTrack group = tracks.Find(t => t.binding == binding);
        if (group == null) { group = new RotationTrack { binding = binding }; tracks.Add(group); }
        int index = "xyzw".IndexOf(track.property[^1]);
        if (index < 0) throw new NotSupportedException("旋转曲线分量无效：" + track.property);
        group.curves[index] = track.curve;
    }

    //混合树和 CrossFade 共用当前层的加权姿态。
    private void SampleRotations(AnimationClip clip, float time, float weight)
    {
        if (!clipRotations.TryGetValue(clip, out var tracks)) return;
        foreach (var track in tracks)
        {
            var binding = track.binding;
            Quaternion initial = binding.initial;
            Quaternion q = Normalize(new Vector4(track.curves[0]?.Evaluate(time) ?? initial.x, track.curves[1]?.Evaluate(time) ?? initial.y,
                track.curves[2]?.Evaluate(time) ?? initial.z, track.curves[3]?.Evaluate(time) ?? initial.w), initial);
            if (binding.weight == 0) binding.reference = q;
            float sign = Quaternion.Dot(binding.reference, q) < 0 ? -1 : 1;
            binding.sum += new Vector4(q.x, q.y, q.z, q.w) * (weight * sign);
            binding.weight += weight;
        }
    }

    //加法层应用相对绑定姿态的旋转增量，覆盖层使用最短弧插值。
    private void ApplyRotationLayer(LayerPlayback layer)
    {
        foreach (var binding in rotationBindings.Values)
        {
            if (binding.weight <= 0 || !Masked(layer.definition, binding.path)) continue;
            Quaternion initial = binding.initial;
            float missing = Mathf.Max(0, 1 - binding.weight) * (Quaternion.Dot(binding.reference, initial) < 0 ? -1 : 1);
            Quaternion sample = Normalize(binding.sum + new Vector4(initial.x, initial.y, initial.z, initial.w) * missing, initial);
            binding.accumulated = layer.definition.additive
                ? binding.accumulated * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(initial) * sample, layer.weight)
                : Quaternion.Slerp(binding.accumulated, sample, layer.weight);
        }
    }
    //无效的零四元数使用该曲线的绑定姿态，不产生 NaN 变换。
    private static Quaternion Normalize(Vector4 value, Quaternion initial)
    {
        float length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
        return length > .000001f ? new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length) : initial;
    }
}
