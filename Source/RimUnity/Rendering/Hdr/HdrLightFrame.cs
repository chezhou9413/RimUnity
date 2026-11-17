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
            if (p < persistent.Count && HdrLightBlend.ShouldCapture(persistent[p])) { p++; continue; }
            if (d < dynamic.Count && HdrLightBlend.ShouldCapture(dynamic[d])) { d++; continue; }
            var item = p < persistent.Count && (d == dynamic.Count || persistent[p].SortKey.CompareTo(dynamic[d].SortKey) <= 0)
                ? persistent[p++] : dynamic[d++];
            Shell.PersistentDraws.Add(item);
        }
    }

    internal bool HasLights(int queueMin, int queueMax)
    {
        foreach (var draw in Lights.Frame.Draws)
            if (draw.RenderQueue >= queueMin && draw.RenderQueue < queueMax) return true;
        return false;
    }

    //本段内：排在第一盏光之前的外壳由宿主先画；之后的外壳（夜晚/迷雾）等光合成后再画一次。
    internal void Partition(int queueMin, int queueMax, List<FinalDrawItem> beforeLights, List<FinalDrawItem> afterLights)
    {
        beforeLights.Clear(); afterLights.Clear();
        var shell = new List<FinalDrawItem>();
        var lights = new List<FinalDrawItem>();
        foreach (var draw in Shell.PersistentDraws)
            if (draw.RenderQueue >= queueMin && draw.RenderQueue < queueMax) shell.Add(draw);
        foreach (var draw in Lights.Frame.Draws)
            if (draw.RenderQueue >= queueMin && draw.RenderQueue < queueMax) lights.Add(draw);
        if (lights.Count == 0) { beforeLights.AddRange(shell); return; }
        var merged = new List<(FinalDrawItem draw, bool light)>(shell.Count + lights.Count);
        foreach (var draw in shell) merged.Add((draw, false));
        foreach (var draw in lights) merged.Add((draw, true));
        merged.Sort((a, b) => a.draw.SortKey.CompareTo(b.draw.SortKey));
        bool seenLight = false;
        foreach (var item in merged)
        {
            if (item.light) { seenLight = true; continue; }
            if (seenLight) afterLights.Add(item.draw);
            else beforeLights.Add(item.draw);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            Shell.PersistentDraws.Clear();
            Shell.Dispose();
        }
        finally { Lights.Dispose(); }
    }
}
