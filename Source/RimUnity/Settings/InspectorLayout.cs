using UnityEngine;
using Verse;

namespace RimUnity.Settings;
using Rect = UnityEngine.Rect;
using Color = UnityEngine.Color;

//按实际字体高度排列 Inspector 参数，中文与其他语言都保留足够空间。
internal static class InspectorLayout
{
    internal static readonly Color Background = new(.15f, .15f, .16f);
    internal static readonly Color Panel = new(.20f, .20f, .21f);
    internal static readonly Color Selection = new(.16f, .36f, .55f);
    internal static string L(string chinese, string english) => LanguageDatabase.activeLanguage?.folderName?.StartsWith("Chinese") == true ? chinese : english;
    internal static float Height(string text, float width) => Mathf.Max(32, Text.CalcHeight(text, Mathf.Max(1, width - 16)) + 12);

    //按钮和标题均按文本高度扩展。
    internal static bool Button(Listing_Standard list, string text) => Widgets.ButtonText(list.GetRect(Height(text, list.ColumnWidth)), text);

    //折叠区域只改变窗口状态，不影响运行时参数。
    internal static bool Group(Listing_Standard list, string title, ref bool expanded)
    {
        list.Gap(8);
        Rect row = list.GetRect(Height(title, list.ColumnWidth));
        Widgets.DrawBoxSolid(row, Panel);
        Widgets.Label(new Rect(row.x + 8, row.y + 6, row.width - 16, row.height - 6), (expanded ? "▼  " : "▶  ") + title);
        if (Widgets.ButtonInvisible(row)) expanded = !expanded;
        list.Gap(6);
        return expanded;
    }

    //长值换行显示，避免版本路径与提供方名称超出窗口。
    internal static void Value(Listing_Standard list, string label, string value)
    {
        string text = label + ": " + value;
        Rect row = list.GetRect(Height(text, list.ColumnWidth));
        Widgets.DrawBoxSolid(row, Panel);
        Widgets.Label(new Rect(row.x + 8, row.y + 6, row.width - 16, row.height - 6), text);
        list.Gap(4);
    }
}
