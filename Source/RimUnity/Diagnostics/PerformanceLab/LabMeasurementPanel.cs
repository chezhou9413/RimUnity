using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Verse;
using Waylith.Input;

namespace RimUnity.Diagnostics;

// A native ImGui window, drawn once by the host before ImGui.Render. Owned only
// by the lab session; it neither inherits nor calls the compatibility UI window.
internal sealed class LabMeasurementPanel(PerformanceLab lab)
{
    private bool failed;
    private int reportedStates;
    private bool firstWindow = true;

    internal void Draw()
    {
        if (failed) return;
        try
        {
            if (!lab.IsCurrent) { ReportOnce(1, "测试场不是当前地图"); return; }
            if (!DebugSettings.ShowDevGizmos) { ReportOnce(2, "等待开发者模式和上帝模式同时开启"); return; }
            var context = ImGui.GetCurrentContext();
            if (context.IsNull() || !context.WithinFrameScope) { ReportOnce(4, "原生 UI 帧作用域不可用"); return; }
            DrawWindow();
            ReportOnce(8, "已提交原生自动测量面板");
        }
        catch (Exception ex)
        {
            // Disable this session's panel on failure, rather than throwing or
            // repeating errors from inside the host's frame-finalization method.
            failed = true;
            lab.EndMeasurementSequence("原生测量面板异常");
            lab.Status = "自动测量面板已停用：" + ex.Message;
            Log.Error("[RimUnity] " + lab.Status + "\n" + ex);
        }
    }

    // At most four messages per lab session, never per-frame logging.
    private void ReportOnce(int state, string message)
    {
        if ((reportedStates & state) != 0) return;
        reportedStates |= state;
        Log.Message("[RimUnity] 自动测量面板：" + message);
    }

    private void DrawWindow()
    {
        var viewport = ImGui.GetMainViewport();
        float width = Math.Clamp(560f, 1f, Math.Max(1f, viewport.Size.X - 32f));
        if (firstWindow)
        {
            // A new lab session must be discoverable even if the previous
            // session's window was moved off-screen or left collapsed.
            ImGui.SetNextWindowPos(new float2(viewport.Pos.X + viewport.Size.X - width - 16f, viewport.Pos.Y + 40f), ImGuiCond.Always);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
            firstWindow = false;
        }
        ImGui.SetNextWindowSize(new float2(width, 0), ImGuiCond.Always);
        bool visible = ImGui.Begin("RimUnity · 自动测量###RimUnity.Measurements", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize);
        try
        {
            if (!visible) return;
            if (lab.MeasurementRunning)
            {
                ImGui.TextWrapped(lab.MeasurementProgress);
                if (Button("停止自动测量并保留已完成报告", "##stop")) lab.EndMeasurementSequence("用户停止");
            }
            else
            {
#if RIMUNITY_PARTICLE_ANALYSIS
                if (Button("一键拖尾细分测量（约 3 分钟，分析版）", "##trail-detail")) lab.StartMeasurementSequence(trailBreakdown: true);
#else
                if (Button("一键拖尾旧／新对照（约 5 分钟）", "##trail-ab")) lab.StartMeasurementSequence(trailComparison: true);
                if (!lab.MeasurementRunning && Button("一键短复测：标准负载＋高拖尾（约 3 分钟）", "##quick")) lab.StartMeasurementSequence(geometryOnly: true);
                // Starting quick measurement changes state in this same frame.
                if (!lab.MeasurementRunning && Button("一键自动测量：探针对照与规模测试（约 16 分钟）", "##full")) lab.StartMeasurementSequence();
#endif
            }
            if (!lab.MeasurementRunning && !string.IsNullOrEmpty(lab.MeasurementReport)
                && Button("复制自动测量汇总路径", "##copy"))
            {
                try { Clipboard.Text = lab.MeasurementReport; }
                catch (Exception ex) { lab.Status = "无法访问剪贴板：" + ex.Message; }
            }
            ImGui.TextWrapped(lab.Status);
        }
        finally { ImGui.End(); }
    }

    // Native button hit testing/background; native draw-list text wraps to the
    // measured width, including Chinese text at the host's current font size.
    private static bool Button(string caption, string id)
    {
        float width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var padding = ImGui.GetStyle().FramePadding;
        float textWidth = Math.Max(1f, width - padding.X * 2);
        float height = Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(caption, false, textWidth).Y + padding.Y * 2);
        bool clicked = ImGui.Button(id, new float2(width, height));
        var position = ImGui.GetItemRectMin();
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new float2(position.X + padding.X, position.Y + padding.Y), ImGui.GetColorU32(ImGuiCol.Text), caption, textWidth);
        return clicked;
    }
}
