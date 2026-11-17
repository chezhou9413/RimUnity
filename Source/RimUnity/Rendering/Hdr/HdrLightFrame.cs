using System;
using System.Collections.Generic;
using System.Threading;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//Experiment：线性可适配材质进光缓冲，其余借用给宿主外壳帧。
internal sealed class HdrLightFrame : IDisposable
{
    internal readonly HdrDrawFrame Lights;
    internal readonly MeshDrawFrame Shell;
    private readonly List<(FinalDrawItem Draw, bool Light)> ordered = new();
    internal sealed class DrawRun(bool light)
    {
        internal readonly bool Light = light;
        internal readonly List<FinalDrawItem> Draws = new();
    }
    private int disposed;

    internal static bool Needed(MeshDrawFrame frame)
    {
        foreach (var draw in frame.Draws) if (HdrLightBlend.ShouldCapture(draw)) return true;
        foreach (var draw in frame.PersistentDraws) if (HdrLightBlend.ShouldCapture(draw)) return true;
        return false;
    }

    internal HdrLightFrame(MeshDrawFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Lights = new HdrDrawFrame(source, lightBuffer: true);
        try
        {
            Shell = HdrDrawFrame.Shell(source, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
            try { FillShell(source); }
            catch { Shell.PersistentDraws.Clear(); Shell.Dispose(); throw; }
        }
        catch { Lights.Dispose(); throw; }
    }

    //外壳只借用宿主绘制项，按宿主合并顺序放入持久列表。
    private void FillShell(MeshDrawFrame source)
    {
        var dynamic = new List<FinalDrawItem>(source.Draws.Count);
        foreach (var item in source.Draws) dynamic.Add(item);
        dynamic.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        var persistent = source.PersistentDraws;
        int p = 0, d = 0;
        while (p < persistent.Count || d < dynamic.Count)
        {
            var item = p < persistent.Count && (d == dynamic.Count || persistent[p].SortKey.CompareTo(dynamic[d].SortKey) <= 0)
                ? persistent[p++] : dynamic[d++];
            //按源对象找快照，不能用两次过滤排序后的下标配对（相同键排序不稳定）。
            bool light = HdrLightBlend.ShouldCapture(item);
            if (light) ordered.Add((Lights.LightDraws[item], true));
            else { Shell.PersistentDraws.Add(item); ordered.Add((item, false)); }
        }
    }

    internal bool HasLights(int queueMin, int queueMax)
    {
        foreach (var draw in Lights.Frame.Draws)
            if (draw.RenderQueue >= queueMin && draw.RenderQueue < queueMax) return true;
        return false;
    }

    //只合并相邻的同类绘制。光不能越过夜晚/透明/不透明层，否则新增曳光会改变远处已有光的亮度。
    internal List<DrawRun> Partition(int queueMin, int queueMax)
    {
        var runs = new List<DrawRun>();
        foreach (var item in ordered)
        {
            if (item.Draw.RenderQueue < queueMin || item.Draw.RenderQueue >= queueMax) continue;
            if (runs.Count == 0 || runs[^1].Light != item.Light) runs.Add(new DrawRun(item.Light));
            runs[^1].Draws.Add(item.Draw);
        }
        return runs;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            ordered.Clear();
            Shell.PersistentDraws.Clear();
            Shell.Dispose();
        }
        finally { Lights.Dispose(); }
    }
}
