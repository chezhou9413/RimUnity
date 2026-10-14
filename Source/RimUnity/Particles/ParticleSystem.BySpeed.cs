
namespace RimUnity;

//速度范围映射到曲线横轴，模块结果与生命周期模块相乘或相加。
public sealed partial class ParticleSystem
{
    public ColorBySpeedModule colorBySpeed = new();
    public SizeBySpeedModule sizeBySpeed = new();
    public RotationBySpeedModule rotationBySpeed = new();

    //低于和高于范围的速度分别使用曲线两端。
    private float SpeedPhase(Particle p, Vector2 range) => Mathf.InverseLerp(range.x, range.y, WorldVector(p.velocity + p.moduleVelocity, p.eventSpace).magnitude);

    //颜色由粒子实际速度决定，保持同一粒子的随机插值因子。
    public sealed class ColorBySpeedModule
    {
        public bool enabled;
        public Vector2 range = new(0, 1);
        public MinMaxGradient color = Color.white;
    }

    //独立坐标轴用于移动时拉长或压缩粒子。
    public sealed class SizeBySpeedModule
    {
        public bool enabled, separateAxes;
        public Vector2 range = new(0, 1);
        public MinMaxCurve size = 1, x = 1, y = 1, z = 1;
    }

    //曲线表示弧度角速度，积分时转换为公开粒子的角度值。
    public sealed class RotationBySpeedModule
    {
        public bool enabled, separateAxes;
        public Vector2 range = new(0, 1);
        public MinMaxCurve x = 0, y = 0, z = 0;
    }
}
