using System;
using RimUnity.EventSystems;
using UnityEngine;
using Verse;

namespace RimUnity.UI.Runtime;

//按前后层级派发点击和拖动，并在事件到达地图前消费覆盖层输入。
internal static class UguiPointer
{
    private static GameObject hovered, pressed, downTarget, dragTarget, pressHit;
    private static Vector2 pressPosition, previous;
    private static bool dragging;
    private static bool captured;
    private static int pressButton;
    private static bool useDragThreshold = true;
    internal static bool IsOverUi => hovered != null;

    //布局更新后进行精确命中，按下与抬起必须指向同一点击目标。
    internal static void Process()
    {
        UguiKeyboard.Process();
        Event ev = Event.current;
        if (!ev.isMouse && ev.type != EventType.ScrollWheel && ev.type != EventType.Repaint) return;
        GameObject hit = null;
        for (int i = UguiRuntime.Entries.Count - 1; i >= 0; i--)
        {
            UguiRuntime.Entry entry = UguiRuntime.Entries[i];
            if (entry.Raycast && UguiRuntime.Contains(entry, new Vector2(ev.mousePosition.x, ev.mousePosition.y))) { hit = entry.Rect.gameObject; break; }
        }
        Vector2 screen = new(ev.mousePosition.x * Prefs.UIScale, Screen.height - ev.mousePosition.y * Prefs.UIScale);
        PointerEventData data = new() { position = screen, delta = screen - previous, pressPosition = pressPosition,
            button = (PointerEventData.InputButton)ev.button, dragging = dragging, scrollDelta = new Vector2(ev.delta.x, ev.delta.y),
            pointerDrag = dragTarget, pointerPress = downTarget, rawPointerPress = pressHit, useDragThreshold = useDragThreshold,
            pointerPressRaycast = new RaycastResult { gameObject = pressHit } };
        if (hit != hovered)
        {
            Send<IPointerExitHandler>(Find<IPointerExitHandler>(hovered), c => c.OnPointerExit(data));
            SetButton(hovered, false, false);
            hovered = hit;
            Send<IPointerEnterHandler>(Find<IPointerEnterHandler>(hovered), c => c.OnPointerEnter(data));
        }
        SetButton(hovered, true, pressed != null && ClickTarget(hovered) == pressed);
        if ((pressed != null && !pressed.activeInHierarchy) || (dragTarget != null && !dragTarget.activeInHierarchy))
            Release(data);
        if (ev.type == EventType.MouseDown && hit != null)
        {
            captured = true;
            pressHit = hit; data.pointerPressRaycast = new RaycastResult { gameObject = hit };
            pressed = ClickTarget(hit); downTarget = Find<IPointerDownHandler>(hit); dragTarget = Find<IDragHandler>(hit);
            pressPosition = screen; pressButton = ev.button; dragging = false; data.pressPosition = screen;
            data.pointerDrag = dragTarget; data.pointerPress = downTarget; data.rawPointerPress = hit;
            data.useDragThreshold = true;
            EventSystem.current?.SetSelectedGameObject(pressed ?? downTarget);
            Send<IPointerDownHandler>(downTarget, c => c.OnPointerDown(data));
            Send<IInitializePotentialDragHandler>(dragTarget, c => c.OnInitializePotentialDrag(data));
            useDragThreshold = data.useDragThreshold;
            SetButton(hit, true, true); ev.Use();
        }
        else if (ev.type == EventType.MouseDrag && dragTarget != null)
        {
            data.button = (PointerEventData.InputButton)pressButton;
            float threshold = EventSystem.current?.pixelDragThreshold ?? 10;
            if (!dragging && (!useDragThreshold || (screen - pressPosition).sqrMagnitude >= threshold * threshold))
            {
                dragging = true; data.dragging = true;
                Send<IBeginDragHandler>(dragTarget, c => c.OnBeginDrag(data));
            }
            if (dragging) Send<IDragHandler>(dragTarget, c => c.OnDrag(data));
            ev.Use();
        }
        else if (ev.type == EventType.MouseUp && captured && ev.button == pressButton)
        {
            data.button = (PointerEventData.InputButton)pressButton;
            Send<IPointerUpHandler>(downTarget, c => c.OnPointerUp(data));
            if (dragging) Send<IDropHandler>(Find<IDropHandler>(hit), c => c.OnDrop(data));
            if (!dragging && pressed != null && pressed == ClickTarget(hit))
            {
                Send<IPointerClickHandler>(pressed, c => c.OnPointerClick(data));
                Button button = pressed.GetComponent<Button>();
                if (pressButton == 0 && button != null && button.IsInteractable()) button.onClick.Invoke();
            }
            Release(data); ev.Use();
        }
        else if (ev.type == EventType.ScrollWheel && hit != null)
        {
            Send<IScrollHandler>(Find<IScrollHandler>(hit), c => c.OnScroll(data)); ev.Use();
        }
        else if ((hit != null || captured) && ev.isMouse) ev.Use();
        previous = screen;
    }

    //拖动完成或目标失活后释放捕获。
    private static void Release(PointerEventData data)
    {
        if (dragging) Send<IEndDragHandler>(dragTarget, c => c.OnEndDrag(data));
        SetButton(pressed, false, false);
        pressed = downTarget = dragTarget = pressHit = null; dragging = captured = false;
    }

    //最近的点击处理器阻止命中继续穿到父按钮。
    private static GameObject ClickTarget(GameObject start)
    {
        for (Transform t = start != null ? start.transform : null; t != null; t = t.parent)
            if (t.GetComponent<Button>() != null || t.GetComponent<IPointerClickHandler>() != null) return t.gameObject;
        return null;
    }

    //查找最近的事件接收节点。
    private static GameObject Find<T>(GameObject start)
    {
        for (Transform t = start != null ? start.transform : null; t != null; t = t.parent)
            if (t.GetComponent<T>() != null) return t.gameObject;
        return null;
    }

    //同一节点的多个监听组件依次接收事件。
    private static void Send<T>(GameObject target, Action<T> call)
    {
        if (target == null) return;
        foreach (T component in target.GetComponents<T>())
            if (component is not Behaviour behaviour || behaviour.isActiveAndEnabled) call(component);
    }

    //只更新按钮的临时状态，监听器与交互开关仍由模组控制。
    private static void SetButton(GameObject target, bool hover, bool held)
    {
        Selectable button = target != null ? target.GetComponentInParent<Selectable>() : null;
        if (button != null) { button.Hovered = hover; button.Held = held; }
    }
}
