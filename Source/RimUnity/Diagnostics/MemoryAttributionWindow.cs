using System;
using System.Linq;
using System.Text.Json;
using LudeonTK;
using UnityEngine;
using Verse;

namespace RimUnity.Diagnostics;

internal sealed class MemoryAttributionWindow : Window
{
    private MemoryAttribution.Snapshot snapshot;
    private MemoryAttribution.Group[] groups = Array.Empty<MemoryAttribution.Group>();
    private MemoryAttribution.Row[] visible = Array.Empty<MemoryAttribution.Row>();
    private string owner, filter = "", status = "";
    private string summary = "", heapText = "";
    private long totalBytes;
    private UnityEngine.Vector2 ownerScroll, resourceScroll;
    public override UnityEngine.Vector2 InitialSize => new(1100, 740);

    private MemoryAttributionWindow()
    {
        doCloseX = true; draggable = true; absorbInputAroundWindow = false;
        Refresh();
    }

    [DebugAction("RimUnity", "显存归责（全量分配）", allowedGameStates = (AllowedGameStates)0)]
    private static void Open()
    {
        if (Prefs.DevMode && !Find.WindowStack.IsOpen<MemoryAttributionWindow>())
            Find.WindowStack.Add(new MemoryAttributionWindow());
    }

    private void Refresh()
    {
        if (!Prefs.DevMode) return;
        try
        {
            var next = MemoryAttributionCapture.Capture();
            snapshot = next; groups = next.Groups; totalBytes = next.Total;
            if (!groups.Any(g => g.Owner == owner)) owner = null;
            Filter();
            long device = groups.Sum(x => x.Device), host = groups.Sum(x => x.Host), unknown = groups.Sum(x => x.UnknownPlacement);
            long sourced = groups.Where(x => x.Owner != MemoryAttribution.Unknown && x.Owner != MemoryAttribution.Shared).Sum(x => x.Bytes);
            summary = "全量分配 " + MemoryAttribution.Size(next.Total) + " · " + next.Rows.Length.ToString("N0") + " 项\n"
                + "设备本地 " + MemoryAttribution.Size(device) + " / 系统内存 " + MemoryAttribution.Size(host)
                + " / 位置未知 " + MemoryAttribution.Size(unknown) + "\n"
                + "路径证据已归属 " + MemoryAttribution.Size(sourced) + " / 共享及未归属 " + MemoryAttribution.Size(next.Total - sourced);
            heapText = string.Join("\n", next.Heaps.Select(h => "堆 " + h.Index + "（" + h.Flags + "） 驱动使用 / 预算："
                + MemoryAttribution.Size((long)h.Usage) + " / " + MemoryAttribution.Size((long)h.Budget)
                + "；VMA 分配 / 预留：" + MemoryAttribution.Size((long)h.Allocated) + " / " + MemoryAttribution.Size((long)h.Reserved)));
            status = "快照 " + next.Utc.ToLocalTime().ToString("HH:mm:ss") + " · 采集 " + next.Milliseconds.ToString("F0") + " ms · 手动刷新";
        }
        catch (Exception e) { status = "采集失败，保留上次快照：" + e.Message; }
    }

