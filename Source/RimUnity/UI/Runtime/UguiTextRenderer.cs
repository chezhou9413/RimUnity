using System.Runtime.CompilerServices;

namespace RimUnity.UI.Runtime;

//文字测量、字号选择、行距和绘制共用排版结果。
internal static partial class UguiTextRenderer
{
    private readonly record struct FitKey(string Content, int Font, FontStyle Style, bool Rich, bool Wrap,
        float Spacing, Vector2 Bounds, float Scale, int Min, int Max);
    private sealed class FitCache { internal FitKey Key; internal UguiTextLayout.Layout Layout; }
    private static readonly ConditionalWeakTable<Text, FitCache> Fitted = new();

    //按实际绘制的字号、缩放和最佳适配规则提前生成字形。
    internal static void Warm(UguiRuntime.Entry entry, Text text)
    {
        Matrix4x4 matrix = entry.Rect.localToWorldMatrix;
        bool local = Mathf.Abs(matrix.m01) > 0.0001f || Mathf.Abs(matrix.m10) > 0.0001f || matrix.m00 <= 0 || matrix.m11 <= 0;
        float scale = local ? 1f : entry.Canvas.scaleFactor / Verse.Prefs.UIScale * Mathf.Abs(entry.Rect.lossyScale.y);
        UiTextStyle style = UguiRuntime.TextStyle(text);
        style.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * scale));
        Rect bounds = local ? new Rect(0, 0, text.ContentRect.width, text.ContentRect.height)
            : UguiRuntime.Bounds(entry, text.ContentRect);
        UguiTextLayout.Warm(Layout(text, style, bounds, scale));
    }

    //预热与绘制共用同一排版入口，避免最佳适配字号不同而再次扩充图集。
    private static UguiTextLayout.Layout Layout(Text text, UiTextStyle style, Rect bounds, float scale)
    {
        if (!text.resizeTextForBestFit) return UguiTextLayout.Build(text, style, bounds.width);
        var key = new FitKey(text.text, text.font != null ? text.font.GameFontIndex : 1, style.fontStyle,
            text.supportRichText, style.wordWrap, text.lineSpacing, bounds.size, scale,
            text.resizeTextMinSize, text.resizeTextMaxSize);
        FitCache cache = Fitted.GetOrCreateValue(text);
        if (cache.Layout != null && cache.Key == key) return cache.Layout;
        Fit(text, text, style, bounds, scale);
        cache.Key = key;
        return cache.Layout = UguiTextLayout.Build(text, style, bounds.width);
    }

    //布局组件取得的尺寸包括自定义行距，但不受自动字号缩放影响。
    internal static Vector2 Measure(RimUnity.UI.Text text, float width, bool wrap)
    {
        UiTextStyle style = UguiRuntime.TextStyle(text);
        style.wordWrap = wrap;
        return UguiTextLayout.Build(text, style, width).Size;
    }

    //按排版结果提交正文、阴影和轮廓。
    internal static void Draw(UguiRuntime.Entry entry, RimUnity.UI.Text text, UiTextStyle style, Rect bounds, Color tint, float scale)
    {
        UguiTextLayout.Layout layout = Layout(text, style, bounds, scale);
        bool clip = text is not Text { clipText: false } && WorldUiSubmission.Current == null && localEntry == null;
        if (clip) NativeGuiHost.PushClip(bounds);
        try
        {
            foreach (Shadow shadow in text.GetComponents<Shadow>())
            {
                if (!shadow.enabled) continue;
                Color color = shadow.effectColor;
                color.a *= shadow.useGraphicAlpha ? tint.a : entry.Alpha;
                Vector2 delta = new(shadow.effectDistance.x * scale, -shadow.effectDistance.y * scale);
                Surface(text, style, new Rect(bounds.position + delta, bounds.size), color, layout, true);
                if (shadow is Outline)
                {
                    Surface(text, style, new Rect(bounds.position - delta, bounds.size), color, layout, true);
                    Surface(text, style, new Rect(bounds.position + new Vector2(delta.x, -delta.y), bounds.size), color, layout, true);
                    Surface(text, style, new Rect(bounds.position + new Vector2(-delta.x, delta.y), bounds.size), color, layout, true);
                }
            }
            Surface(text, style, bounds, tint, layout, false);
        }
        finally { if (clip) NativeGuiHost.PopClip(); }
    }

    //寻找考虑换行和行距后能够容纳的最大字号。
    private static void Fit(RimUnity.UI.Text text, Text fit, UiTextStyle style, Rect bounds, float scale)
    {
        int low = Mathf.Max(1, Mathf.RoundToInt(fit.resizeTextMinSize * scale));
        int high = Mathf.Max(low, Mathf.RoundToInt(fit.resizeTextMaxSize * scale));
        int best = low;
        while (low <= high)
        {
            int size = low + (high - low) / 2;
            style.fontSize = size;
            Vector2 measured = UguiTextLayout.Build(text, style, bounds.width).Size;
            if (measured.x <= bounds.width && measured.y <= bounds.height) { best = size; low = size + 1; }
            else high = size - 1;
        }
        style.fontSize = best;
    }

    //同一排版同时服务阴影、普通文字和材质字形。
    private static void Surface(RimUnity.UI.Text text, UiTextStyle style, Rect bounds, Color tint, UguiTextLayout.Layout layout, bool shadow)
    {
        MaterialText(text, bounds, tint, layout, shadow);
    }

    //阴影使用统一颜色，正文保留富文本片段的颜色和透明度。
    private static Color RunColor(Color run, Color tint, bool shadow)
        => shadow ? new Color(tint.r, tint.g, tint.b, tint.a * run.a) : run * tint;
}
