using Verse;

namespace RimUnity;

//地图动画跟随实际游戏 Tick，菜单动画可选择真实时间。
internal sealed class BridgeTime
{
    private int previousTick = -1;
    internal float Step(bool unscaled)
    {
        int tick = Current.ProgramState == ProgramState.Playing ? Find.TickManager.TicksGame : -1;
        float delta = unscaled ? Time.unscaledDeltaTime : tick >= 0 && previousTick >= 0 ? Mathf.Max(0, tick - previousTick) / 60f : tick < 0 ? Time.deltaTime : 0;
        previousTick = tick;
        return delta;
    }
    internal void Reset() => previousTick = Current.ProgramState == ProgramState.Playing ? Find.TickManager.TicksGame : -1;
}
