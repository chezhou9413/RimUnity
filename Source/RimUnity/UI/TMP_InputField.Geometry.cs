using System;

namespace RimUnity.UI;

//输入框光标与选择区使用同一份 TMP 字体度量，不替换成宿主字体。
public sealed partial class TMP_InputField
{
    //通过字符前缀宽度定位点击位置，跳过代理对中间位置。
    private int Hit(Vector2 position, Camera eventCamera)
    {
        if (textComponent == null || textComponent.font == null) return 0;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform, position, eventCamera, out Vector2 local)) return caret;
        Rect content = Content();
        float total = Width(text.Length), origin = Origin(content, total), x = local.x - origin + ScrollOffset;
        int previous = 0; float previousWidth = 0;
        for (int index = Step(0, true); index > previous; index = Step(index, true))
        {
            float width = Width(index);
            if (x < (previousWidth + width) * .5f) return previous;
            previous = index; previousWidth = width;
            if (index == text.Length) break;
        }
        return text.Length;
    }
    //测量的文本不包含选择区和光标装饰。
    private float Width(int length) => MeasureWidth(text, length);
    private float MeasureWidth(string source, int length) => length == 0 || textComponent == null ? 0 : textComponent.GetPreferredValues(source.Substring(0, length)).x;
    private Rect Content()
    {
        Rect rect = textComponent.rectTransform.rect;
        Vector4 margin = textComponent.margin;
        rect.xMin += margin.x; rect.xMax -= margin.z; rect.yMin += margin.w; rect.yMax -= margin.y;
        return rect;
    }
    private float Origin(Rect content, float width)
    {
        int align = textComponent.alignment & 0xFF;
        return content.xMin + (align is 2 or 32 ? .5f : align == 4 ? 1f : 0f) * (content.width - width);
    }
    //可视光标超出单行视口时移动文字；IME 组合阶段按组合文本与组合光标定位。
    internal Rect Decoration(out bool selection, out bool visible)
    {
        if (textComponent == null || textComponent.font == null || !(textComponent.font.pointSize > 0f))
        {
            selection = false;
            visible = false;
            return default;
        }
        Rect content = Content();
        string composition = ActiveComposition;
        float total, cursor, selected;
        if (composition.Length > 0)
        {
            int start = Math.Min(caret, anchor);
            string display = DisplayText;
            int compositionCaret = start + Math.Clamp(UnityStubs.Internal.ImeComposition.Cursor, 0, composition.Length);
            total = MeasureWidth(display, display.Length);
            cursor = selected = MeasureWidth(display, compositionCaret);
            selection = false;
        }
        else
        {
            total = Width(text.Length);
            cursor = Width(caret);
            selected = Width(anchor);
            selection = caret != anchor;
        }
        float origin = Origin(content, total);
        float x = origin + cursor - ScrollOffset;
        if (x > content.xMax - caretWidth) ScrollOffset += x - content.xMax + caretWidth;
        else if (x < content.xMin) ScrollOffset += x - content.xMin;
        float height = textComponent.font.lineHeight * textComponent.fontSize / textComponent.font.pointSize;
        int vertical = textComponent.alignment & 0xFF00;
        float vFactor = vertical is 512 or 2048 or 4096 or 8192 ? 0.5f : vertical == 1024 ? 1f : 0f;
        float top = content.yMax - vFactor * (content.height - height);
        visible = isFocused && (selection || caretBlinkRate <= 0 || (Time.unscaledTime - blinkStart) * caretBlinkRate % 1 < .5f);
        float left = origin + (selection ? Math.Min(cursor, selected) : cursor) - ScrollOffset;
        return new Rect(left, top - height, selection ? Math.Abs(cursor - selected) : caretWidth, height);
    }
}
