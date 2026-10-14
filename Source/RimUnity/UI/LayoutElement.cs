namespace RimUnity.UI;

//声明子节点参与自动布局时的尺寸与排除规则。
public class LayoutElement : MonoBehaviour, RimUnity.UI.ILayoutElement
{
    public bool ignoreLayout { get; set => SetProperty(ref field, value); }
    public float minWidth { get; set => SetProperty(ref field, value); } = -1f;
    public float minHeight { get; set => SetProperty(ref field, value); } = -1f;
    public float preferredWidth { get; set => SetProperty(ref field, value); } = -1f;
    public float preferredHeight { get; set => SetProperty(ref field, value); } = -1f;
    public float flexibleWidth { get; set => SetProperty(ref field, value); } = -1f;
    public float flexibleHeight { get; set => SetProperty(ref field, value); } = -1f;
    public int layoutPriority { get; set => SetProperty(ref field, value); } = 1;

    //显式尺寸不需要额外的横向测量。
    public void CalculateLayoutInputHorizontal() { }
    //显式尺寸不需要额外的纵向测量。
    public void CalculateLayoutInputVertical() { }
}
