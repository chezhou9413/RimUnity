namespace RimUnity.UI;

//保存布局测量结果，具体排列算法由线性或网格布局实现。
public abstract class LayoutGroup : MonoBehaviour, ILayoutElement, ILayoutGroup
{
    public RectOffset padding { get; set => SetProperty(ref field, value); } = new();
    public TextAnchor childAlignment { get; set => SetProperty(ref field, value); }
    public float minWidth { get; protected set; }
    public float preferredWidth { get; protected set; }
    public float flexibleWidth { get; protected set; }
    public float minHeight { get; protected set; }
    public float preferredHeight { get; protected set; }
    public float flexibleHeight { get; protected set; }
    public int layoutPriority => 0;
    public virtual void CalculateLayoutInputHorizontal() { }
    public abstract void CalculateLayoutInputVertical();
    public abstract void SetLayoutHorizontal();
    public abstract void SetLayoutVertical();
}
