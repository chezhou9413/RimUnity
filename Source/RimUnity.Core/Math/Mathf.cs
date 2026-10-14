using System;

namespace RimUnity;

//常用标量运算与阻尼公式。
public static class Mathf
{
    public const float PI = MathF.PI, Deg2Rad = PI / 180, Rad2Deg = 180 / PI, Infinity = float.PositiveInfinity;
    public const float Epsilon = float.Epsilon;
    //饱和三次插值，端点速度为零。
    public static float SmoothStep(float from, float to, float t)
    {
        t = Math.Clamp(t, 0, 1); return from + (to - from) * t * t * (3 - 2 * t);
    }
    public static float Abs(float x) => MathF.Abs(x);
    public static int Abs(int x) => Math.Abs(x);
    public static float Sin(float x) => MathF.Sin(x);
    public static float Cos(float x) => MathF.Cos(x);
    public static float Tan(float x) => MathF.Tan(x);
    public static float Asin(float x) => MathF.Asin(x);
    public static float Acos(float x) => MathF.Acos(x);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static float Sqrt(float x) => MathF.Sqrt(x);
    public static float Pow(float x, float p) => MathF.Pow(x, p);
    public static float Exp(float x) => MathF.Exp(x);
    public static float Log(float x) => MathF.Log(x);
    public static float Log(float x, float b) => MathF.Log(x, b);
    public static float Min(float a, float b) => MathF.Min(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static float Min(params float[] values) { float r = float.PositiveInfinity; foreach (float value in values) r = Min(r, value); return r; }
    public static float Max(float a, float b) => MathF.Max(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Max(params float[] values) { float r = float.NegativeInfinity; foreach (float value in values) r = Max(r, value); return r; }
    public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;
    public static int Clamp(int x, int min, int max) => x < min ? min : x > max ? max : x;
    public static float Clamp01(float x) => Clamp(x, 0, 1);
    public static float Lerp(float a, float b, float t) => LerpUnclamped(a, b, Clamp01(t));
    public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
    public static float InverseLerp(float a, float b, float value) => a == b ? 0 : Clamp01((value - a) / (b - a));
    public static float Floor(float x) => MathF.Floor(x);
    public static int FloorToInt(float x) => (int)MathF.Floor(x);
    public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
    public static float Round(float x) => MathF.Round(x);
    public static int RoundToInt(float x) => (int)MathF.Round(x);
    public static float Sign(float x) => x >= 0 ? 1 : -1;
    public static bool Approximately(float a, float b) => Abs(a - b) < Max(1e-6f * Max(Abs(a), Abs(b)), 8 * Epsilon);
    public static float Repeat(float value, float length) => Clamp(value - Floor(value / length) * length, 0, length);
    public static float PingPong(float value, float length) => length - Abs(Repeat(value, length * 2) - length);
    public static float DeltaAngle(float a, float b) { float d = Repeat(b - a, 360); return d > 180 ? d - 360 : d; }
    public static float MoveTowards(float current, float target, float maxDelta) => Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta;
    //临界阻尼的有理近似，限制最大速度并避免跨过目标。
    public static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float maxSpeed = Infinity, float deltaTime = -1)
    {
        if (deltaTime < 0) deltaTime = Time.deltaTime;
        if (deltaTime == 0) return current;
        smoothTime = Max(.0001f, smoothTime);
        float omega = 2 / smoothTime, x = omega * deltaTime, decay = 1 / (1 + x + .48f * x * x + .235f * x * x * x);
        float original = target, change = Clamp(current - target, -maxSpeed * smoothTime, maxSpeed * smoothTime);
        target = current - change;
        float step = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * step) * decay;
        float result = target + (change + step) * decay;
        if ((original - current > 0) == (result > original)) { result = original; velocity = 0; }
        return result;
    }
}
