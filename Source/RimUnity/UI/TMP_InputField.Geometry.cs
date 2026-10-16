using System;

namespace RimUnity.UI;

//输入框光标与选择区使用同一份 TMP 字体度量，不替换成宿主字体。
public sealed partial class TMP_InputField
{
    //通过字符前缀宽度定位点击位置，跳过代理对中间位置。
    private int Hit(Vector2 position)
    {
        if (textComponent == null) return 0;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform, position, null, out Vector2 local)) return caret;
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
    private float Width(int length) => length == 0 ? 0 : textComponent.GetPreferredValues(text.Substring(0, length)).x;
    private Rect Content()
    {
        Rect rect = textComponent.rectTransform.rect;
        Vector4 margin = textComponent.margin;
        rect.xMin += margin.x; rect.xMax -= margin.z; rect.yMin += margin.w; rect.yMax -= margin.y;
        return rect;
    }
    private float Origin(Rect content, float width)
    {
        int align = textComponent.alignment & 255;
        return content.xMin + (align == 2 ? .5f : align == 4 ? 1 : 0) * (content.width - width);
    }
    //可视光标超出单行视口时移动文字，并保留矩形裁切。
    internal Rect Decoration(out bool selection, out bool visible)
    {
        Rect content = Content();
        float total = Width(text.Length), cursor = Width(caret), selected = Width(anchor);
        float origin = Origin(content, total);
        float x = origin + cursor - ScrollOffset;
        if (x > content.xMax - caretWidth) ScrollOffset += x - content.xMax + caretWidth;
        else if (x < content.xMin) ScrollOffset += x - content.xMin;
        float height = textComponent.font.lineHeight * textComponent.fontSize / textComponent.font.pointSize;
        int vertical = textComponent.alignment & 0xFF00;
        float top = content.yMax - (vertical == 512 ? .5f : vertical == 1024 ? 1 : 0) * (content.height - height);
        selection = caret != anchor;
        visible = isFocused && (selection || caretBlinkRate <= 0 || (Time.unscaledTime - blinkStart) * caretBlinkRate % 1 < .5f);
        float left = origin + (selection ? Math.Min(cursor, selected) : cursor) - ScrollOffset;
        return new Rect(left, top - height, selection ? Math.Abs(cursor - selected) : caretWidth, height);
    }
}
