using System;

namespace RimUnity;

//将粒子动画的序列化路径绑定到实际参与模拟的自定义数据。
internal static class AnimatedParticleFloat
{
    //Custom1、Custom2 的四个分量分别保存 MinMaxCurve，写入后必须回填值类型。
    internal static bool TryBind(ParticleSystem system, string property, out Func<float> read, out Action<float> write)
    {
        read = null;
        write = null;
        if (!property.StartsWith("CustomDataModule.", StringComparison.Ordinal)) return false;
        string[] parts = property.Split('.');
        if (parts.Length != 3 || parts[1].Length != 9 || !parts[1].StartsWith("vector", StringComparison.Ordinal)
            || parts[1][6] < '1' || parts[1][6] > '2' || parts[1][7] != '_'
            || parts[1][8] < '0' || parts[1][8] > '3' || parts[2] is not ("scalar" or "minScalar"))
            throw new NotSupportedException("粒子自定义数据动画路径尚未支持：" + property);
        var stream = (ParticleSystemCustomData)(parts[1][6] - '1');
        int component = parts[1][8] - '0';
        bool minimum = parts[2] == "minScalar";
        read = () =>
        {
            var curve = system.customData.GetVector(stream, component);
            return minimum ? curve.constantMin : UsesCurve(curve) ? curve.curveMultiplier : curve.constantMax;
        };
        write = value =>
        {
            var curve = system.customData.GetVector(stream, component);
            if (minimum) curve.constantMin = value;
            else if (UsesCurve(curve)) curve.curveMultiplier = value;
            else curve.constantMax = value;
            system.customData.SetVector(stream, component, curve);
        };
        return true;
    }

    //Unity 的 scalar 在常量模式表示上界，在曲线模式表示整条曲线的倍率。
    private static bool UsesCurve(ParticleSystem.MinMaxCurve curve)
        => curve.mode is ParticleSystemCurveMode.Curve or ParticleSystemCurveMode.TwoCurves;
}
