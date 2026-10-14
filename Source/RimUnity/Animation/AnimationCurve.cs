using System;

namespace RimUnity;

//保留阶梯切线、加权切线和首尾循环，供动画与粒子共用。
public sealed class AnimationCurve
{
    private Keyframe[] points = Array.Empty<Keyframe>();
    public WrapMode preWrapMode { get; set; } = WrapMode.ClampForever;
    public WrapMode postWrapMode { get; set; } = WrapMode.ClampForever;
    public int length => points.Length;
    public Keyframe this[int index] => points[index];
    public Keyframe[] keys { get => (Keyframe[])points.Clone(); set { points = (Keyframe[])value.Clone(); Array.Sort(points, (a, b) => a.time.CompareTo(b.time)); } }
    public AnimationCurve() { }
    public AnimationCurve(params Keyframe[] keys) { this.keys = keys; }

    //二分查找曲线段；加权曲线按时间反解三次贝塞尔参数。
    public float Evaluate(float time)
    {
        if (points.Length == 0) return 0;
        if (points.Length == 1) return points[0].value;
        float first = points[0].time, last = points[^1].time;
        if (last <= first) return points[^1].value;
        if (time < first || time > last)
        {
            WrapMode wrap = time < first ? preWrapMode : postWrapMode;
            time = wrap == WrapMode.Loop ? first + Mathf.Repeat(time - first, last - first)
                : wrap == WrapMode.PingPong ? first + Mathf.PingPong(time - first, last - first) : Mathf.Clamp(time, first, last);
        }
        if (time <= first) return points[0].value;
        if (time >= last) return points[^1].value;
        int lo = 0, hi = points.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (points[mid].time <= time) lo = mid; else hi = mid; }
        Keyframe a = points[lo], b = points[hi];
        if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent)) return a.value;
        float dt = b.time - a.time, x = (time - a.time) / dt;
        float wa = (a.weightedMode & WeightedMode.Out) != 0 ? a.outWeight : 1f / 3;
        float wb = (b.weightedMode & WeightedMode.In) != 0 ? b.inWeight : 1f / 3;
        float u = x;
        if (wa != 1f / 3 || wb != 1f / 3)
        {
            float low = 0, high = 1;
            for (int i = 0; i < 20; i++) { u = (low + high) * .5f; if (Bezier(0, wa, 1 - wb, 1, u) < x) low = u; else high = u; }
        }
        return Bezier(a.value, a.value + a.outTangent * dt * wa, b.value - b.inTangent * dt * wb, b.value, u);
    }

    //关键点编辑会维持时间顺序。
    public int AddKey(Keyframe key) { var list = new System.Collections.Generic.List<Keyframe>(points) { key }; keys = list.ToArray(); return Array.FindIndex(points, k => k.time == key.time); }
    public int AddKey(float time, float value) => AddKey(new Keyframe(time, value));
    public void RemoveKey(int index) { var list = new System.Collections.Generic.List<Keyframe>(points); list.RemoveAt(index); points = list.ToArray(); }
    public int MoveKey(int index, Keyframe key) { RemoveKey(index); return AddKey(key); }
    public static AnimationCurve Constant(float start, float end, float value) => new(new Keyframe(start, value), new Keyframe(end, value));
    public static AnimationCurve Linear(float start, float a, float end, float b) { float slope = (b - a) / (end - start); return new(new Keyframe(start, a, slope, slope), new Keyframe(end, b, slope, slope)); }
    public static AnimationCurve EaseInOut(float start, float a, float end, float b) => new(new Keyframe(start, a), new Keyframe(end, b));
    private static float Bezier(float a, float b, float c, float d, float t) { float s = 1 - t; return s * s * s * a + 3 * s * s * t * b + 3 * s * t * t * c + t * t * t * d; }
}
