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

    internal void Draw()
    {
        if (failed || !lab.IsCurrent || !DebugSettings.ShowDevGizmos) return;
        var context = ImGui.GetCurrentContext();
        if (context.IsNull() || !context.WithinFrameScope) return;
        try { DrawWindow(); }
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

    private void DrawWindow()
    {
        var viewport = ImGui.GetMainViewport();
        float width = Math.Clamp(560f, 1f, Math.Max(1f, viewport.Size.X - 32f));
        ImGui.SetNextWindowPos(new float2(viewport.Pos.X + viewport.Size.X - width - 16f, viewport.Pos.Y + 40f), ImGuiCond.FirstUseEver);
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
                if (Button("一键短复测：标准负载＋高拖尾（约 3 分钟）", "##quick")) lab.StartMeasurementSequence(geometryOnly: true);
                // Starting quick measurement changes state in this same frame.
                if (!lab.MeasurementRunning && Button("一键自动测量：探针对照与规模测试（约 16 分钟）", "##full")) lab.StartMeasurementSequence();
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
