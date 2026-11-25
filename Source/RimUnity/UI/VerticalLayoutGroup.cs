namespace RimUnity.UI;

//对齐 UGUI 1.0.0：直接继承 HorizontalOrVerticalLayoutGroup，沿垂直轴排列子节点。
public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
    public override void CalculateLayoutInputHorizontal() { CollectChildren(); CalcAlongAxis(0, true); }
    public override void CalculateLayoutInputVertical() => CalcAlongAxis(1, true);
    public override void SetLayoutHorizontal() => SetChildrenAlongAxis(0, true);
    public override void SetLayoutVertical() => SetChildrenAlongAxis(1, true);
}
