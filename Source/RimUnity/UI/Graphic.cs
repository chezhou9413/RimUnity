namespace RimUnity.UI;

//图形组件只保存绘制状态，几何和提交由画布运行时统一处理。
public abstract class Graphic : MonoBehaviour
{
    public RectTransform rectTransform => (RectTransform)transform;
    public virtual Color color { get; set; } = Color.white;
    public virtual bool raycastTarget { get; set; } = true;
    public virtual Material material { get; set; }
    public virtual Texture mainTexture => null;

    //布局缓存失效后，下一次画布准备阶段统一重建。
    public virtual void SetLayoutDirty() => Runtime.UguiDirty.Mark(transform);
    //当前几何提交读取组件最新状态，不保留失效的材质或顶点快照。
    public virtual void SetVerticesDirty() => SetLayoutDirty();
    public virtual void SetMaterialDirty() => SetLayoutDirty();
    public virtual void SetAllDirty() => SetLayoutDirty();
}
