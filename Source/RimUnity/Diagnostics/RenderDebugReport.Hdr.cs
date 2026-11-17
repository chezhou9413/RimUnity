using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RimUnity.Rendering;
using Waylith.Rendering;
using NativeTarget = Waylith.Rendering.RenderTexture;

namespace RimUnity.Diagnostics;

internal static partial class RenderDebugReport
{
    // Main-thread capture metadata only. Reset for every explicit capture.
    private static int lastHdrSignature;
    private static long nextHdrDetail;
    private static int lastPlanSignature;
    private static long nextPlanDetail, earliestPlanDetail;
    private static void ResetHdrSampling() { lastHdrSignature = lastPlanSignature = 0; nextHdrDetail = nextPlanDetail = earliestPlanDetail = 0; }

    internal static void HdrFrame(MeshDrawFrame frame, MapHdrMode requested, string route, int captured)
    {
        if (!HdrTracing) return;
        try
        {
            var hash = new HashCode(); int seen = 0, linear = 0, eligible = 0;
            hash.Add(requested); hash.Add(route); hash.Add(captured); hash.Add(frame.Draws.Count); hash.Add(frame.PersistentDraws.Count);
            void Count(IReadOnlyList<FinalDrawItem> draws)
            {
                foreach (var draw in draws)
                {
                    if (seen++ >= 8192) break;
                    bool isLinear = LinearShaderVariants.TryGet(draw.Binding.Shader, out _);
                    if (isLinear) linear++;
                    if (HdrLightBlend.ShouldCapture(draw)) eligible++;
                    hash.Add(draw.Binding.MaterialId); hash.Add(draw.Binding.PipelineState);
                    hash.Add(draw.RenderQueue); hash.Add(draw.PassId); hash.Add(isLinear);
                }
            }
            Count(frame.Draws); Count(frame.PersistentDraws);
            int signature = hash.ToHashCode();
            Stage("HdrCapture", "frame=" + frame.FrameNumber + "; requested=" + requested + "; route=" + route
                + "; dynamic=" + frame.Draws.Count + "; persistent=" + frame.PersistentDraws.Count + "; captured=" + captured
                + "; linear=" + linear + "; eligible=" + eligible + "; examined<=8192; signature=" + signature
                + "; preserve=" + frame.PreserveSceneColor + "; background=" + frame.BackgroundColor);
            long now = Stopwatch.GetTimestamp();
            if (signature == lastHdrSignature && now < nextHdrDetail) return;
            lastHdrSignature = signature; nextHdrDetail = now + Stopwatch.Frequency / 2;
            var text = new StringBuilder("frame=" + frame.FrameNumber + "; source-list indices, not final GPU order\n");
            int linearRows = 0, nativeRows = 0, omitted = 0;
            void Describe(IReadOnlyList<FinalDrawItem> draws, string origin)
            {
                for (int i = 0; i < Math.Min(draws.Count, 8192); i++)
                {
                    var draw = draws[i];
                    bool isLinear = LinearShaderVariants.TryGet(draw.Binding.Shader, out _);
                    // Reserve independent budgets so thousands of terrain entries cannot hide the tracer.
                    if (isLinear ? linearRows++ >= 24 : !draw.Binding.PipelineState.Blend.Enabled || nativeRows++ >= 12)
                    { omitted++; continue; }
                    text.Append(origin).Append('[').Append(i).Append("] ").AppendLine(HdrDraw(draw));
                }
            }
            Describe(frame.PersistentDraws, "persistent"); Describe(frame.Draws, "dynamic");
            text.AppendLine("omitted=" + omitted + "; each source list examined<=8192; linear rows<=24, blended native rows<=12");
            Stage("HdrMaterials", text.ToString());
        }
        catch (Exception error) { Stage("HdrTraceError", "capture: " + Error(error)); }
    }

