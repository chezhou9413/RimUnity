using UnityEngine;
using Verse;
using static RimUnity.Settings.InspectorLayout;

namespace RimUnity.Rendering;

public sealed partial class BloomSettings
{
    private bool parametersExpanded = true, layersExpanded = true;

    //只调整布局与文字，公共参数、设置键和层接管规则保持一致。
    internal void DrawContent(Listing_Standard list)
    {
        list.Label(L("Bloom 泛光", "Bloom"));
        list.Label(L("让明亮特效向周围扩散光晕。参数同时影响手动层和模组接管的层。", "Spread a glow around bright effects. Parameters affect both manual and mod-controlled layers."));
        if (Button(list, L("恢复全部默认设置", "Restore all defaults"))) ResetDefaults();
        if (Group(list, L("光晕参数", "Bloom parameters"), ref parametersExpanded))
        {
            Parameter(list, L("亮部阈值", "Threshold"), ref threshold, 0, 4,
                L("越低，越多像素产生光晕。默认 0.80。", "Lower values let more pixels glow. Default: 0.80."));
            Parameter(list, L("泛光强度", "Intensity"), ref intensity, 0, 4,
                L("叠加光晕的亮度，0 表示关闭叠加。默认 0.60。", "Brightness of the added glow; 0 disables it. Default: 0.60."));
            Parameter(list, L("阈值柔和度", "Soft knee"), ref softKnee, 0, 1,
                L("柔化阈值附近的亮度分界。默认 0.50。", "Smooth the brightness transition around the threshold. Default: 0.50."));
            Parameter(list, L("光晕扩散", "Scatter"), ref scatter, .05f, .95f,
                L("控制光晕扩散范围。默认 0.65。", "Control the spread of the glow. Default: 0.65."));
            if (Button(list, L("仅恢复默认参数", "Reset parameters only")))
            { threshold = .8f; intensity = .6f; softKnee = .5f; scatter = .65f; }
        }
        if (!Group(list, L("参与泛光的层", "Bloom layers"), ref layersExpanded)) return;
        string enableLabel = L("启用手动选择的层", "Enable manually selected layers");
        Widgets.CheckboxLabeled(list.GetRect(Height(enableLabel, list.ColumnWidth)), enableLabel, ref enabled);
        list.Label(L("模组接管的层由各模组设置控制，不受此开关影响。层编号来自 Unity 导出数据；普通地表通常使用层 0，建议保持关闭。",
            "Mod-controlled layers use their own settings and ignore this switch. Layer numbers come from exported Unity data. Layer 0 often includes terrain; leave it off unless needed."));
        if (activeLayers != 0) list.Label(L("当前有模组开启泛光层，上方参数也会影响这些特效。", "Some mods have enabled bloom layers. Parameters above also affect those effects."));
        else if (enabled && (layerMask & ~controlledLayers) == 0) list.Label(L("尚未选择可用的手动层。", "No available manual layer is selected."));
        bool oldEnabled = GUI.enabled;
        GUI.enabled = oldEnabled && enabled;
        if (Button(list, L("清空手动层选择", "Clear manual layers"))) layerMask = 0;
        GUI.enabled = oldEnabled;
        list.Gap(8);
        int columns = list.ColumnWidth >= 560 ? 4 : list.ColumnWidth >= 300 ? 2 : 1;
        float cellWidth = list.ColumnWidth / columns;
        float height = Height(L("层 31 · 模组", "Layer 31 · Mod"), cellWidth - 34);
        for (int first = 0; first < 32; first += columns)
        {
            var row = list.GetRect(height);
            for (int column = 0; column < columns && first + column < 32; column++)
            {
                int layer = first + column, bit = 1 << layer;
                bool controlled = (controlledLayers & bit) != 0;
                bool selected = controlled ? (activeLayers & bit) != 0 : (layerMask & bit) != 0;
                var cell = new UnityEngine.Rect(row.x + column * cellWidth, row.y, cellWidth - 10, row.height);
                GUI.enabled = oldEnabled && enabled && !controlled;
                Widgets.CheckboxLabeled(cell, L("层 ", "Layer ") + layer + (controlled ? L(" · 模组", " · Mod") : ""), ref selected);
                if (!controlled) { if (selected) layerMask |= bit; else layerMask &= ~bit; }
                GUI.enabled = oldEnabled;
                TooltipHandler.TipRegion(cell, controlled
                    ? L("此层由内容模组接管，请在对应模组的设置中切换。", "This layer is controlled by a content mod. Use that mod's settings.")
                    : L("只控制此编号的层；同名但不同编号的层不会自动匹配。", "Controls this layer number only. Equal names with different numbers do not match."));
            }
        }
    }

    //统一参数行，数值与滑块分别占用足够高度。
    private static void Parameter(Listing_Standard list, string label, ref float value, float min, float max, string explanation)
    {
        var row = list.GetRect(Height(label, list.ColumnWidth));
        Widgets.Label(row, $"{label}: {value:F2}");
        TooltipHandler.TipRegion(row, explanation);
        value = list.Slider(value, min, max);
    }
}
