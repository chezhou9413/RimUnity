using System;

namespace RimUnity;

//明文动画资源只包含属性曲线和事件，不保留编辑器控制器对象。
public sealed class AnimationClip : ScriptableObject
{
    public float length, frameRate = 60;
    public bool isLooping;
    public AnimationTrack[] tracks = Array.Empty<AnimationTrack>();
    public AnimationEvent[] events = Array.Empty<AnimationEvent>();
}

//路径相对于 Animator 所在对象；组件类型在导出端转换。
public sealed class AnimationTrack
{
    public string path, type, property;
    public AnimationCurve curve;
}

//事件参数类型与 Unity Inspector 的四种参数一致。
public sealed class AnimationEvent
{
    public float time, floatParameter;
    public string functionName, stringParameter;
    public int intParameter;
    public RimUnity.Object objectReferenceParameter;
}
