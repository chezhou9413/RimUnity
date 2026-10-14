using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //显式写入曲线和渐变的有效成员，避免序列化引擎内部指针。
    internal static class MathValueWriter
    {
        internal static ValueData Write(object value, ValueWriter writer, string path)
        {
            if (value is UnityEngine.EventSystems.EventTrigger.Entry) return Properties(value, writer, path, "eventID", "callback");
            if (value is UnityEngine.UI.AnimationTriggers) return Properties(value, writer, path, "normalTrigger", "highlightedTrigger", "pressedTrigger", "selectedTrigger", "disabledTrigger");
            if (value is AnimationCurve) return Properties(value, writer, path, "keys", "preWrapMode", "postWrapMode");
            if (value is Keyframe) return Properties(value, writer, path, "time", "value", "inTangent", "outTangent", "inWeight", "outWeight", "weightedMode");
            if (value is Gradient) return Properties(value, writer, path, "mode", "colorSpace", "colorKeys", "alphaKeys");
            if (value is GradientColorKey) return Properties(value, writer, path, "time", "color");
            if (value is GradientAlphaKey) return Properties(value, writer, path, "time", "alpha");
            if (value is ParticleSystem.MinMaxCurve curve) return Curve(curve, writer, path);
            if (value is ParticleSystem.MinMaxGradient gradient) return Gradient(gradient, writer, path);
            if (value is ParticleSystem.Burst) return Properties(value, writer, path, "time", "count", "cycleCount", "repeatInterval", "probability");
            if (value is BoneWeight) return Properties(value, writer, path, "boneIndex0", "boneIndex1", "boneIndex2", "boneIndex3", "weight0", "weight1", "weight2", "weight3");
            return null;
        }

        //原生粒子只保证当前模式的数据有效，不读取未使用的常量或曲线槽。
        private static ValueData Curve(ParticleSystem.MinMaxCurve value, ValueWriter writer, string path)
        {
            switch (value.mode)
            {
                case ParticleSystemCurveMode.Constant: return Properties(value, writer, path, "mode", "constantMax");
                case ParticleSystemCurveMode.TwoConstants: return Properties(value, writer, path, "mode", "constantMin", "constantMax");
                case ParticleSystemCurveMode.Curve: return Properties(value, writer, path, "mode", "curveMultiplier", "curveMax");
                case ParticleSystemCurveMode.TwoCurves: return Properties(value, writer, path, "mode", "curveMultiplier", "curveMin", "curveMax");
                default: throw new System.NotSupportedException(path + "：未知曲线模式 " + value.mode);
            }
        }

        //Gradient 模式的常量颜色槽可能含未初始化值，只保留真正参与计算的字段。
        private static ValueData Gradient(ParticleSystem.MinMaxGradient value, ValueWriter writer, string path)
        {
            switch (value.mode)
            {
                case ParticleSystemGradientMode.Color: return Properties(value, writer, path, "mode", "colorMax");
                case ParticleSystemGradientMode.TwoColors: return Properties(value, writer, path, "mode", "colorMin", "colorMax");
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor: return Properties(value, writer, path, "mode", "gradientMax");
                case ParticleSystemGradientMode.TwoGradients: return Properties(value, writer, path, "mode", "gradientMin", "gradientMax");
                default: throw new System.NotSupportedException(path + "：未知渐变模式 " + value.mode);
            }
        }
        internal static ValueData Properties(object value, ValueWriter writer, string path, params string[] names)
        {
            var data = new ValueData { kind = "data", fields = new System.Collections.Generic.List<MemberData>() };
            foreach (string name in names) data.fields.Add(writer.Property(value, name, path));
            return data;
        }
    }
}
