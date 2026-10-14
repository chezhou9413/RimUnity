using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using LudeonTK;

namespace RimUnity.Diagnostics;

// Text snapshots only: no retained renderers, materials, maps, or GPU resources.
internal sealed partial class ParticleDebugWindow : Window
{
    internal static bool Capturing;
    internal sealed record Snapshot(string Name, string Report, DateTime Seen, int NativeMaterialId, int NativeMeshId);
    private static readonly Dictionary<string, Snapshot> snapshots = new();
    private string filter = "", selected;
    private UnityEngine.Vector2 listScroll, detailScroll;
    private bool paused;
    public override UnityEngine.Vector2 InitialSize => new(1000, 700);
    internal ParticleDebugWindow() { doCloseX = true; draggable = true; resizeable = true; absorbInputAroundWindow = false; Capturing = true; snapshots.Clear(); FinalReports.Clear(); }
    [DebugAction("RimUnity", "粒子渲染诊断", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void Open() { if (!Find.WindowStack.IsOpen<ParticleDebugWindow>()) Find.WindowStack.Add(new ParticleDebugWindow()); }
    public override void PostClose() { base.PostClose(); Capturing = false; snapshots.Clear(); FinalReports.Clear(); }
    internal static bool BeginSample(string key)
    {
        if (!Capturing || !Prefs.DevMode) return false;
        if (snapshots.TryGetValue(key, out var old)) return (DateTime.UtcNow - old.Seen).TotalSeconds >= .5;
        return snapshots.Count < 256;
    }
    internal static void Record(string key, string name, string report, int nativeMaterialId = 0, int nativeMeshId = 0) => snapshots[key] = new(name, report, DateTime.UtcNow, nativeMaterialId, nativeMeshId);
    public override void DoWindowContents(UnityEngine.Rect rect)
    {
        if (!Prefs.DevMode) { Close(); return; }
        var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap; var color = GUI.color;
        try
        {
            Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.UpperLeft; Text.WordWrap = true;
            float row = Mathf.Max(32, Text.LineHeight + 10);
            Widgets.Label(new UnityEngine.Rect(0, 0, rect.width - 40, row), "RimUnity · 特效渲染诊断");
            float y = row + 8;
            filter = Widgets.TextField(new UnityEngine.Rect(0, y, rect.width - 330, row), filter);
            if (Widgets.ButtonText(new UnityEngine.Rect(rect.width - 320, y, 100, row), paused ? "继续采样" : "暂停采样")) { paused = !paused; Capturing = !paused; }
            if (Widgets.ButtonText(new UnityEngine.Rect(rect.width - 210, y, 100, row), "清空")) { snapshots.Clear(); FinalReports.Clear(); selected = null; }
            if (Widgets.ButtonText(new UnityEngine.Rect(rect.width - 100, y, 100, row), "导出报告")) Export();
            y += row + 8;
            if (Widgets.ButtonText(new UnityEngine.Rect(0, y, 130, row), "复制当前")) Copy(false);
            if (Widgets.ButtonText(new UnityEngine.Rect(140, y, 130, row), "复制全部")) Copy(true);
            Widgets.Label(new UnityEngine.Rect(280, y, rect.width - 280, row), "记录数：" + snapshots.Count + " / 256");
            y += row + 8;
            Widgets.Label(new UnityEngine.Rect(0, y, rect.width, row * 2), "开启后触发特效；每实例每材质最多每 0.5 秒采样，最多 256 项。记录实际提交的 CPU 网格与材质，不能证明 GPU 最终采样结果。");
            y += row * 2 + 8;
            var body = new UnityEngine.Rect(0, y, rect.width, Mathf.Max(1, rect.height - y - 8));
            float left = Mathf.Min(320, rect.width * .35f);
            var rows = snapshots.Where(x => x.Value.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Value.Name).ToArray();
            var viewport = new UnityEngine.Rect(0, body.y, left, body.height);
            var view = new UnityEngine.Rect(0, 0, left - 20, Mathf.Max(body.height, rows.Length * (row + 4)));
            Widgets.BeginScrollView(viewport, ref listScroll, view);
            try
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var item = new UnityEngine.Rect(0, i * (row + 4), view.width, row);
                    if (selected == rows[i].Key) Widgets.DrawBoxSolid(item, new UnityEngine.Color(.095f, .14f, .16f));
                    if (Widgets.ButtonText(item, rows[i].Value.Name)) selected = rows[i].Key;
                    TooltipHandler.TipRegion(item, rows[i].Value.Name);
                }
            }
            finally { Widgets.EndScrollView(); }
            string report = selected != null && snapshots.TryGetValue(selected, out var value)
                ? Format(value)
                : "选择左侧实例查看详情。没有条目时，请在面板开启期间触发粒子；尚未提交绘制的实例不会出现在此列表。";
            var detail = new UnityEngine.Rect(left + 12, body.y, body.width - left - 12, body.height);
            var detailView = new UnityEngine.Rect(0, 0, detail.width - 20, Mathf.Max(detail.height, Text.CalcHeight(report, detail.width - 20) + 8));
            Widgets.BeginScrollView(detail, ref detailScroll, detailView);
            try { Widgets.Label(detailView, report); }
            finally { Widgets.EndScrollView(); }
        }
        finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
    }
    private static string Format(Snapshot snapshot) => "最后采样 UTC：" + snapshot.Seen.ToString("O") + "\n\n" + snapshot.Report + "\n\n" + RendererDebugCapture.CompositeState + "\n\n" + (FinalReports.TryGetValue(snapshot.NativeMaterialId + ":" + snapshot.NativeMeshId, out var final) ? final : "最终帧：尚未捕获此材质，请保持采样并触发特效。");
    private static string AllReports() => string.Join("\n\n----------------\n\n", snapshots.Values.OrderBy(x => x.Name).Select(Format));
    private void Copy(bool all)
    {
        if (!Prefs.DevMode) return;
        string text = all ? AllReports() : selected != null && snapshots.TryGetValue(selected, out var value) ? Format(value) : "";
        if (string.IsNullOrEmpty(text)) { Messages.Message("请先选择已有记录。", MessageTypeDefOf.RejectInput, false); return; }
        try
        {
            UnityEngine.GUIUtility.systemCopyBuffer = text;
            Messages.Message(all ? "已复制全部诊断记录。" : "已复制当前诊断记录。", MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception error) { Log.Error("[RimUnity] 复制粒子诊断失败：" + error.Message); }
    }
    private void Export()
    {
        if (!Prefs.DevMode) return;
        try
        {
            string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimUnityReports"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "particles-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(file, AllReports());
            Messages.Message("诊断报告：" + file, MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception error) { Log.Error("[RimUnity] 导出粒子诊断失败：" + error.Message); }
    }
}
