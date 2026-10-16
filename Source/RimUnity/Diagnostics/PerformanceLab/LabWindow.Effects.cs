using System;
using Verse;
using URect = UnityEngine.Rect;

namespace RimUnity.Diagnostics;

//按模组名和逻辑路径筛选，支持单项循环与多项轮播。
internal sealed partial class LabWindow
{
    private string filter = "";

    //选择改变时结束旧样本，防止报告声明的特效集合与实际负载不一致。
    private void SelectionChanged()
    {
        lab.Sampler.Stop("改变特效选择", export: true);
        lab.StopPlayback();
        lab.Status = "选择已更新，请在播放控制页开始播放。";
    }

    private void DrawEffects(float width, float row, ref float y)
    {
        Label("导入特效 " + lab.Effects.Count + " 项 · 已选 " + lab.Effects.FindAll(e => e.Selected).Count + " 项", width, ref y);
        filter = Widgets.TextField(new URect(0, y, width, row), filter); y += row + 8;
        bool all = Widgets.ButtonText(new URect(0, y, width / 2 - 4, row), "勾选筛选结果");
        bool none = Widgets.ButtonText(new URect(width / 2 + 4, y, width / 2 - 4, row), "取消全部勾选");
        if (all || none)
        {
            SelectionChanged();
            foreach (var effect in lab.Effects)
                if (none || effect.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)) effect.Selected = all;
        }
        y += row + 10;
        foreach (var effect in lab.Effects)
        {
            if (!effect.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            string label = effect.Label + "（" + effect.Systems + " 个系统）";
            if (effect.Warnings.Length > 0) label += "【含导出警告】";
            float height = Math.Max(row, Text.CalcHeight(label, width - 34) + 8);
            bool selected = effect.Selected;
            Widgets.CheckboxLabeled(new URect(0, y, width, height), label, ref selected);
            if (selected != effect.Selected) { SelectionChanged(); effect.Selected = selected; }
            y += height + 6;
            foreach (string warning in effect.Warnings) Label("导出警告：" + warning, width, ref y);
        }
    }
}
