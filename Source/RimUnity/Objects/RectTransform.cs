
using RimUnity.UI;
namespace RimUnity;

//保留 UGUI 锚点与尺寸语义，并让引擎继续管理父子关系、销毁和激活。
public class RectTransform : Transform
{
    public enum Axis { Horizontal, Vertical }
    public Vector2 anchorMin { get; set => SetProperty(ref field, value); } = new Vector2(0.5f, 0.5f);
    public Vector2 anchorMax { get; set => SetProperty(ref field, value); } = new Vector2(0.5f, 0.5f);
    public Vector2 pivot { get; set => SetProperty(ref field, value); } = new Vector2(0.5f, 0.5f);
    public Vector2 sizeDelta { get; set => SetProperty(ref field, value); } = new Vector2(100f, 100f);
    private Vector2 anchored;
    private Vector3 lastLocalPosition;
    private Transform lastParent;

    public Vector2 anchoredPosition
    {
        get { CapturePosition(); return anchored; }
        set { anchored = value; WritePosition(); }
    }

    public Vector3 anchoredPosition3D
    {
        get => new Vector3(anchoredPosition.x, anchoredPosition.y, localPosition.z);
        set { localPosition = new Vector3(localPosition.x, localPosition.y, value.z); anchoredPosition = value; }
    }

    public Vector2 offsetMin
    {
        get => anchoredPosition - Vector2.Scale(sizeDelta, pivot);
        set { Vector2 delta = value - offsetMin; sizeDelta -= delta; anchoredPosition += Vector2.Scale(delta, Vector2.one - pivot); }
    }

    public Vector2 offsetMax
    {
        get => anchoredPosition + Vector2.Scale(sizeDelta, Vector2.one - pivot);
        set { Vector2 delta = value - offsetMax; sizeDelta += delta; anchoredPosition += Vector2.Scale(delta, pivot); }
    }

    public Rect rect
    {
        get
        {
            Vector2 parentSize = parent is RectTransform p ? p.rect.size : Vector2.zero;
            Vector2 size = Vector2.Scale(parentSize, anchorMax - anchorMin) + sizeDelta;
            return new Rect(-Vector2.Scale(size, pivot), size);
        }
    }

    //把直接写入 Transform 的位移同步回锚点位置。
    private void CapturePosition()
    {
        if (parent != lastParent) anchored = (Vector2)localPosition - AnchorOrigin();
        else anchored += (Vector2)(localPosition - lastLocalPosition);
        lastParent = parent;
        lastLocalPosition = localPosition;
    }

    //父矩形改变时重算锚点，但保留调用方设置的偏移。
    internal void RefreshAnchors()
    {
        CapturePosition();
        WritePosition();
    }

    //将锚点位置转换为引擎可理解的局部坐标。
    private void WritePosition()
    {
        Vector2 origin = AnchorOrigin();
        localPosition = new Vector3(origin.x + anchored.x, origin.y + anchored.y, localPosition.z);
        lastLocalPosition = localPosition;
        lastParent = parent;
    }

    //计算父矩形中当前锚点基准的位置。
    private Vector2 AnchorOrigin()
        => parent is RectTransform p ? p.rect.min + Vector2.Scale(p.rect.size, anchorMin + Vector2.Scale(anchorMax - anchorMin, pivot)) : Vector2.zero;

    //按父锚点跨度求出指定轴所需的尺寸增量。
    public void SetSizeWithCurrentAnchors(Axis axis, float size)
    {
        int i = (int)axis;
        Vector2 delta = sizeDelta;
        delta[i] = size - (parent is RectTransform p ? p.rect.size[i] * (anchorMax[i] - anchorMin[i]) : 0f);
        sizeDelta = delta;
    }

    //返回左下、左上、右上、右下四个世界坐标。
    public void GetWorldCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length < 4) throw new System.ArgumentException("需要四个角点的数组。", nameof(corners));
        Rect r = rect;
        corners[0] = TransformPoint(new Vector3(r.xMin, r.yMin));
        corners[1] = TransformPoint(new Vector3(r.xMin, r.yMax));
        corners[2] = TransformPoint(new Vector3(r.xMax, r.yMax));
        corners[3] = TransformPoint(new Vector3(r.xMax, r.yMin));
    }
}
