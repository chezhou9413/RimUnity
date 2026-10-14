using System.Collections.Generic;

namespace RimUnity.UI;

//按固定行列或可用矩形布置网格，保留起始方向、角点和对齐方式。
public class GridLayoutGroup : LayoutGroup
{
    public enum Constraint { Flexible, FixedColumnCount, FixedRowCount }
    public enum Axis { Horizontal, Vertical }
    public enum Corner { UpperLeft, UpperRight, LowerLeft, LowerRight }
    public Constraint constraint { get; set => SetProperty(ref field, value); }
    public int constraintCount { get; set => SetProperty(ref field, value); } = 2;
    public Axis startAxis { get; set => SetProperty(ref field, value); }
    public Corner startCorner { get; set => SetProperty(ref field, value); }
    public Vector2 cellSize { get; set => SetProperty(ref field, value); } = new(100, 100);
    public Vector2 spacing { get; set => SetProperty(ref field, value); }
    private readonly List<RectTransform> children = new();
    private RectTransform Rect => (RectTransform)transform;
    private int ConstraintCount => Mathf.Max(1, constraintCount);

    //横向测量收集子节点，弹性网格用平方根估算首选列数。
    public override void CalculateLayoutInputHorizontal()
    {
        children.Clear();
        foreach (RectTransform child in Runtime.UguiLayout.Children(Rect))
        {
            bool ignored = false;
            foreach (LayoutElement element in child.GetComponents<LayoutElement>())
                if (element.isActiveAndEnabled && element.ignoreLayout) { ignored = true; break; }
            if (!ignored) children.Add(child);
        }
        int minColumns = 1;
        int preferredColumns = Mathf.CeilToInt(Mathf.Sqrt(children.Count));
        if (constraint == Constraint.FixedColumnCount) minColumns = preferredColumns = ConstraintCount;
        else if (constraint == Constraint.FixedRowCount)
            minColumns = preferredColumns = Mathf.CeilToInt(children.Count / (float)ConstraintCount - 0.001f);
        minWidth = padding.horizontal + (cellSize.x + spacing.x) * minColumns - spacing.x;
        preferredWidth = padding.horizontal + (cellSize.x + spacing.x) * preferredColumns - spacing.x;
        flexibleWidth = -1f;
    }

    //纵向测量等待实际宽度确定后计算行数。
    public override void CalculateLayoutInputVertical()
    {
        int rows = constraint == Constraint.FixedRowCount ? ConstraintCount : constraint == Constraint.FixedColumnCount
            ? Mathf.CeilToInt(children.Count / (float)ConstraintCount - 0.001f)
            : Mathf.CeilToInt(children.Count / (float)Capacity(0));
        minHeight = preferredHeight = padding.vertical + (cellSize.y + spacing.y) * rows - spacing.y;
        flexibleHeight = -1f;
    }

    //横向阶段先固定单元尺寸，让子元素能够正确测量换行高度。
    public override void SetLayoutHorizontal()
    {
        foreach (RectTransform child in children)
        {
            child.anchorMin = child.anchorMax = Vector2.up;
            child.sizeDelta = cellSize;
        }
    }

    //纵向阶段根据实际占用的行列完成位置与对齐。
    public override void SetLayoutVertical()
    {
        int count = children.Count;
        if (count == 0) return;
        int columns, rows;
        if (constraint == Constraint.FixedColumnCount)
        {
            columns = ConstraintCount;
            rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        }
        else if (constraint == Constraint.FixedRowCount)
        {
            rows = ConstraintCount;
            columns = Mathf.Max(1, Mathf.CeilToInt(count / (float)rows));
        }
        else { columns = Capacity(0); rows = Capacity(1); }
        bool horizontal = startAxis == Axis.Horizontal;
        int mainCount = horizontal ? columns : rows;
        int actualColumns = horizontal ? Mathf.Min(columns, count) : constraint == Constraint.FixedColumnCount
            ? Mathf.Min(columns, count) : Mathf.Clamp(columns, 1, Mathf.CeilToInt(count / (float)mainCount));
        int actualRows = !horizontal ? Mathf.Min(rows, count) : constraint == Constraint.FixedRowCount
            ? Mathf.Min(rows, count) : Mathf.Clamp(rows, 1, Mathf.CeilToInt(count / (float)mainCount));
        Vector2 used = new(actualColumns * cellSize.x + (actualColumns - 1) * spacing.x,
            actualRows * cellSize.y + (actualRows - 1) * spacing.y);
        Vector2 free = Rect.rect.size - used - new Vector2(padding.horizontal, padding.vertical);
        Vector2 offset = new(padding.left + free.x * ((int)childAlignment % 3) * 0.5f,
            padding.top + free.y * ((int)childAlignment / 3) * 0.5f);

        //固定次轴数量时，把末尾单元移到尚未占用的行或列。
        int move = 0;
        bool fixedSecondary = horizontal ? constraint == Constraint.FixedRowCount : constraint == Constraint.FixedColumnCount;
        if (fixedSecondary && count > ConstraintCount && mainCount > 1)
        {
            int missing = ConstraintCount - Mathf.CeilToInt(count / (float)mainCount);
            if (missing > 0)
            {
                move = missing + Mathf.FloorToInt(missing / (float)(mainCount - 1));
                if (count % mainCount == 1) move++;
            }
        }
        for (int i = 0; i < count; i++)
        {
            int major = i % mainCount;
            int minor = i / mainCount;
            if (count - i <= move) { major = 0; minor = ConstraintCount - (count - i); }
            int x = horizontal ? major : minor;
            int y = horizontal ? minor : major;
            if ((int)startCorner % 2 == 1) x = actualColumns - 1 - x;
            if ((int)startCorner / 2 == 1) y = actualRows - 1 - y;
            RectTransform child = children[i];
            Vector2 topLeft = offset + new Vector2(x * (cellSize.x + spacing.x), y * (cellSize.y + spacing.y));
            child.anchoredPosition = new Vector2(topLeft.x + cellSize.x * child.pivot.x,
                -topLeft.y - cellSize.y * (1f - child.pivot.y));
        }
    }

    //非正步长允许单元重叠，不把它当成每行只能容纳一个元素。
    private int Capacity(int axis)
    {
        float step = cellSize[axis] + spacing[axis];
        if (step <= 0f) return int.MaxValue;
        float available = Rect.rect.size[axis] - (axis == 0 ? padding.horizontal : padding.vertical);
        return Mathf.Max(1, Mathf.FloorToInt((available + spacing[axis] + 0.001f) / step));
    }
}
