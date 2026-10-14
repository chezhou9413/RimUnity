
namespace RimUnity;

//粒子值类型与模块沿用 Unity 的名称和赋值习惯。
public sealed partial class ParticleSystem
{
    //曲线的随机混合因子由粒子种子固定，不随帧重新抽样。
    public struct MinMaxCurve
    {
        public ParticleSystemCurveMode mode;
        public float constantMin, constantMax, curveMultiplier;
        public AnimationCurve curveMin, curveMax;
        public float constant { get => constantMax; set { constantMin = constantMax = value; } }
        public AnimationCurve curve { get => curveMax; set => curveMax = value; }
        public MinMaxCurve(float value) { this = default; constant = value; curveMultiplier = 1; }
        public MinMaxCurve(float min, float max) : this(max) { mode = ParticleSystemCurveMode.TwoConstants; constantMin = min; }
        public MinMaxCurve(float multiplier, AnimationCurve curve) : this(0) { mode = ParticleSystemCurveMode.Curve; curveMultiplier = multiplier; curveMax = curve; }
        public MinMaxCurve(float multiplier, AnimationCurve min, AnimationCurve max) : this(multiplier, max) { mode = ParticleSystemCurveMode.TwoCurves; curveMin = min; }
        public float Evaluate(float time, float factor = 1) => mode switch
        {
            ParticleSystemCurveMode.Curve => curveMax.Evaluate(time) * curveMultiplier,
            ParticleSystemCurveMode.TwoCurves => Mathf.Lerp(curveMin.Evaluate(time), curveMax.Evaluate(time), factor) * curveMultiplier,
            ParticleSystemCurveMode.TwoConstants => Mathf.Lerp(constantMin, constantMax, factor),
            _ => constantMax
        };
        public static implicit operator MinMaxCurve(float value) => new(value);
    }

    //渐变使用原生色键，随机颜色模式在出生时选定位置。
    public struct MinMaxGradient
    {
        public ParticleSystemGradientMode mode;
        public Color colorMin, colorMax;
        public Gradient gradientMin, gradientMax;
        public Color color { get => colorMax; set => colorMax = value; }
        public Gradient gradient { get => gradientMax; set => gradientMax = value; }
        public MinMaxGradient(Color color) { this = default; colorMin = colorMax = color; }
        public MinMaxGradient(Gradient gradient) { this = default; mode = ParticleSystemGradientMode.Gradient; gradientMax = gradient; }
        public MinMaxGradient(Color min, Color max) : this(max) { mode = ParticleSystemGradientMode.TwoColors; colorMin = min; }
        public MinMaxGradient(Gradient min, Gradient max) : this(max) { mode = ParticleSystemGradientMode.TwoGradients; gradientMin = min; }
        public Color Evaluate(float time, float factor = 1) => mode switch
        {
            ParticleSystemGradientMode.Gradient => gradientMax.Evaluate(time),
            ParticleSystemGradientMode.TwoGradients => Color.Lerp(gradientMin.Evaluate(time), gradientMax.Evaluate(time), factor),
            ParticleSystemGradientMode.TwoColors => Color.Lerp(colorMin, colorMax, factor),
            ParticleSystemGradientMode.RandomColor => gradientMax.Evaluate(factor),
            _ => colorMax
        };
        public static implicit operator MinMaxGradient(Color color) => new(color);
        public static implicit operator MinMaxGradient(Gradient gradient) => new(gradient);
    }

    //公开快照可用 GetParticles 和 SetParticles 往返修改。
    public struct Particle
    {
        internal ulong identity;
        internal float birthOffset, elapsedAge;
        internal Vector3 moduleVelocity;
        internal Vector3 initialEmitterVelocity;
        internal EventSpace eventSpace;
        internal bool alignedToDirection, physicsKilled;
        internal int? meshIndexOverride;
        public Vector3 position, velocity, rotation3D, angularVelocity3D, startSize3D;
        public Color32 startColor;
        public float remainingLifetime, startLifetime;
        public uint randomSeed;
        public Vector3 animatedVelocity => moduleVelocity;
        public Vector3 totalVelocity => velocity + moduleVelocity;
        public float startSize { get => startSize3D.x; set => startSize3D = Vector3.one * value; }
        public float rotation { get => rotation3D.z; set => rotation3D = new Vector3(0, 0, value); }
        public float angularVelocity { get => angularVelocity3D.z; set => angularVelocity3D = new Vector3(0, 0, value); }
        public Color32 GetCurrentColor(ParticleSystem system) => system.ColorAt(this);
        public float GetCurrentSize(ParticleSystem system) => system.SizeAt(this).x;
        public Vector3 GetCurrentSize3D(ParticleSystem system) => system.SizeAt(this);
        public int GetMeshIndex(ParticleSystemRenderer renderer) => renderer.MeshIndex(this);
        public void SetMeshIndex(int index) => meshIndexOverride = index;
    }