    private void Filter()
    {
        visible = snapshot?.Rows.Where(x => (owner == null || x.Owner == owner)
            && (x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || x.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || x.Owner.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Bytes).ToArray() ?? Array.Empty<MemoryAttribution.Row>();
        resourceScroll = UnityEngine.Vector2.zero;
    }

    public override void DoWindowContents(UnityEngine.Rect rect)
    {
        if (!Prefs.DevMode) { Close(); return; }
        var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap; var color = UnityEngine.GUI.color;
        try
        {
            Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.UpperLeft; Text.WordWrap = true;
            float line = Text.LineHeight, button = UnityEngine.Mathf.Max(32, line + 10), y = 0;
            Widgets.Label(new UnityEngine.Rect(0, y, rect.width - 44, line + 8), "RimUnity · 显存归责 / 资源来源");
            y += line + 14;
            if (Widgets.ButtonText(new UnityEngine.Rect(0, y, 110, button), "刷新快照")) Refresh();
            if (Widgets.ButtonText(new UnityEngine.Rect(120, y, 140, button), "复制全量 JSON")) Copy();
            Text.WordWrap = false;
            Widgets.Label(new UnityEngine.Rect(274, y, rect.width - 274, button), status);
            TooltipHandler.TipRegion(new UnityEngine.Rect(274, y, rect.width - 274, button), status);
            Text.WordWrap = true;
            y += button + 8;
            float summaryHeight = Text.CalcHeight(summary, rect.width);
            Widgets.Label(new UnityEngine.Rect(0, y, rect.width, summaryHeight), summary);
            if (snapshot != null) TooltipHandler.TipRegion(new UnityEngine.Rect(0, y, rect.width, summaryHeight), string.Join("\n\n", snapshot.Notes));
            y += summaryHeight + 8;
            if (!string.IsNullOrEmpty(heapText))
            {
                float height = Text.CalcHeight(heapText, rect.width);
                Widgets.Label(new UnityEngine.Rect(0, y, rect.width, height), heapText); y += height + 6;
            }
            const string caveat = "按实际来源与唯一分配句柄记账；不是卸载该模组后的节省量。设备本地分配不等于驱动当前物理驻留。共享资源不硬分摊；位置未知不算作显存。";
            float noteHeight = Text.CalcHeight(caveat, rect.width);
            Widgets.Label(new UnityEngine.Rect(0, y, rect.width, noteHeight), caveat); y += noteHeight + 8;
            float left = UnityEngine.Mathf.Min(360, rect.width * .35f), right = left + 14;
            if (Widgets.ButtonText(new UnityEngine.Rect(0, y, left, button), "全部来源（" + groups.Length + " 组）")) { owner = null; Filter(); }
            var nextFilter = Widgets.TextField(new UnityEngine.Rect(right, y, rect.width - right, button), filter);
            if (nextFilter != filter) { filter = nextFilter; Filter(); }
            TooltipHandler.TipRegion(new UnityEngine.Rect(right, y, rect.width - right, button), "搜索资源路径、来源或类别");
            y += button + 6;
            float footerHeight = line + 6;
            var body = new UnityEngine.Rect(0, y, rect.width, UnityEngine.Mathf.Max(1, rect.height - y - footerHeight - 8));
            DrawOwners(new UnityEngine.Rect(0, body.y, left, body.height), line);
            DrawResources(new UnityEngine.Rect(right, body.y, rect.width - right, body.height), line);
            Widgets.Label(new UnityEngine.Rect(0, rect.height - footerHeight, rect.width, footerHeight), "资源明细 " + visible.Length.ToString("N0") + " 项 · 悬停查看来源证据、位置和完整路径");
        }
        finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; UnityEngine.GUI.color = color; }
    }

