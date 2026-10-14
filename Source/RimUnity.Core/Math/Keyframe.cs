namespace RimUnity;

[System.Flags]
public enum WeightedMode { None, In, Out, Both }
public enum WrapMode { Default = 0, Once = 1, Loop = 2, PingPong = 4, ClampForever = 8, Clamp = 1 }

//动画曲线保留左右切线和各自权重。
public struct Keyframe
{
    public float time, value, inTangent, outTangent, inWeight, outWeight;
    public WeightedMode weightedMode;
    public Keyframe(float time, float value) : this(time, value, 0, 0) { }
    public Keyframe(float time, float value, float inTangent, float outTangent)
    { this.time = time; this.value = value; this.inTangent = inTangent; this.outTangent = outTangent; inWeight = outWeight = 1f / 3; weightedMode = WeightedMode.None; }
    public Keyframe(float time, float value, float inTangent, float outTangent, float inWeight, float outWeight)
        : this(time, value, inTangent, outTangent) { this.inWeight = inWeight; this.outWeight = outWeight; weightedMode = WeightedMode.Both; }
}
