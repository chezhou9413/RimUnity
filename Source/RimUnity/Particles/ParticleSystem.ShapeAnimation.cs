using System;

namespace RimUnity;

//形状模式枚举值与制作端一致。
public enum ParticleSystemShapeMultiModeValue { Random, Loop, PingPong, BurstSpread }

//形状相位由实际出生时刻求值，不随帧切分或发射数量改变旋转速度。
public sealed partial class ParticleSystem
{
    //圆弧速度以每秒完整 360 度计；折返与量化在实际圆弧范围内执行。
    private float ShapePhase(ParticleSystemShapeMultiModeValue mode, MinMaxCurve speed, float spread,
        float seconds, int index, int number, float extent, float speedUnit, bool closed = true)
    {
        if (extent <= 0) return 0;
        float value;
        switch (mode)
        {
            case ParticleSystemShapeMultiModeValue.Random:
                value = Random01(); break;
            case ParticleSystemShapeMultiModeValue.Loop:
                value = Mathf.Repeat(ShapeTravel(speed, seconds) * speedUnit / extent, 1); break;
            case ParticleSystemShapeMultiModeValue.PingPong:
                value = Mathf.PingPong(ShapeTravel(speed, seconds) * speedUnit / extent, 1); break;
            case ParticleSystemShapeMultiModeValue.BurstSpread:
                bool wraps = closed && extent >= speedUnit;
                value = number > 1 ? index / (float)(wraps ? number : number - 1) : 0;
                break;
            default: throw new NotSupportedException("不支持的形状发射模式：" + mode);
        }
        return spread > 0 ? Mathf.Floor(value / spread) * spread : value;
    }

    //常量直接积分；曲线按系统周期累计，不能用当前速度乘整个经过时间。
    private float ShapeTravel(MinMaxCurve speed, float seconds)
    {
        seconds = Math.Max(0, seconds);
        float random = Random(randomSeed, 142);
        if (speed.mode == ParticleSystemCurveMode.Constant || speed.mode == ParticleSystemCurveMode.TwoConstants)
            return speed.Evaluate(0, random) * seconds;
        if (main.duration <= 0) throw new InvalidOperationException("形状速度曲线要求正的系统时长。");
        float cycles = seconds / main.duration;
        if (!main.loop && cycles > 1)
            return CurveArea(speed, 1, random) * main.duration + speed.Evaluate(1, random) * (seconds - main.duration);
        float full = Mathf.Floor(cycles);
        return (CurveArea(speed, 1, random) * full + CurveArea(speed, cycles - full, random)) * main.duration;
    }

    //按关键点拆分区间，两点 Gauss 积分精确覆盖普通三次 Hermite 段。
    private static float CurveArea(MinMaxCurve curve, float end, float random)
    {
        if (end <= 0) return 0;
        float area = 0, from = 0;
        Keyframe[] min = curve.curveMin?.keys ?? Array.Empty<Keyframe>();
        Keyframe[] max = curve.curveMax?.keys ?? Array.Empty<Keyframe>();
        while (from < end)
        {
            float to = end;
            foreach (Keyframe key in min) if (key.time > from && key.time < to) to = key.time;
            foreach (Keyframe key in max) if (key.time > from && key.time < to) to = key.time;
            //加权切线不一定是时间上的三次多项式，细分后仍按真实 Evaluate 采样。
            const int intervals = 8;
            float halfStep = (to - from) / (2 * intervals);
            for (int i = 0; i < intervals; i++)
            {
                float center = from + halfStep * (2 * i + 1), offset = halfStep * .57735026919f;
                area += halfStep * (curve.Evaluate(center - offset, random) + curve.Evaluate(center + offset, random));
            }
            from = to;
        }
        return area;
    }
}
