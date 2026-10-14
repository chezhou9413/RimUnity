using System;
using RimUnity;
using RimUnity.UI;

namespace Spine.Unity;

public delegate void UpdateBonesDelegate(ISkeletonAnimation animated);
//业务脚本在动画应用后、世界骨骼计算前修改局部姿态。
public interface ISkeletonAnimation { Skeleton Skeleton { get; } }

//使用 Spine 原生状态机、约束和蒙皮，画布只负责提交生成的三角形。
public sealed partial class SkeletonGraphic : MeshGraphic, ISkeletonAnimation
{
    public SkeletonDataAsset skeletonDataAsset;
    public Material additiveMaterial, multiplyMaterial, screenMaterial;
    public string initialSkinName, startingAnimation;
    public bool initialFlipX, initialFlipY, startingLoop, freeze, unscaledTime;
    public float timeScale = 1;
    public bool pmaVertexColors = true;
    public Vector2 pivotOffset;
    public Vector2 physicsPositionInheritanceFactor = Vector2.one;
    public float physicsRotationInheritanceFactor = 1;
    public Transform physicsMovementRelativeTo;
    private Skeleton skeleton;
    private AnimationState state;
    private Vector2 lastPosition;
    private float lastRotation;
    public SkeletonDataAsset SkeletonDataAsset => skeletonDataAsset;
    public Skeleton Skeleton { get { Initialize(false); return skeleton; } }
    public AnimationState AnimationState { get { Initialize(false); return state; } }
    public bool IsValid => skeleton != null;
    public float MeshScale => GetComponentInParent<Canvas>()?.referencePixelsPerUnit ?? 100;
    public Vector2 MeshOffset => pivotOffset;
    public event UpdateBonesDelegate BeforeApply, UpdateLocal, UpdateWorld, UpdateComplete;

    //初始化只发生于实例激活或显式访问骨骼。
    public void Initialize(bool overwrite)
    {
        if (skeleton != null && !overwrite) return;
        skeleton = new Skeleton(skeletonDataAsset.GetSkeletonData()) { ScaleX = initialFlipX ? -1 : 1, ScaleY = initialFlipY ? -1 : 1 };
        if (!string.IsNullOrEmpty(initialSkinName)) skeleton.SetSkin(initialSkinName);
        skeleton.SetSlotsToSetupPose();
        state = new AnimationState(skeletonDataAsset.GetAnimationStateData());
        if (!string.IsNullOrEmpty(startingAnimation)) state.SetAnimation(0, startingAnimation, startingLoop);
        state.Apply(skeleton);
        skeleton.UpdateWorldTransform(Skeleton.Physics.Update);
        ResetLastPositionAndRotation();
    }
    private void Awake() => Initialize(false);
    private void Update() { if (!freeze) Update(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime); }
    public void Update(float deltaTime)
    {
        Initialize(false);
        float elapsed = deltaTime * timeScale;
        state.Update(elapsed); skeleton.Update(elapsed);
        Vector2 position = PhysicsPosition();
        Vector2 delta = transform.InverseTransformVector((position - lastPosition) / MeshScale);
        if (physicsMovementRelativeTo) delta = physicsMovementRelativeTo.TransformVector(delta);
        skeleton.PhysicsTranslate(delta.x * physicsPositionInheritanceFactor.x, delta.y * physicsPositionInheritanceFactor.y);
        float rotation = PhysicsRotation();
        skeleton.PhysicsRotate(0, 0, physicsRotationInheritanceFactor * (rotation - lastRotation));
        lastPosition = position; lastRotation = rotation;
        BeforeApply?.Invoke(this);
        state.Apply(skeleton);
        UpdateLocal?.Invoke(this);
        if (UpdateWorld != null) { skeleton.UpdateWorldTransform(Skeleton.Physics.Pose); UpdateWorld(this); }
        skeleton.UpdateWorldTransform(Skeleton.Physics.Update);
        UpdateComplete?.Invoke(this);
    }
    //物理约束继承相对对象运动，避免首次激活时误判为大幅位移。
    public void ResetLastPositionAndRotation() { lastPosition = PhysicsPosition(); lastRotation = PhysicsRotation(); }
    private Vector2 PhysicsPosition() => physicsMovementRelativeTo ? physicsMovementRelativeTo.InverseTransformPoint(transform.position) : transform.position;
    private float PhysicsRotation() => physicsMovementRelativeTo ? (Quaternion.Inverse(physicsMovementRelativeTo.rotation) * transform.rotation).eulerAngles.z : transform.rotation.eulerAngles.z;
}
