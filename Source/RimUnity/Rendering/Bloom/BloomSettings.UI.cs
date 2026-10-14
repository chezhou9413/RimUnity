using UnityEngine;
using Verse;

namespace RimUnity.Rendering;

public sealed partial class BloomSettings
{
    //仅界面滚动状态，不参与存档或渲染。
    private UnityEngine.Vector2 settingsScroll;
    private float settingsHeight = 900f;

    internal void Draw(UnityEngine.Rect rect)
    {
        var oldFont = Text.Font;
        var oldAnchor = Text.Anchor;
        bool oldWrap = Text.WordWrap, oldEnabled = UnityEngine.GUI.enabled;
        bool scrolling = false;
        try
        {
            Text.Font = GameFont.Small;
            Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
            Text.WordWrap = true;
            var view = new UnityEngine.Rect(0, 0, Mathf.Max(1, rect.width - 20), Mathf.Max(rect.height, settingsHeight));
            Widgets.BeginScrollView(rect, ref settingsScroll, view);
            scrolling = true;
            var listing = new Listing_Standard();
            listing.Begin(view);
            listing.Label("Bloom 泛光");
            listing.Label("让明亮特效向周围扩散光晕。以下参数共用于手动选择的层和模组接管的层，进入地图后可直接观察调整结果。");
            if (Widgets.ButtonText(listing.GetRect(RowHeight), "恢复全部默认设置")) ResetDefaults();
            listing.Gap();

            listing.Label("光晕参数");
            Parameter(listing, "亮部阈值", ref threshold, 0, 4,
                "越低，越多像素产生光晕；越高，只保留最亮部分。默认 0.80。");
            Parameter(listing, "泛光强度", ref intensity, 0, 4,
                "控制叠加光晕的亮度；0 表示不叠加光晕。过高容易使画面发白。默认 0.60。");
            Parameter(listing, "阈值柔和度", ref softKnee, 0, 1,
                "让阈值附近的像素逐渐产生光晕，减少明显分界。默认 0.50。");
            Parameter(listing, "光晕扩散", ref scatter, .05f, .95f,
                "控制光晕向周围扩散的程度。默认 0.65。");
            if (Widgets.ButtonText(listing.GetRect(RowHeight), "仅恢复默认参数"))
            { threshold = .8f; intensity = .6f; softKnee = .5f; scatter = .65f; }
            listing.Gap();

            listing.Label("参与泛光的层");
            listing.CheckboxLabeled("启用手动选择的层", ref enabled);
            listing.Label("模组接管的层由各模组设置控制，不受此开关影响。层编号来自 Unity 导出数据；普通地表通常使用层 0，建议保持关闭。");
            if (activeLayers != 0) listing.Label("当前有模组开启了泛光层，调整上方参数也会影响这些特效。");
            else if (enabled && (layerMask & ~controlledLayers) == 0)
                listing.Label("尚未选择可用的手动层，手动泛光不会生效。");
            UnityEngine.GUI.enabled = oldEnabled && enabled;
            if (Widgets.ButtonText(listing.GetRect(RowHeight), "清空手动层选择")) layerMask = 0;
            UnityEngine.GUI.enabled = oldEnabled;
            listing.Gap();
            int columns = view.width >= 560 ? 4 : view.width >= 300 ? 2 : 1;
            for (int first = 0; first < 32; first += columns)
            {
                UnityEngine.Rect row = listing.GetRect(RowHeight);
                for (int column = 0; column < columns && first + column < 32; column++)
                {
                    int layer = first + column, bit = 1 << layer;
                    bool controlled = (controlledLayers & bit) != 0;
                    bool selected = controlled ? (activeLayers & bit) != 0 : (layerMask & bit) != 0;
                    var cell = new UnityEngine.Rect(row.x + column * row.width / columns, row.y, row.width / columns - 10, row.height);
                    UnityEngine.GUI.enabled = oldEnabled && enabled && !controlled;
                    Widgets.CheckboxLabeled(cell, controlled ? $"层 {layer} · 模组" : $"层 {layer}", ref selected);
                    if (!controlled)
                    { if (selected) layerMask |= bit; else layerMask &= ~bit; }
                    UnityEngine.GUI.enabled = oldEnabled;
                    TooltipHandler.TipRegion(cell, controlled
                        ? "此层由内容模组接管，请在对应模组的设置中切换。这里显示当前实际开关。"
                        : layer == 0 ? "层 0 通常包括普通地表，开启后可能让大面积场景产生泛光。" : "只控制此编号的层；同名但编号不同的 Unity 层不会自动匹配。");
                }
            }
            settingsHeight = listing.CurHeight + 12;
            listing.End();
        }
        finally
        {
            if (scrolling) Widgets.EndScrollView();
            Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; UnityEngine.GUI.enabled = oldEnabled;
        }
    }

    private static float RowHeight => Mathf.Max(32f, Text.LineHeightOf(Text.Font) + 8f);

    private static void Parameter(Listing_Standard listing, string label, ref float value, float min, float max, string explanation)
    {
        var row = listing.GetRect(RowHeight);
        Widgets.Label(row, $"{label}：{value:F2}");
        TooltipHandler.TipRegion(row, explanation);
        value = listing.Slider(value, min, max);
    }
}