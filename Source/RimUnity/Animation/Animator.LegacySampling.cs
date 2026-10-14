using System;

namespace RimUnity;

//Legacy 复用 Animator 的属性绑定和四元数采样，不维护第二套曲线解释器。
public sealed partial class Animator
{
    internal void SampleLegacyClip(AnimationClip clip, float sampleTime)
    {
        BindMotion(new AnimationMotion { clip = clip });
        foreach (AnimationTrack track in clip.tracks)
            if (!track.property.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) trackBindings[track].write(track.curve.Evaluate(sampleTime));
        foreach (RotationBinding rotation in rotationBindings.Values) { rotation.sum = Vector4.zero; rotation.weight = 0; }
        SampleRotations(clip, sampleTime, 1);
        foreach (RotationBinding rotation in rotationBindings.Values)
            if (rotation.weight > 0) rotation.target.localRotation = Normalize(rotation.sum, rotation.initial);
    }
}
