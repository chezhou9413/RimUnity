using System;

namespace RimUnity;

//资源状态不可变，参数值与当前状态只存放在 Animator 实例。
public sealed class RuntimeAnimatorController : ScriptableObject
{
    public AnimatorControllerParameter[] parameters = Array.Empty<AnimatorControllerParameter>();
    public AnimatorLayer[] layers = Array.Empty<AnimatorLayer>();
}
public enum AnimatorControllerParameterType { Float = 1, Int = 3, Bool = 4, Trigger = 9 }
public enum AnimatorUpdateMode { Normal, AnimatePhysics, UnscaledTime }

//参数的默认值来自编辑器 Controller。
public sealed class AnimatorControllerParameter
{
    public string name;
    public AnimatorControllerParameterType type;
    public float defaultFloat;
    public int defaultInt;
    public bool defaultBool;
    public int nameHash => Animator.StringToHash(name);
}
//骨骼遮罩导出 Transform 路径，空数组表示所有骨骼。
public sealed class AnimatorLayer
{
    public string name, defaultState;
    public float defaultWeight = 1;
    public bool additive;
    public AnimatorMaskPath[] mask = Array.Empty<AnimatorMaskPath>();
    public AnimatorState[] states = Array.Empty<AnimatorState>();
    public AnimatorTransition[] anyTransitions = Array.Empty<AnimatorTransition>();
}
public sealed class AnimatorMaskPath { public string path; public bool active; }
//状态速度、循环偏移和可选参数驱动。
public sealed class AnimatorState
{
    public string name, tag, speedParameter;
    public float speed = 1, cycleOffset;
    public AnimationMotion motion;
    public AnimatorTransition[] transitions = Array.Empty<AnimatorTransition>();
}
//clip 与一维混合树共用一棵可求值的运动树。
public sealed class AnimationMotion
{
    public AnimationClip clip;
    public string parameter;
    public MotionChild[] children = Array.Empty<MotionChild>();
}
public sealed class MotionChild { public float threshold, timeScale = 1, cycleOffset; public AnimationMotion motion; }
//过渡按源状态的顺序检查，条件列表表示逻辑与。
public sealed class AnimatorTransition
{
    public string destination;
    public bool hasExitTime, fixedDuration = true, canTransitionToSelf;
    public float exitTime, duration, offset;
    public AnimatorCondition[] conditions = Array.Empty<AnimatorCondition>();
}
public sealed class AnimatorCondition { public string parameter; public int mode; public float threshold; }
//常用状态查询结果，哈希只需在同一桥接运行时内一致。
public struct AnimatorStateInfo
{
    public int shortNameHash, fullPathHash, tagHash;
    public float normalizedTime, length, speed;
    public bool loop;
    public bool IsName(string name) => shortNameHash == Animator.StringToHash(name) || fullPathHash == Animator.StringToHash(name);
    public bool IsTag(string tag) => tagHash == Animator.StringToHash(tag);
}
