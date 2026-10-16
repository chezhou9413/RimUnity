using System;

namespace RimUnity;

//Legacy 复用 Animator 的属性绑定和四元数采样，不维护第二套曲线解释器。
public sealed partial class Animator
{
    //Legacy 与状态机共用绑定结果，缺失目标的曲线不会反复查找或报错。
    internal void SampleLegacyClip(AnimationClip clip, float sampleTime)
    {
        BindMotion(new AnimationMotion { clip = clip });
        foreach (AnimationTrack track in clip.tracks)
            if (!track.property.StartsWith("m_LocalRotation.", StringComparison.Ordinal) && trackBindings[track] is { } binding)
                binding.write(track.curve.Evaluate(sampleTime));
        foreach (RotationBinding rotation in rotationBindings.Values) { rotation.sum = Vector4.zero; rotation.weight = 0; }
        SampleRotations(clip, sampleTime, 1);
        foreach (RotationBinding rotation in rotationBindings.Values)
            if (rotation.weight > 0) rotation.target.localRotation = Normalize(rotation.sum, rotation.initial);
    }
}
