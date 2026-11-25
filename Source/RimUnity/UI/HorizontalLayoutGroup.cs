namespace RimUnity.UI;

//对齐 UGUI 1.0.0：直接继承 HorizontalOrVerticalLayoutGroup，不再继承 VerticalLayoutGroup。
public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup
{
    public override void CalculateLayoutInputHorizontal() { CollectChildren(); CalcAlongAxis(0, false); }
    public override void CalculateLayoutInputVertical() => CalcAlongAxis(1, false);
    public override void SetLayoutHorizontal() => SetChildrenAlongAxis(0, false);
    public override void SetLayoutVertical() => SetChildrenAlongAxis(1, false);
}
