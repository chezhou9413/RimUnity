using System;

namespace RimUnity;

//每个实例独立持有模块，脚本修改不会改变资源模板。
public sealed partial class ParticleSystem
{
    public MainModule main = new();
    public EmissionModule emission = new();
    public ShapeModule shape = new();
    public VelocityOverLifetimeModule velocityOverLifetime = new();
    public ForceOverLifetimeModule forceOverLifetime = new();
    public InheritVelocityModule inheritVelocity = new();
    public ExternalForcesModule externalForces = new();
    public ColorOverLifetimeModule colorOverLifetime = new();
    public SizeOverLifetimeModule sizeOverLifetime = new();
    public RotationOverLifetimeModule rotationOverLifetime = new();
    public LimitVelocityOverLifetimeModule limitVelocityOverLifetime = new();
    public NoiseModule noise = new();
    public TextureSheetAnimationModule textureSheetAnimation = new();
    public TrailsModule trails = new();
    public LifetimeByEmitterSpeedModule lifetimeByEmitterSpeed = new();

    //按发射器出生时的真实速度缩放初始寿命。
    public sealed class LifetimeByEmitterSpeedModule
    {
        public bool enabled;
        public MinMaxCurve curve = 1;
        public Vector2 range = new(0, 1);
    }

    //系统时钟、出生属性和空间选择。
    public sealed class MainModule
    {
        public float duration = 5, simulationSpeed = 1, flipRotation;
        public bool loop = true, prewarm, playOnAwake = true, useUnscaledTime, startSize3D, startRotation3D;
        public int maxParticles = 1000;
        public MinMaxCurve startDelay = 0, startLifetime = 5, startSpeed = 5, startSize = 1, startSizeX = 1, startSizeY = 1, startSizeZ = 1;
        public MinMaxCurve startRotation = 0, startRotationX = 0, startRotationY = 0, startRotationZ = 0, gravityModifier = 0;
        public MinMaxGradient startColor = Color.white;
        public ParticleSystemSimulationSpace simulationSpace;
        public ParticleSystemEmitterVelocityMode emitterVelocityMode;
        public Vector3 emitterVelocity;
        public ParticleSystemScalingMode scalingMode;
        public Transform customSimulationSpace;
        public ParticleSystemStopAction stopAction;
        public ParticleSystemRingBufferMode ringBufferMode;
        public Vector2 ringBufferLoopRange = new(0, 1);
    }
    //发射曲线与爆发队列。
    public sealed class EmissionModule
    {
        public bool enabled = true;
        public MinMaxCurve rateOverTime = 10, rateOverDistance = 0;
        //常量模式的倍率对应常量值，曲线模式对应导出的曲线幅度。
        public float rateOverTimeMultiplier { get => EmissionMultiplier(rateOverTime); set => SetEmissionMultiplier(ref rateOverTime, value); }
        public float rateOverDistanceMultiplier { get => EmissionMultiplier(rateOverDistance); set => SetEmissionMultiplier(ref rateOverDistance, value); }
        private static float EmissionMultiplier(MinMaxCurve curve) => curve.mode is ParticleSystemCurveMode.Constant or ParticleSystemCurveMode.TwoConstants ? curve.constantMax : curve.curveMultiplier;
        private static void SetEmissionMultiplier(ref MinMaxCurve curve, float value)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant) curve.constant = value;
            else if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                curve.constantMin = curve.constantMax == 0 ? 0 : curve.constantMin * value / curve.constantMax;
                curve.constantMax = value;
            }
            else curve.curveMultiplier = value;
        }
        public Burst[] bursts = Array.Empty<Burst>();
        public int burstCount { get => bursts.Length; set => Array.Resize(ref bursts, value); }
        public void SetBursts(Burst[] value) => SetBursts(value, value.Length);
        public void SetBursts(Burst[] value, int size)
        { if (size < 0 || size > value.Length) throw new ArgumentOutOfRangeException(nameof(size)); bursts = new Burst[size]; Array.Copy(value, bursts, size); }
        public int GetBursts(Burst[] target) { int n = Math.Min(target.Length, bursts.Length); Array.Copy(bursts, target, n); return n; }
        public Burst GetBurst(int index) => bursts[index];
        public void SetBurst(int index, Burst burst) => bursts[index] = burst;
    }
    //常用体积与圆锥出生区域。
    public sealed class ShapeModule
    {
        public bool enabled = true, alignToDirection, useMeshColors = true, useMeshMaterialIndex;
        public Mesh mesh;
        public Sprite sprite;
        public MeshRenderer meshRenderer;
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public SpriteRenderer spriteRenderer;
        public ParticleSystemMeshShapeType meshShapeType;
        public int meshMaterialIndex;
        public float normalOffset, meshSpawnSpread;
        public ParticleSystemShapeMultiModeValue meshSpawnMode;
        public MinMaxCurve meshSpawnSpeed = 1;
        public Texture2D texture;
        public bool textureColorAffectsParticles = true, textureAlphaAffectsParticles = true, textureBilinearFiltering;
        public int textureClipChannel = 3, textureUVChannel;
        public float textureClipThreshold;
        public Vector3[] spriteVertices = Array.Empty<Vector3>();
        public int[] spriteTriangles = Array.Empty<int>();
        public ParticleSystemShapeType shapeType = ParticleSystemShapeType.Cone;
        public float radius = 1, radiusThickness = 1, angle = 25, length = 5, arc = 360, randomDirectionAmount, sphericalDirectionAmount;
        public float donutRadius = .2f, arcSpread, radiusSpread, randomPositionAmount;
        public ParticleSystemShapeMultiModeValue arcMode, radiusMode;
        public MinMaxCurve arcSpeed = 1, radiusSpeed = 1;
        public Vector3 position, rotation, boxThickness, scale = Vector3.one;
    }
    //速度采用每帧偏移量，不累计叠加为加速度。
    public sealed class VelocityOverLifetimeModule
    {
        public bool enabled;
        public MinMaxCurve x = 0, y = 0, z = 0, speedModifier = 1;
        public MinMaxCurve orbitalX = 0, orbitalY = 0, orbitalZ = 0, orbitalOffsetX = 0, orbitalOffsetY = 0, orbitalOffsetZ = 0, radial = 0;
        public ParticleSystemSimulationSpace space;
    }
    //力按时间积分到粒子的基础速度。
    public sealed class ForceOverLifetimeModule
    {
        public bool enabled, randomized;
        public MinMaxCurve x = 0, y = 0, z = 0;
        public ParticleSystemSimulationSpace space;
    }
    //世界空间粒子按出生源速度或当前源速度继承，曲线横轴为粒子寿命。
    public sealed class InheritVelocityModule
    {
        public bool enabled;
        public ParticleSystemInheritVelocityMode mode;
        public MinMaxCurve curve = 0;
    }
    //按 Layer 或显式引用筛选场景中的力场与风区。
    public sealed partial class ExternalForcesModule
    {
        public bool enabled;
        public MinMaxCurve multiplierCurve = 1;
        public int influenceFilter, influenceMask = -1;
    }
    //颜色在绘制时按年龄求值。
    public sealed class ColorOverLifetimeModule { public bool enabled; public MinMaxGradient color = Color.white; }
    //尺寸支持独立坐标轴。
    public sealed class SizeOverLifetimeModule { public bool enabled, separateAxes; public MinMaxCurve size = 1, x = 1, y = 1, z = 1; }
    //旋转曲线表示角速度，内部以弧度转换为公开的角度值。
    public sealed class RotationOverLifetimeModule { public bool enabled, separateAxes; public MinMaxCurve x = 0, y = 0, z = 0; }
    //阻力和限速按时间连续衰减。
    public sealed class LimitVelocityOverLifetimeModule
    {
        public bool enabled, separateAxes, multiplyDragByParticleSize, multiplyDragByParticleVelocity;
        public float dampen;
        public MinMaxCurve limit = 1, limitX = 1, limitY = 1, limitZ = 1, drag = 0;
        public ParticleSystemSimulationSpace space;
    }
    //确定性分形噪声；数值场与 Unity 原生实现存在差异。
    public sealed class NoiseModule
    {
        public bool enabled, separateAxes, damping = true, remapEnabled;
        public MinMaxCurve remap = new(1, AnimationCurve.Linear(0, -1, 1, 1)), remapX = new(1, AnimationCurve.Linear(0, -1, 1, 1)), remapY = new(1, AnimationCurve.Linear(0, -1, 1, 1)), remapZ = new(1, AnimationCurve.Linear(0, -1, 1, 1));
        public float frequency = .5f, octaveMultiplier = .5f, octaveScale = 2;
        public int octaveCount = 1;
        public ParticleSystemNoiseQuality quality = ParticleSystemNoiseQuality.High;
        public MinMaxCurve strength = 1, strengthX = 1, strengthY = 1, strengthZ = 1, scrollSpeed = 0, positionAmount = 1, rotationAmount = 0, sizeAmount = 0;
    }
    //网格图集按寿命曲线、速度范围或固定帧率选帧。
    public sealed partial class TextureSheetAnimationModule
    {
        public bool enabled;
        public int numTilesX = 1, numTilesY = 1, cycleCount = 1, rowIndex;
        public float fps = 30;
        public Vector2 speedRange = new(0, 1);
        public ParticleSystemAnimationMode mode;
        public ParticleSystemAnimationTimeMode timeMode;
        public ParticleSystemAnimationType animation;
        public ParticleSystemAnimationRowMode rowMode;
        public MinMaxCurve frameOverTime = new(1, AnimationCurve.Linear(0, 0, 1, 1)), startFrame = 0;
    }
    //每个粒子保留自己的轨迹采样队列。
    public sealed class TrailsModule
    {
        public bool enabled, dieWithParticles = true, worldSpace, sizeAffectsWidth = true, sizeAffectsLifetime, inheritParticleColor = true;
        public ParticleSystemTrailMode mode;
        public int ribbonCount = 1;
        public bool splitSubEmitterRibbons;
        public float ratio = 1, minVertexDistance = .2f;
        public MinMaxCurve lifetime = 1, widthOverTrail = 1;
        public MinMaxGradient colorOverLifetime = Color.white, colorOverTrail = Color.white;
        public ParticleSystemTrailTextureMode textureMode;
    }
}