    private void DrawOwners(UnityEngine.Rect viewport, float line)
    {
        float step = line * 3 + 18;
        var view = new UnityEngine.Rect(0, 0, viewport.width - 20, UnityEngine.Mathf.Max(viewport.height, groups.Length * step));
        Widgets.BeginScrollView(viewport, ref ownerScroll, view);
        try
        {
            int start = UnityEngine.Mathf.Max(0, (int)(ownerScroll.y / step));
            int end = UnityEngine.Mathf.Min(groups.Length, (int)((ownerScroll.y + viewport.height) / step) + 1);
            Text.WordWrap = false;
            for (int i = start; i < end; i++)
            {
                var g = groups[i]; float y = i * step;
                var area = new UnityEngine.Rect(0, y, view.width, step - 4);
                if (owner == g.Owner) Widgets.DrawBoxSolid(area, new UnityEngine.Color(.095f, .14f, .16f));
                Widgets.Label(new UnityEngine.Rect(6, y, view.width - 12, line), g.Owner);
                Widgets.Label(new UnityEngine.Rect(6, y + line, view.width - 12, line), MemoryAttribution.Size(g.Bytes) + " · " + g.Count.ToString("N0") + " 项");
                Widgets.Label(new UnityEngine.Rect(6, y + line * 2, view.width - 12, line), "设备 " + MemoryAttribution.Size(g.Device) + " / 系统 " + MemoryAttribution.Size(g.Host));
                float fraction = totalBytes > 0 ? (float)((double)g.Bytes / totalBytes) : 0;
                Widgets.DrawBoxSolid(new UnityEngine.Rect(6, y + step - 10, (view.width - 12) * fraction, 3), new UnityEngine.Color(.3f, .65f, .68f));
                TooltipHandler.TipRegion(area, g.Owner + "\n占全量分配 " + (fraction * 100).ToString("F2") + "%\n位置未知 " + MemoryAttribution.Size(g.UnknownPlacement));
                if (Widgets.ButtonInvisible(area)) { owner = g.Owner; Filter(); }
            }
        }
        finally { Text.WordWrap = true; Widgets.EndScrollView(); }
    }

    private void DrawResources(UnityEngine.Rect viewport, float line)
    {
        float step = line * 2 + 12;
        var view = new UnityEngine.Rect(0, 0, viewport.width - 20, UnityEngine.Mathf.Max(viewport.height, visible.Length * step));
        Widgets.BeginScrollView(viewport, ref resourceScroll, view);
        try
        {
            int start = UnityEngine.Mathf.Max(0, (int)(resourceScroll.y / step));
            int end = UnityEngine.Mathf.Min(visible.Length, (int)((resourceScroll.y + viewport.height) / step) + 1);
            Text.WordWrap = false;
            for (int i = start; i < end; i++)
            {
                var row = visible[i]; float y = i * step;
                var area = new UnityEngine.Rect(0, y, view.width, step - 3);
                if ((i & 1) == 0) Widgets.DrawBoxSolid(area, new UnityEngine.Color(.085f, .095f, .115f));
                Widgets.Label(new UnityEngine.Rect(6, y + 2, view.width - 12, line), MemoryAttribution.Size(row.Bytes) + " · " + row.Placement + " · " + row.Category);
                Widgets.Label(new UnityEngine.Rect(6, y + line + 2, view.width - 12, line), row.Name);
                TooltipHandler.TipRegion(area, row.Name + "\n" + row.Owner + "\n" + row.Evidence + "\n"
                    + row.Bytes.ToString("N0") + " bytes · " + row.Placement + "\n" + row.Detail + "\n" + row.Handle);
            }
        }
        finally { Text.WordWrap = true; Widgets.EndScrollView(); }
    }

    private void Copy()
    {
        if (!Prefs.DevMode || snapshot == null) return;
        try
        {
            string text = JsonSerializer.Serialize(new { Schema = 1, Metric = "Tracked allocation bytes, not physical VRAM residency",
                snapshot.Utc, snapshot.Milliseconds, snapshot.Total, Groups = groups, snapshot.Heaps, snapshot.Notes, snapshot.Rows },
                new JsonSerializerOptions { WriteIndented = true });
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) text = text.Replace(JsonSerializer.Serialize(profile)[1..^1], "<USER>", StringComparison.OrdinalIgnoreCase);
            UnityEngine.GUIUtility.systemCopyBuffer = text;
            status = "已复制完整快照（不受筛选和滚动位置限制）";
        }
        catch (Exception e) { status = "复制失败：" + e.Message; }
    }

    public override void PostClose()
    {
        snapshot = null; groups = Array.Empty<MemoryAttribution.Group>(); visible = Array.Empty<MemoryAttribution.Row>();
        base.PostClose();
    }
}
