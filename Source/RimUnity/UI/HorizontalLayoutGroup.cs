namespace RimUnity.UI;

//复用线性布局参数，沿水平方向放置子节点。
public class HorizontalLayoutGroup : VerticalLayoutGroup
{
    internal override bool Horizontal => true;
}