    private static string HdrDraw(FinalDrawItem draw, bool capturedLight = false)
    {
        bool linear = LinearShaderVariants.TryGet(draw.Binding.Shader, out _);
        bool adaptable = HdrLightBlend.TryAdapt(draw.Binding.PipelineState, out var adapted);
        string reason = capturedLight ? "captured-light" : !HdrLightBlend.IsMap(draw) ? "other-pass" : !linear ? "no-linear-variant" : !adaptable ? "native-blend" : "light";
        return "material=" + draw.Binding.DebugName + "#" + draw.Binding.MaterialId + "; binding=" + draw.Binding.Id
            + "; version=" + draw.Binding.MaterialVersion + "; shader=" + draw.Binding.Shader.Key
            + "; shaderErrors=" + draw.Binding.Shader.HasErrors + "; queue=" + draw.RenderQueue + "; sort=" + draw.SortKey
            + "; pass=" + draw.PassId + "; layer=" + draw.Layer + "; override=" + draw.HasOverride
            + "; grabQueue=" + draw.Binding.Shader.SceneGrab?.Queue + "; classification=" + reason
            + "; pipeline=" + draw.Binding.PipelineState + (reason == "light" ? "; adapted=" + adapted : "");
    }

    internal static void HdrPlan(int frame, List<HdrLightFrame.DrawRun> runs)
    {
        if (!HdrTracing) return;
        try
        {
            var hash = new HashCode(); hash.Add(runs.Count);
            foreach (var run in runs)
            {
                hash.Add(run.Light); hash.Add(run.Draws.Count);
                if (run.Draws.Count > 0) { hash.Add(run.Draws[0].Binding.MaterialId); hash.Add(run.Draws[^1].Binding.MaterialId); }
            }
            int signature = hash.ToHashCode(); long now = Stopwatch.GetTimestamp();
            if (now < earliestPlanDetail || signature == lastPlanSignature && now < nextPlanDetail) return;
            lastPlanSignature = signature; earliestPlanDetail = now + Stopwatch.Frequency / 20; nextPlanDetail = now + Stopwatch.Frequency / 2;
            var text = new StringBuilder("frame=" + frame + "; runs=" + runs.Count + "\n");
            for (int i = 0; i < Math.Min(runs.Count, 64); i++)
            {
                var run = runs[i];
                text.Append("run=").Append(i).Append(run.Light ? " LIGHT" : " SHELL").Append(" count=").Append(run.Draws.Count).AppendLine();
                // First/last show the exact boundaries that must not be crossed by light batching.
                if (run.Draws.Count > 0) text.Append(" first: ").AppendLine(HdrDraw(run.Draws[0], run.Light));
                if (run.Draws.Count > 1) text.Append(" last: ").AppendLine(HdrDraw(run.Draws[^1], run.Light));
                if (text.Length >= 15000) { text.AppendLine("[PLAN DETAILS TRUNCATED; execution events continue]"); break; }
            }
            if (runs.Count > 64) text.AppendLine("[PLAN RUNS TRUNCATED at 64]");
            Stage("HdrPlan", text.ToString());
        }
        catch (Exception error) { Stage("HdrTraceError", "plan: " + Error(error)); }
    }

    internal static void HdrLightPass(string phase, int frame, in FrameContext ctx, NativeTarget scene,
        NativeTarget light, NativeTarget snapshot, int draws)
    {
        if (!HdrTracing) return;
        try
        {
            Stage("HdrLightPass", "frame=" + frame + "; slot=" + ctx.SubmissionFrameIndex + "; phase=" + phase + "; draws=" + draws
                + "; scene=" + Target(scene) + "; light=" + Target(light) + "; snapshot=" + Target(snapshot)
                + "; sceneDepth=" + scene.DepthFormat + "; depthLayout=" + scene.Depth?.CurrentLayout);
        }
        catch (Exception error) { Stage("HdrTraceError", "light-pass: " + Error(error)); }
    }
    private static string Target(NativeTarget target) => target.Width + "x" + target.Height + "/" + target.ColorFormat
        + "/view=" + target.ColorView + "/layout=" + target.Color.CurrentLayout;
}
