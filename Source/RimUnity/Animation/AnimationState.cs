
namespace RimUnity;

//Legacy 状态保存每段剪辑独立的时间、速度和循环方式。
public sealed class AnimationState
{
    public string name;
    public AnimationClip clip;
    public bool enabled;
    public float time, speed = 1, weight = 1;
    public int layer;
    public WrapMode wrapMode;
    public float length => clip?.length ?? 0;
    public float normalizedTime { get => length > 0 ? time / length : 0; set => time = value * length; }
    public float normalizedSpeed { get => length > 0 ? speed / length : 0; set => speed = value * length; }
}
