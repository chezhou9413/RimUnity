using System;

namespace RimUnity;

public enum GradientMode { Blend, Fixed, PerceptualBlend }
public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }

//颜色和透明度使用独立关键点，保持粒子制作端的渐变数据。
public struct GradientColorKey
{
    public Color color;
    public float time;
    public GradientColorKey(Color color, float time) { this.color = color; this.time = time; }
}
public struct GradientAlphaKey
{
    public float alpha, time;
    public GradientAlphaKey(float alpha, float time) { this.alpha = alpha; this.time = time; }
}

//按时间插值颜色与透明度，固定模式在下一个关键点到达前保持左值。
public sealed class Gradient
{
    private GradientColorKey[] colors = { new(Color.white, 0), new(Color.white, 1) };
    private GradientAlphaKey[] alphas = { new(1, 0), new(1, 1) };
    public GradientMode mode { get; set; }
    public ColorSpace colorSpace { get; set; } = ColorSpace.Gamma;
    public GradientColorKey[] colorKeys { get => (GradientColorKey[])colors.Clone(); set { ArgumentNullException.ThrowIfNull(value); colors = (GradientColorKey[])value.Clone(); Array.Sort(colors, (a, b) => a.time.CompareTo(b.time)); } }
    public GradientAlphaKey[] alphaKeys { get => (GradientAlphaKey[])alphas.Clone(); set { ArgumentNullException.ThrowIfNull(value); alphas = (GradientAlphaKey[])value.Clone(); Array.Sort(alphas, (a, b) => a.time.CompareTo(b.time)); } }
    public void SetKeys(GradientColorKey[] colors, GradientAlphaKey[] alphas) { colorKeys = colors; alphaKeys = alphas; }
    public Color Evaluate(float time)
    {
        time = Mathf.Clamp01(time);
        Color value = colors.Length == 0 ? Color.white : colors[0].color;
        for (int i = 1; i < colors.Length; i++)
        {
            if (time >= colors[i].time) { value = colors[i].color; continue; }
            float t = Mathf.InverseLerp(colors[i - 1].time, colors[i].time, time);
            value = mode switch
            {
                GradientMode.Fixed => value,
                GradientMode.Blend => Color.Lerp(colors[i - 1].color, colors[i].color, t),
                GradientMode.PerceptualBlend => GradientColorInterpolation.Blend(colors[i - 1].color, colors[i].color, t, colorSpace),
                _ => throw new NotSupportedException("未知渐变模式：" + mode)
            };
            break;
        }
        float alpha = alphas.Length == 0 ? 1 : alphas[0].alpha;
        for (int i = 1; i < alphas.Length; i++)
        {
            if (time >= alphas[i].time) { alpha = alphas[i].alpha; continue; }
            if (mode != GradientMode.Fixed) alpha = Mathf.Lerp(alphas[i - 1].alpha, alphas[i].alpha, Mathf.InverseLerp(alphas[i - 1].time, alphas[i].time, time));
            break;
        }
        value.a = alpha; return value;
    }
}
