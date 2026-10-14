using System.Collections.Generic;
using RimUnity.EventSystems;

namespace RimUnity.UI;

//用场景中已启用的控件建立方向导航，不把资源模板纳入焦点候选。
public abstract partial class Selectable
{
    private static readonly List<Selectable> Active = new();
    public static Selectable[] allSelectablesArray => Active.ToArray();
    public static int allSelectableCount => Active.Count;

    //控件启用时登记导航候选。
    protected virtual void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    //控件停用时释放焦点和指针状态。
    protected virtual void OnDisable()
    {
        Active.Remove(this);
        if (EventSystem.current?.currentSelectedGameObject == gameObject) EventSystem.current.SetSelectedGameObject(null);
        selected = Hovered = Held = false;
        activeAnimationTrigger = null;
    }
    //主动把焦点移到当前可交互控件。
    public void Select() { if (IsInteractable()) EventSystem.current?.SetSelectedGameObject(gameObject); }

    //沿矩形边缘向外搜索，优先选择方向接近且距离较短的控件。
    public Selectable FindSelectable(Vector3 direction)
    {
        direction = direction.normalized;
        Vector3 local = transform.InverseTransformDirection(direction);
        Vector3 point = Vector3.zero;
        if (transform is RectTransform rect)
        {
            float longest = Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y));
            if (longest > 0f) local /= longest;
            point = rect.rect.center + Vector2.Scale(rect.rect.size * 0.5f, local);
        }
        Vector3 origin = transform.TransformPoint(point);
        Selectable best = null, wrapped = null;
        float bestScore = float.NegativeInfinity, wrapScore = float.NegativeInfinity;
        bool wrap = navigation.wrapAround && (navigation.mode == Navigation.Mode.Horizontal || navigation.mode == Navigation.Mode.Vertical);
        foreach (Selectable candidate in Active)
        {
            if (candidate == this || !candidate.IsInteractable() || candidate.navigation.mode == Navigation.Mode.None) continue;
            Vector3 center = candidate.transform is RectTransform other ? (Vector3)other.rect.center : Vector3.zero;
            Vector3 delta = candidate.transform.TransformPoint(center) - origin;
            float dot = Vector3.Dot(direction, delta);
            if (wrap && dot < 0f)
            {
                float score = -dot * delta.sqrMagnitude;
                if (score > wrapScore) { wrapScore = score; wrapped = candidate; }
            }
            if (dot <= 0f) continue;
            float forwardScore = dot / delta.sqrMagnitude;
            if (forwardScore > bestScore) { bestScore = forwardScore; best = candidate; }
        }
        return best ?? (wrap ? wrapped : null);
    }

    //显式导航优先，否则按启用的轴搜索。
    public virtual Selectable FindSelectableOnLeft() => FindDirection(Navigation.Mode.Horizontal, navigation.selectOnLeft, Vector3.left);
    //显式导航优先，否则按启用的轴搜索。
    public virtual Selectable FindSelectableOnRight() => FindDirection(Navigation.Mode.Horizontal, navigation.selectOnRight, Vector3.right);
    //显式导航优先，否则按启用的轴搜索。
    public virtual Selectable FindSelectableOnUp() => FindDirection(Navigation.Mode.Vertical, navigation.selectOnUp, Vector3.up);
    //显式导航优先，否则按启用的轴搜索。
    public virtual Selectable FindSelectableOnDown() => FindDirection(Navigation.Mode.Vertical, navigation.selectOnDown, Vector3.down);
    //把控件局部方向转换为导航使用的世界方向。
    private Selectable FindDirection(Navigation.Mode axis, Selectable explicitTarget, Vector3 local)
        => navigation.mode == Navigation.Mode.Explicit ? explicitTarget :
            (navigation.mode & axis) != 0 ? FindSelectable(transform.TransformDirection(local)) : null;

    //派发方向事件时允许派生控件先处理自身数值。
    public virtual void OnMove(AxisEventData data)
    {
        Selectable next = NavigationTarget(data.moveDir);
        if (next == null || !next.IsInteractable()) return;
        data.selectedObject = next.gameObject;
        data.Use();
    }
    //按事件方向取相邻控件。
    protected Selectable NavigationTarget(MoveDirection direction) => direction switch
    {
        MoveDirection.Left => FindSelectableOnLeft(),
        MoveDirection.Right => FindSelectableOnRight(),
        MoveDirection.Up => FindSelectableOnUp(),
        MoveDirection.Down => FindSelectableOnDown(),
        _ => null
    };
}
