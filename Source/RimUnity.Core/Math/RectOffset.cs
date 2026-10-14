namespace RimUnity;

//保存整数边距，供布局和文字排版使用。
public sealed class RectOffset
{
    public int left, right, top, bottom;
    public int horizontal => left + right;
    public int vertical => top + bottom;
    public RectOffset() { }
    public RectOffset(int left, int right, int top, int bottom)
    { this.left = left; this.right = right; this.top = top; this.bottom = bottom; }
    //坐标按屏幕向下增长，扩展或缩小边界。
    public Rect Add(Rect rect) => Rect.MinMaxRect(rect.xMin - left, rect.yMin - top, rect.xMax + right, rect.yMax + bottom);
    public Rect Remove(Rect rect) => Rect.MinMaxRect(rect.xMin + left, rect.yMin + top, rect.xMax - right, rect.yMax - bottom);
}
