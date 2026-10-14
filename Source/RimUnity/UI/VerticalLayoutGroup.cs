using RimUnity.UI;

namespace RimUnity.UI;

//计算纵向列表的尺寸与位置，横向子类使用相同算法和不同排列轴。
public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
    internal virtual bool Horizontal => false;

    //横向测量时更新参与布局的节点。
    public override void CalculateLayoutInputHorizontal() { CollectChildren(); CalcAlongAxis(0, !Horizontal); }
    //纵向测量沿用当前子节点列表。
    public override void CalculateLayoutInputVertical() => CalcAlongAxis(1, !Horizontal);
    //安排横向位置和尺寸。
    public override void SetLayoutHorizontal() => SetChildrenAlongAxis(0, !Horizontal);
    //安排纵向位置和尺寸。
    public override void SetLayoutVertical() => SetChildrenAlongAxis(1, !Horizontal);
}