    //发射覆盖值区分零值与未设置值。
    public struct EmitParams
    {
        internal Vector3? p, v, s, r, angular; internal Color32? c; internal float? life; internal uint? seed;
        internal float? sampleTime, inheritedLifetime, scheduledOffset;
        internal Color? inheritedColor;
        internal Vector3? inheritedSize, inheritedRotation;
        internal EventSpace eventSpace;
        internal Vector3? emitterWorld;
        public bool applyShapeToPosition;
        public Vector3 position { get => p ?? default; set => p = value; }
        public Vector3 velocity { get => v ?? default; set => v = value; }
        public Vector3 startSize3D { get => s ?? default; set => s = value; }
        public float startSize { get => startSize3D.x; set => s = Vector3.one * value; }
        public Vector3 rotation3D { get => r ?? default; set => r = value; }
        public Vector3 angularVelocity3D { get => angular ?? default; set => angular = value; }
        public float angularVelocity { get => angularVelocity3D.z; set => angular = new Vector3(0, 0, value); }
        public float rotation { get => rotation3D.z; set => r = new Vector3(0, 0, value); }
        public Color32 startColor { get => c ?? default; set => c = value; }
        public float startLifetime { get => life ?? 0; set => life = value; }
        public uint randomSeed { get => seed ?? 0; set => seed = value; }
        public Particle particle
        {
            get => new() { position = position, velocity = velocity, startSize3D = startSize3D, rotation3D = rotation3D, angularVelocity3D = angularVelocity3D, startColor = startColor, startLifetime = startLifetime, randomSeed = randomSeed };
            set { position = value.position; velocity = value.velocity; startSize3D = value.startSize3D; rotation3D = value.rotation3D; angularVelocity3D = value.angularVelocity3D; startColor = value.startColor; startLifetime = value.startLifetime; randomSeed = value.randomSeed; }
        }
        public void ResetPosition() => p = null;
        public void ResetVelocity() => v = null;
        public void ResetStartSize() => s = null;
        public void ResetStartColor() => c = null;
        public void ResetStartLifetime() => life = null;
        public void ResetRandomSeed() => seed = null;
        public void ResetRotation() => r = null;
        public void ResetAngularVelocity() => angular = null;
    }

    //Burst 的循环次数和概率在每个系统循环内分别计算。
    public struct Burst
    {
        public float time, repeatInterval;
        private float inverseProbability;
        private int repeats;
        public float probability { get => 1 - inverseProbability; set => inverseProbability = 1 - value; }
        public int cycleCount { get => repeats + 1; set => repeats = value - 1; }
        public short minCount { get => (short)count.constantMin; set => count.constantMin = value; }
        public short maxCount { get => (short)count.constantMax; set => count.constantMax = value; }
        public MinMaxCurve count;
        public Burst(float time, short count) : this(time, new MinMaxCurve(count)) { }
        public Burst(float time, MinMaxCurve count) { this = default; this.time = time; this.count = count; }
        public Burst(float time, short minCount, short maxCount) : this(time, new MinMaxCurve(minCount, maxCount)) { }
        public Burst(float time, short minCount, short maxCount, int cycleCount, float repeatInterval) : this(time, new MinMaxCurve(minCount, maxCount), cycleCount, repeatInterval) { }
        public Burst(float time, MinMaxCurve count, int cycleCount, float repeatInterval) : this(time, count) { this.cycleCount = cycleCount; this.repeatInterval = repeatInterval; }
    }
}
