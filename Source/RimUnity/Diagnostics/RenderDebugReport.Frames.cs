using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Diagnostics;

internal static partial class RenderDebugReport
{
    private sealed record Sample(long Calls, string First, string Last, string Detail);
    private static readonly object StageGate = new();
    //At most 32 stage keys, only during the explicit 3-second capture; no frames/materials/targets retained.
    private static readonly Dictionary<string, Sample> Stages = new();
    private static void ClearStages() { lock (StageGate) Stages.Clear(); }
    internal static void Stage(string key, string detail)
    {
        if (!Collecting) return;
        lock (StageGate)
        {
            if (Stages.Count >= 32 && !Stages.ContainsKey(key)) return;
            string now = DateTime.UtcNow.ToString("O");
            Stages.TryGetValue(key, out var previous);
            Stages[key] = new Sample((previous?.Calls ?? 0) + 1, previous?.First ?? now, now, detail);
        }
    }
    private static long nextFrameSample;
    internal static void Frame(MeshDrawFrame frame, bool hdr)
    {
        if (!Collecting) return;
        long now = Stopwatch.GetTimestamp();
        if (now < Volatile.Read(ref nextFrameSample)) return;
        Volatile.Write(ref nextFrameSample, now + Stopwatch.Frequency / 2);
        try
        {
        var counts = new Dictionary<string, int>(); int missing = 0, disposed = 0, sampled = 0;
        var bindings = new StringBuilder();
        foreach (var draw in frame.Draws.Concat(frame.PersistentDraws).Take(4096))
        {
            string key = "pass=" + draw.PassId + "/layer=" + draw.Layer;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            if (sampled++ >= 128) continue;
            if (sampled <= 24) bindings.AppendLine("DRAW material=" + draw.Binding.DebugName + "; shader=" + draw.Binding.Shader.Key + "; shaderErrors=" + draw.Binding.Shader.HasErrors + "; pass=" + draw.PassId + "; layer=" + draw.Layer + "; queue=" + draw.RenderQueue + "; override=" + draw.HasOverride + "; pipeline=" + draw.Binding.PipelineState);
            foreach (var slot in draw.Binding.MaterialLayout.TextureSlots)
            {
                var texture = draw.HasOverride ? draw.Resources[slot.ResourceSlot] : draw.Binding.Resources[slot.ResourceSlot];
                if (sampled <= 24) bindings.AppendLine(" slot=" + slot.PropertyName + "; texture=" + texture?.Name + "; size=" + texture?.width + "x" + texture?.height + "; gpu=" + (texture?.GpuResource != null) + "; format=" + texture?.GpuResource?.Format + "; layout=" + texture?.GpuResource?.CurrentLayout);
                if (texture?.GpuResource == null) missing++;
                else if (texture.GpuResource.IsDisposed) disposed++;
            }
        }
        Stage("MapCapture", "frame=" + frame.FrameNumber + " dynamic=" + frame.Draws.Count + " persistent=" + frame.PersistentDraws.Count + " HDR=" + hdr
            + " background=" + frame.BackgroundColor + "\nfirst <=4096 entries: " + string.Join(",", counts.Select(x => x.Key + ":" + x.Value))
            + "\nfirst <=128 bindings before host residency resolution: missing/null GPU slots=" + missing + "; disposed GPU=" + disposed + "\nfirst <=24 draw bindings:\n" + bindings);
        }
        catch (Exception e) { Stage("Map sampling failed", Error(e)); }
    }
    private static void FrameSection(StringBuilder b)
    {
        b.AppendLine("Observed calls only, not proof of successful GPU execution. Main-menu map stages may correctly be absent.");
        lock (StageGate)
        {
            if (Stages.Count == 0) b.AppendLine("NO OBSERVED STAGES. Check engine activity / patched methods below; this is not reported as normal.");
            foreach (var row in Stages.OrderBy(x => x.Key)) b.AppendLine(row.Key + " calls=" + row.Value.Calls + " first=" + row.Value.First + " last=" + row.Value.Last + "\n" + row.Value.Detail);
        }
    }
    //Method discovery is setup-only; inactive hooks perform a single boolean check. No patch modifies rendering.
    [HarmonyPatch]
    private static class ObservePasses
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            //CompositePass.Record contains host readonly-reference calls that Harmony cannot safely rewrite.
            //Map slices and native composition are observed at existing bridge call sites instead.
            var method = AccessTools.DeclaredMethod(AccessTools.TypeByName("RimWorldWaylith.Rendering.PostFxFeature"), "CaptureFrame");
            if (method != null) yield return method;
        }
        private static void Postfix(object __instance, MethodBase __originalMethod)
        {
            if (!Collecting) return;
            try
            {
                string detail = __instance.GetType().FullName;
                Stage(__originalMethod.DeclaringType.FullName + "." + __originalMethod.Name, detail);
            }
            catch (Exception e) { Stage("Observer error", Error(e)); }
        }
        private static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (Collecting && __exception != null) Stage("Render exception", __originalMethod.DeclaringType?.FullName + "." + __originalMethod.Name + "\n" + __exception);
            return __exception;
        }
    }
}
