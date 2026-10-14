using System;

namespace RimUnity;

//由原生帧入口赋值，模拟时间和真实帧时间相互独立。
public static class Time
{
    public static float time { get; private set; }
    public static float unscaledTime { get; private set; }
    public static float deltaTime { get; internal set; }
    public static float unscaledDeltaTime { get; private set; }
    public static float smoothDeltaTime { get; private set; }
    public static float realtimeSinceStartup => unscaledTime;
    public static double realtimeSinceStartupAsDouble => unscaledTime;
    public static int frameCount { get; private set; }
    public static float fixedDeltaTime { get; set; } = .02f;
    public static float maximumDeltaTime { get; set; } = 1f / 3f;
    private static float scale = 1;
    public static float timeScale { get => scale; set { if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); scale = value; } }
    internal static void Advance(float realDelta)
    {
        if (!float.IsFinite(realDelta) || realDelta < 0) throw new ArgumentOutOfRangeException(nameof(realDelta));
        if (!float.IsFinite(maximumDeltaTime) || maximumDeltaTime <= 0) throw new InvalidOperationException("maximumDeltaTime 必须为有限正数。");
        unscaledDeltaTime = realDelta; unscaledTime += realDelta;
        deltaTime = MathF.Min(realDelta * scale, maximumDeltaTime); time += deltaTime;
        smoothDeltaTime = frameCount == 0 ? deltaTime : smoothDeltaTime + (deltaTime - smoothDeltaTime) * .2f;
        frameCount++;
    }
}
