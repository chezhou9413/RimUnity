namespace RimUnity.UI;

//布局元素提供两轴的最小、首选和弹性尺寸。
public interface ILayoutElement
{
    float minWidth { get; }
    float preferredWidth { get; }
    float flexibleWidth { get; }
    float minHeight { get; }
    float preferredHeight { get; }
    float flexibleHeight { get; }
    int layoutPriority { get; }
    void CalculateLayoutInputHorizontal();
    void CalculateLayoutInputVertical();
}

//先完成横向布局，再使用实际宽度计算纵向布局。
public interface ILayoutController
{
    void SetLayoutHorizontal();
    void SetLayoutVertical();
}
public interface ILayoutGroup : ILayoutController { }
public interface ILayoutSelfController : ILayoutController { }
