using System.Collections.Generic;

namespace RimUnity.EventSystems;

//对齐 UGUI 1.0.0 PointerEventData 语义，同时保留原有公开字段的二进制兼容性。
public class PointerEventData : BaseEventData
{
    public enum InputButton { Left = 0, Right = 1, Middle = 2 }
    public enum FramePressState { Pressed, Released, PressedAndReleased, NotChanged }

    public Vector2 position;
    public Vector2 delta;
    public Vector2 pressPosition;
    public Vector2 scrollDelta;
    public InputButton button;
    public bool dragging;
    public int clickCount;
    public readonly List<GameObject> hovered = new();
    public bool useDragThreshold = true;
    public bool eligibleForClick;
    public GameObject pointerDrag, pointerPress, rawPointerPress;
    public Camera pressEventCamera { get; internal set; }
    public Camera enterEventCamera { get; internal set; }
    public RaycastResult pointerPressRaycast;

    public GameObject pointerEnter { get; set; }
    public GameObject lastPress { get; internal set; }
    public GameObject pointerClick { get; set; }
    public RaycastResult pointerCurrentRaycast { get; set; }
    public int displayIndex { get; set; }
    public int pointerId { get; set; } = -1;
    public float clickTime { get; set; }
    public bool fullyExited { get; set; }
    public bool reentered { get; set; }

    //指针和导航事件共享处理标记与焦点访问。
    public PointerEventData(EventSystem system = null) : base(system) { }

    public bool IsPointerMoving() => delta.sqrMagnitude > 0f;
    public bool IsScrolling() => scrollDelta.sqrMagnitude > 0f;
}

//记录命中的图形节点与层级信息。
public struct RaycastResult
{
    public GameObject gameObject;
    public Vector2 screenPosition;
    public int depth;
    public int sortingLayer;
    public int sortingOrder;
    public bool isValid => gameObject != null;
}

//指针与拖动事件回调接口，全部继承 IEventSystemHandler 供 ExecuteEvents 统一派发。
public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData eventData); }
public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData eventData); }
public interface IPointerMoveHandler : IEventSystemHandler { void OnPointerMove(PointerEventData eventData); }
public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData eventData); }
public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData eventData); }
public interface IInitializePotentialDragHandler : IEventSystemHandler { void OnInitializePotentialDrag(PointerEventData eventData); }
public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData eventData); }
public interface IDropHandler : IEventSystemHandler { void OnDrop(PointerEventData eventData); }
public interface IScrollHandler : IEventSystemHandler { void OnScroll(PointerEventData eventData); }
