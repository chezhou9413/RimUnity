using System;
using UnityEngine;
using Verse;
using UVector2 = UnityEngine.Vector2;
using URect = UnityEngine.Rect;

namespace RimUnity.Diagnostics;

//侧边控制面板允许移动地图相机，折叠后仍显示负载与退出入口。
internal sealed partial class LabWindow : Window
{
    private readonly PerformanceLab lab;
    private int tab;
    private bool compact;
    private float expandedHeight;
    private UVector2 scroll;
    private float contentHeight = 1100;
    public override UVector2 InitialSize => new(Math.Min(560, Verse.UI.screenWidth - 40), Math.Min(820, Verse.UI.screenHeight - 80));

    //强制暂停原版 Tick，并阻止临时地图被常规保存菜单保存。
    internal LabWindow(PerformanceLab lab)
    {
        this.lab = lab;
        doCloseX = true; draggable = true; absorbInputAroundWindow = false;
        forcePause = true; preventSave = true; preventCameraMotion = false; closeOnAccept = false;
    }

    //窗口放在左侧，保留地图中心给特效展示。
    protected override void SetInitialSizeAndPosition()
    {
        base.SetInitialSizeAndPosition();
        windowRect.x = 16; windowRect.y = 40; expandedHeight = windowRect.height;
    }

    //关闭面板等同退出，暂停采样使用面板内按钮。
    public override void PostClose() { lab.Close(); base.PostClose(); }

    //自动退出场景后及时关闭旧面板，避免窗口继续强制暂停游戏。
    public override void WindowUpdate()
    {
        base.WindowUpdate();
        if (PerformanceLab.Current != lab) Close();
    }

    //滚动内容和底部退出按钮分开布局，中文行高按宿主实际字体测量。
    public override void DoWindowContents(URect rect)
    {
        var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap; var color = GUI.color;
        try
        {
            Text.Font = GameFont.Small; Text.Anchor = UnityEngine.TextAnchor.UpperLeft; Text.WordWrap = true;
            float row = Math.Max(32, Text.LineHeight + 10), y = 0;
            Widgets.Label(new URect(0, y, rect.width - 145, row), "RimUnity · 特效性能测试场");
            if (Widgets.ButtonText(new URect(rect.width - 135, y, 95, row), compact ? "展开" : "折叠"))
            {
                compact = !compact;
                windowRect.height = compact ? row * 3 + Margin * 2 + 24 : expandedHeight;
                return;
            }
            y += row + 6;
            if (compact)
            {
                Widgets.Label(new URect(0, y, rect.width, row), $"实例 {lab.LiveCount} · 已创建 {lab.Spawned} · 采样 {(lab.Sampler.Sampling ? "开启" : "关闭")}");
                y += row + 6;
                if (Widgets.ButtonText(new URect(0, y, rect.width, row), "退出测试场")) Close();
                return;
            }
            string[] tabs = { "播放控制", "选择特效", "采样与报告" };
            for (int i = 0; i < tabs.Length; i++)
                if (Widgets.ButtonText(new URect(i * rect.width / 3, y, rect.width / 3 - 4, row), (tab == i ? "● " : "") + tabs[i]))
                { tab = i; scroll = default; }
            y += row + 8;
            var viewport = new URect(0, y, rect.width, Math.Max(1, rect.height - y - row - 12));
            var view = new URect(0, 0, viewport.width - 20, contentHeight);
            Widgets.BeginScrollView(viewport, ref scroll, view);
            try
            {
                float at = 0;
                if (tab == 0) DrawControls(view.width, row, ref at);
                else if (tab == 1) DrawEffects(view.width, row, ref at);
                else DrawReports(view.width, row, ref at);
                contentHeight = Math.Max(viewport.height, at + 8);
            }
            finally { Widgets.EndScrollView(); }
            if (Widgets.ButtonText(new URect(0, rect.height - row, rect.width, row), "退出测试场并返回地图")) Close();
        }
        finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
    }

    //说明文本使用实际换行高度，路径和长特效名不会覆盖后续控件。
    private static void Label(string value, float width, ref float y)
    {
        float height = Math.Max(Text.LineHeight, Text.CalcHeight(value, width));
        Widgets.Label(new URect(0, y, width, height), value); y += height + 10;
    }
}
