using System;
using RimUnity.EventSystems;
using UnityEngine;
using Verse;

namespace RimUnity.UI.Runtime;

//对齐 UGUI 1.0.0 PointerInputModule / StandaloneInputModule / BaseInputModule 语义：
//按左右中三键独立维护 PointerEventData 状态，经 ExecuteEvents 派发悬停/按下/拖动/释放/点击/滚轮，
//同时适配 RimWorld 1.7 Waylith ImguiDispatch 复用单 passEvent 实例与 Window_UGUI 路由模型。
internal static class UguiPointer
{
    private const float DOUBLE_CLICK_TIME = 0.3f;
    private const float SCROLL_EVENT_UNITS_PER_NOTCH = 3f;
    private const int BUTTON_COUNT = 3;

    private static readonly ButtonSlot[] s_slots =
    {
        new(PointerEventData.InputButton.Left, -1),
        new(PointerEventData.InputButton.Right, -2),
        new(PointerEventData.InputButton.Middle, -3)
    };

    private static Vector2 s_lastMousePosition;
    private static bool s_hasLastMousePosition;
    //宿主 ImguiDispatch 复用单个 passEvent 实例，按单次派发轮次标记是否已被处理。
    private static bool s_handledInCurrentPass;

    private sealed class ButtonSlot
    {
        public readonly PointerEventData.InputButton Button;
        public readonly int PointerId;
        public PointerEventData Data;
        public bool Captured;
        public Canvas CapturedCanvas;

        public ButtonSlot(PointerEventData.InputButton button, int pointerId)
        {
            Button = button;
            PointerId = pointerId;
        }

        public PointerEventData EnsureData()
        {
            if (Data == null)
            {
                Data = new PointerEventData(EventSystem.current)
                {
                    pointerId = PointerId,
                    button = Button
                };
            }
            return Data;
        }
    }

    private static ButtonSlot LeftSlot => s_slots[0];

    internal static bool IsOverUi => LeftSlot.Data != null && LeftSlot.Data.pointerEnter != null;
    internal static bool IsCapturing => s_slots[0].Captured || s_slots[1].Captured || s_slots[2].Captured;

    //在宿主把事件路由给 IMGUI 窗口之前，先让顶层 Overlay 画布或正在拖拽的交互处理输入。
    internal static void ProcessPreWindow()
    {
        s_handledInCurrentPass = false;
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }
        if (currentEvent.type is EventType.Layout or EventType.Repaint or EventType.Used or EventType.Ignore)
        {
            return;
        }

        if (currentEvent.type is EventType.KeyDown or EventType.KeyUp)
        {
            GameObject selectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selectedObject == null)
            {
                return;
            }
            Canvas selectedCanvas = selectedObject.GetComponentInParent<Canvas>();
            if (selectedCanvas == null || selectedCanvas.EffectiveMode != RenderMode.ScreenSpaceOverlay)
            {
                return;
            }
            UguiKeyboard.Process();
            if (currentEvent.type == EventType.Used)
            {
                s_handledInCurrentPass = true;
            }
            return;
        }

        if (!currentEvent.isMouse && currentEvent.type != EventType.ScrollWheel)
        {
            return;
        }

        UguiRuntime.Prepare();
        //GUI.DispatchInputToWindows 执行于 UI.ApplyUIScaling() 之前（GUI.matrix 为单位阵），mousePosition 为物理像素，需换算为 GUI 单位。
        float inverseScale = Prefs.UIScale > 0f ? 1f / Prefs.UIScale : 1f;
        Vector2 guiPoint = new(currentEvent.mousePosition.x * inverseScale, currentEvent.mousePosition.y * inverseScale);

        Canvas capturedCanvas = _GetCapturedCanvas(currentEvent);
        if (IsCapturing)
        {
            _ProcessCore(currentEvent, guiPoint, filterCanvas: capturedCanvas, overlayOnly: false);
            s_handledInCurrentPass = true;
            return;
        }

        RaycastResult overlayHit = _HitTest(guiPoint, filterCanvas: null, overlayOnly: true);
        if (overlayHit.gameObject == null)
        {
            if (currentEvent.type == EventType.MouseDown)
            {
                GameObject selectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                Canvas selectedCanvas = selectedObject != null ? selectedObject.GetComponentInParent<Canvas>() : null;
                if (selectedCanvas != null && selectedCanvas.EffectiveMode == RenderMode.ScreenSpaceOverlay)
                {
                    _DeselectIfSelectionChanged(null, LeftSlot.EnsureData());
                }
            }
            return;
        }

        _ProcessCore(currentEvent, guiPoint, filterCanvas: null, overlayOnly: true);
        s_handledInCurrentPass = true;
    }

    //由承载 UGUI 画布的 Verse.Window 在其成为顶层窗口并收到宿主派发事件时调用。
    internal static void ProcessWindowCanvas(Canvas canvas, Vector2 windowOffset)
    {
        Event currentEvent = Event.current;
        if (canvas == null || currentEvent == null || s_handledInCurrentPass)
        {
            return;
        }
        if (currentEvent.type is EventType.Layout or EventType.Repaint or EventType.Used or EventType.Ignore)
        {
            return;
        }

        UguiRuntime.Prepare();
        if (currentEvent.type is EventType.KeyDown or EventType.KeyUp)
        {
            UguiKeyboard.Process();
            if (currentEvent.type == EventType.Used)
            {
                s_handledInCurrentPass = true;
            }
            return;
        }

        if (!currentEvent.isMouse && currentEvent.type != EventType.ScrollWheel)
        {
            return;
        }

        Vector2 guiPoint = new(currentEvent.mousePosition.x + windowOffset.x, currentEvent.mousePosition.y + windowOffset.y);
        _ProcessCore(currentEvent, guiPoint, filterCanvas: canvas.rootCanvas, overlayOnly: false);
        //仅在当前窗口画布实际消费事件或正处于捕获状态时拦截后续窗口，避免空窗口阻塞同帧其他 UGUI 窗口。
        if (currentEvent.type == EventType.Used || IsCapturing)
        {
            s_handledInCurrentPass = true;
        }
    }

    //在 UIRootOnGUI 阶段处理悬停更新、键盘导航以及未被 Window_UGUI 消费的画布输入。
    internal static void Process()
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }
        if (currentEvent.type == EventType.Layout)
        {
            s_handledInCurrentPass = false;
            return;
        }
        if (s_handledInCurrentPass || currentEvent.type is EventType.Used or EventType.Ignore)
        {
            return;
        }

        UguiKeyboard.Process();
        if (!currentEvent.isMouse && currentEvent.type != EventType.ScrollWheel && currentEvent.type != EventType.Repaint)
        {
            return;
        }

        Vector2 guiPoint = new(currentEvent.mousePosition.x, currentEvent.mousePosition.y);
        _ProcessCore(currentEvent, guiPoint, filterCanvas: null, overlayOnly: false);
        if (currentEvent.type != EventType.Repaint)
        {
            s_handledInCurrentPass = true;
        }
    }

    private static Canvas _GetCapturedCanvas(Event currentEvent)
    {
        int buttonIndex = currentEvent.button;
        if (buttonIndex >= 0 && buttonIndex < BUTTON_COUNT && s_slots[buttonIndex].Captured)
        {
            return s_slots[buttonIndex].CapturedCanvas;
        }
        for (int index = 0; index < BUTTON_COUNT; index++)
        {
            if (s_slots[index].Captured)
            {
                return s_slots[index].CapturedCanvas;
            }
        }
        return null;
    }

    private static void _ProcessCore(Event currentEvent, Vector2 guiPoint, Canvas filterCanvas, bool overlayOnly)
    {
        Vector2 screenPosition = new(guiPoint.x * Prefs.UIScale, Screen.height - guiPoint.y * Prefs.UIScale);
        Vector2 pointerDelta = s_hasLastMousePosition ? screenPosition - s_lastMousePosition : Vector2.zero;
        if (currentEvent.type == EventType.MouseDrag && pointerDelta.sqrMagnitude == 0f && currentEvent.delta.sqrMagnitude > 0f)
        {
            pointerDelta = new Vector2(currentEvent.delta.x, -currentEvent.delta.y);
        }
        s_lastMousePosition = screenPosition;
        s_hasLastMousePosition = true;

        RaycastResult hitResult = _HitTest(guiPoint, filterCanvas, overlayOnly);
        GameObject currentOverObject = hitResult.gameObject;
        Camera enterCamera = _GetCameraFor(currentOverObject);

        //与 StandaloneInputModule 一致：Left 槽位维护当前指针位置、射线结果与悬停链。
        PointerEventData leftData = LeftSlot.EnsureData();
        leftData.Reset();
        leftData.position = screenPosition;
        leftData.delta = pointerDelta;
        leftData.pointerCurrentRaycast = hitResult;
        leftData.enterEventCamera = enterCamera;
        leftData.scrollDelta = currentEvent.type == EventType.ScrollWheel
            ? new Vector2(-currentEvent.delta.x, -currentEvent.delta.y) / SCROLL_EVENT_UNITS_PER_NOTCH
            : Vector2.zero;

        //把最新指针位置/射线同步到右、中键槽位（与 PointerInputModule.CopyFromTo 一致）。
        for (int index = 1; index < BUTTON_COUNT; index++)
        {
            PointerEventData otherData = s_slots[index].EnsureData();
            otherData.Reset();
            _CopyFromTo(leftData, otherData);
        }

        //若已捕获目标所属画布已停用或被销毁，立即按 UGUI 释放流程收尾，防止捕获卡死。
        for (int index = 0; index < BUTTON_COUNT; index++)
        {
            ButtonSlot slot = s_slots[index];
            if (!slot.Captured)
            {
                continue;
            }
            bool invalidCanvas = slot.CapturedCanvas == null || !slot.CapturedCanvas.isActiveAndEnabled;
            bool invalidPress = slot.Data.rawPointerPress == null || !slot.Data.rawPointerPress.activeInHierarchy;
            if (invalidCanvas || (invalidPress && !slot.Data.dragging))
            {
                _ReleaseMouse(slot, currentOverObject);
            }
        }

        int buttonIndex = currentEvent.button;
        bool validButton = buttonIndex >= 0 && buttonIndex < BUTTON_COUNT;

        if (currentEvent.type == EventType.MouseDown && validButton)
        {
            ButtonSlot slot = s_slots[buttonIndex];
            if (currentOverObject != null)
            {
                slot.Captured = true;
                Canvas parentCanvas = currentOverObject.GetComponentInParent<Canvas>();
                slot.CapturedCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
                _ProcessMousePressDown(slot.Data, currentOverObject);
                _HandlePointerExitAndEnter(leftData, currentOverObject);
                _SyncHoveredToOtherSlots(leftData);
                currentEvent.Use();
            }
            else
            {
                //点击空白区域时按 UGUI 规则清除当前焦点与连击状态。
                _SetPointerPress(slot.Data, null);
                slot.Data.clickCount = 0;
                _DeselectIfSelectionChanged(null, slot.Data);
                _HandlePointerExitAndEnter(leftData, null);
                _SyncHoveredToOtherSlots(leftData);
            }
            return;
        }

        if (currentEvent.type == EventType.MouseDrag && validButton)
        {
            ButtonSlot slot = s_slots[buttonIndex];
            _HandlePointerExitAndEnter(leftData, currentOverObject);
            _SyncHoveredToOtherSlots(leftData);
            if (slot.Captured)
            {
                _ProcessDrag(slot.Data);
                currentEvent.Use();
            }
            else if (currentOverObject != null)
            {
                currentEvent.Use();
            }
            return;
        }

        if (currentEvent.type == EventType.MouseUp && validButton)
        {
            ButtonSlot slot = s_slots[buttonIndex];
            _HandlePointerExitAndEnter(leftData, currentOverObject);
            _SyncHoveredToOtherSlots(leftData);
            if (slot.Captured)
            {
                _ReleaseMouse(slot, currentOverObject);
                currentEvent.Use();
            }
            else if (currentOverObject != null)
            {
                currentEvent.Use();
            }
            return;
        }

        if (currentEvent.type == EventType.ScrollWheel)
        {
            _HandlePointerExitAndEnter(leftData, currentOverObject);
            _SyncHoveredToOtherSlots(leftData);
            if (currentOverObject != null && !Mathf.Approximately(leftData.scrollDelta.sqrMagnitude, 0f))
            {
                GameObject scrollHandlerObject = ExecuteEvents.GetEventHandler<IScrollHandler>(currentOverObject);
                ExecuteEvents.ExecuteHierarchy(scrollHandlerObject, leftData, ExecuteEvents.scrollHandler);
                currentEvent.Use();
            }
            return;
        }

        //Repaint / MouseMove：刷新悬停退出与进入（ImguiDispatch 不单独产生 MouseMove 事件）。
        _HandlePointerExitAndEnter(leftData, currentOverObject);
        _SyncHoveredToOtherSlots(leftData);
        if ((currentOverObject != null || IsCapturing) && currentEvent.isMouse)
        {
            currentEvent.Use();
        }
    }

    //对齐 UGUI 1.0.0 StandaloneInputModule.ProcessMousePress (Pressed 分支)。
    private static void _ProcessMousePressDown(PointerEventData pointerEvent, GameObject currentOverObject)
    {
        pointerEvent.eligibleForClick = true;
        pointerEvent.delta = Vector2.zero;
        pointerEvent.dragging = false;
        pointerEvent.useDragThreshold = true;
        pointerEvent.pressPosition = pointerEvent.position;
        pointerEvent.pointerPressRaycast = pointerEvent.pointerCurrentRaycast;
        pointerEvent.pressEventCamera = pointerEvent.enterEventCamera;

        _DeselectIfSelectionChanged(currentOverObject, pointerEvent);

        float currentTime = Time.unscaledTime;
        if (currentTime - pointerEvent.clickTime >= DOUBLE_CLICK_TIME)
        {
            pointerEvent.clickCount = 0;
        }

        GameObject newPressedObject = ExecuteEvents.ExecuteHierarchy(currentOverObject, pointerEvent, ExecuteEvents.pointerDownHandler);
        GameObject newClickObject = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverObject);

        if (newPressedObject == null)
        {
            newPressedObject = newClickObject;
        }

        if (newPressedObject == pointerEvent.lastPress)
        {
            if (currentTime - pointerEvent.clickTime < DOUBLE_CLICK_TIME)
            {
                ++pointerEvent.clickCount;
            }
            else
            {
                pointerEvent.clickCount = 1;
            }
        }
        else
        {
            pointerEvent.clickCount = 1;
        }

        _SetPointerPress(pointerEvent, newPressedObject);
        pointerEvent.rawPointerPress = currentOverObject;
        pointerEvent.pointerClick = newClickObject;
        pointerEvent.clickTime = currentTime;

        pointerEvent.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentOverObject);
        if (pointerEvent.pointerDrag != null)
        {
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.initializePotentialDrag);
        }
    }

    //对齐 UGUI 1.0.0 PointerInputModule.ProcessDrag。
    private static void _ProcessDrag(PointerEventData pointerEvent)
    {
        if (!pointerEvent.IsPointerMoving() || pointerEvent.pointerDrag == null)
        {
            return;
        }

        int dragThreshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10;
        if (!pointerEvent.dragging && _ShouldStartDrag(pointerEvent.pressPosition, pointerEvent.position, dragThreshold, pointerEvent.useDragThreshold))
        {
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.beginDragHandler);
            pointerEvent.dragging = true;
        }

        if (pointerEvent.dragging)
        {
            if (pointerEvent.pointerPress != pointerEvent.pointerDrag)
            {
                ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);
                pointerEvent.eligibleForClick = false;
                _SetPointerPress(pointerEvent, null);
                pointerEvent.rawPointerPress = null;
            }
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.dragHandler);
        }
    }

    //对齐 UGUI 1.0.0 StandaloneInputModule.ReleaseMouse。
    private static void _ReleaseMouse(ButtonSlot slot, GameObject currentOverObject)
    {
        PointerEventData pointerEvent = slot.Data;
        ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);

        GameObject pointerClickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverObject);
        if (pointerEvent.pointerClick == pointerClickHandler && pointerEvent.eligibleForClick)
        {
            ExecuteEvents.Execute(pointerEvent.pointerClick, pointerEvent, ExecuteEvents.pointerClickHandler);
        }

        if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
        {
            ExecuteEvents.ExecuteHierarchy(currentOverObject, pointerEvent, ExecuteEvents.dropHandler);
        }

        pointerEvent.eligibleForClick = false;
        _SetPointerPress(pointerEvent, null);
        pointerEvent.rawPointerPress = null;
        pointerEvent.pointerClick = null;

        if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
        {
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.endDragHandler);
        }

        pointerEvent.dragging = false;
        pointerEvent.pointerDrag = null;
        slot.Captured = false;
        slot.CapturedCanvas = null;

        if (slot == LeftSlot && currentOverObject != pointerEvent.pointerEnter)
        {
            _HandlePointerExitAndEnter(pointerEvent, null);
            _HandlePointerExitAndEnter(pointerEvent, currentOverObject);
            _SyncHoveredToOtherSlots(pointerEvent);
        }
    }

    //同步更新 lastPress，保持与 UGUI PointerEventData.pointerPress setter 相同语义且不破坏字段二进制兼容。
    private static void _SetPointerPress(PointerEventData pointerEvent, GameObject value)
    {
        if (pointerEvent.pointerPress == value)
        {
            return;
        }
        pointerEvent.lastPress = pointerEvent.pointerPress;
        pointerEvent.pointerPress = value;
    }

    //对齐 UGUI 1.0.0 BaseInputModule.HandlePointerExitAndEnter (sendPointerHoverToParent = true)。
    private static void _HandlePointerExitAndEnter(PointerEventData currentPointerData, GameObject newEnterTarget)
    {
        if (newEnterTarget == null || currentPointerData.pointerEnter == null)
        {
            int hoveredCount = currentPointerData.hovered.Count;
            for (int index = 0; index < hoveredCount; index++)
            {
                currentPointerData.fullyExited = true;
                ExecuteEvents.Execute(currentPointerData.hovered[index], currentPointerData, ExecuteEvents.pointerMoveHandler);
                ExecuteEvents.Execute(currentPointerData.hovered[index], currentPointerData, ExecuteEvents.pointerExitHandler);
            }
            currentPointerData.hovered.Clear();

            if (newEnterTarget == null)
            {
                currentPointerData.pointerEnter = null;
                return;
            }
        }

        if (currentPointerData.pointerEnter == newEnterTarget && newEnterTarget != null)
        {
            if (currentPointerData.IsPointerMoving())
            {
                int hoveredCount = currentPointerData.hovered.Count;
                for (int index = 0; index < hoveredCount; index++)
                {
                    ExecuteEvents.Execute(currentPointerData.hovered[index], currentPointerData, ExecuteEvents.pointerMoveHandler);
                }
            }
            return;
        }

        GameObject commonRoot = _FindCommonRoot(currentPointerData.pointerEnter, newEnterTarget);

        if (currentPointerData.pointerEnter != null)
        {
            Transform currentTransform = currentPointerData.pointerEnter.transform;
            while (currentTransform != null)
            {
                if (commonRoot != null && commonRoot.transform == currentTransform)
                {
                    break;
                }

                Transform parentTransform = currentTransform.parent;
                currentPointerData.fullyExited = currentTransform.gameObject != commonRoot && currentPointerData.pointerEnter != newEnterTarget;
                ExecuteEvents.Execute(currentTransform.gameObject, currentPointerData, ExecuteEvents.pointerMoveHandler);
                ExecuteEvents.Execute(currentTransform.gameObject, currentPointerData, ExecuteEvents.pointerExitHandler);
                currentPointerData.hovered.Remove(currentTransform.gameObject);
                currentTransform = parentTransform;
            }
        }

        GameObject oldPointerEnter = currentPointerData.pointerEnter;
        currentPointerData.pointerEnter = newEnterTarget;
        if (newEnterTarget != null)
        {
            Transform currentTransform = newEnterTarget.transform;
            while (currentTransform != null)
            {
                currentPointerData.reentered = currentTransform.gameObject == commonRoot && currentTransform.gameObject != oldPointerEnter;
                if (currentPointerData.reentered)
                {
                    break;
                }

                Transform parentTransform = currentTransform.parent;
                ExecuteEvents.Execute(currentTransform.gameObject, currentPointerData, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(currentTransform.gameObject, currentPointerData, ExecuteEvents.pointerMoveHandler);
                currentPointerData.hovered.Add(currentTransform.gameObject);
                currentTransform = parentTransform;
                if (commonRoot != null && commonRoot.transform == currentTransform)
                {
                    break;
                }
            }
        }
    }

    private static void _DeselectIfSelectionChanged(GameObject currentOverObject, BaseEventData pointerEvent)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return;
        }
        GameObject selectHandlerObject = ExecuteEvents.GetEventHandler<ISelectHandler>(currentOverObject);
        if (selectHandlerObject != eventSystem.currentSelectedGameObject)
        {
            eventSystem.SetSelectedGameObject(null, pointerEvent);
        }
    }

    private static bool _ShouldStartDrag(Vector2 pressPosition, Vector2 currentPosition, float threshold, bool useDragThreshold)
    {
        if (!useDragThreshold)
        {
            return true;
        }
        return (pressPosition - currentPosition).sqrMagnitude >= threshold * threshold;
    }

    private static void _CopyFromTo(PointerEventData source, PointerEventData destination)
    {
        destination.position = source.position;
        destination.delta = source.delta;
        destination.scrollDelta = source.scrollDelta;
        destination.pointerCurrentRaycast = source.pointerCurrentRaycast;
        destination.pointerEnter = source.pointerEnter;
        destination.enterEventCamera = source.enterEventCamera;
        destination.hovered.Clear();
        destination.hovered.AddRange(source.hovered);
    }

    private static void _SyncHoveredToOtherSlots(PointerEventData leftData)
    {
        for (int index = 1; index < BUTTON_COUNT; index++)
        {
            PointerEventData otherData = s_slots[index].Data;
            if (otherData == null)
            {
                continue;
            }
            otherData.pointerEnter = leftData.pointerEnter;
            otherData.hovered.Clear();
            otherData.hovered.AddRange(leftData.hovered);
        }
    }

    private static GameObject _FindCommonRoot(GameObject firstObject, GameObject secondObject)
    {
        if (firstObject == null || secondObject == null)
        {
            return null;
        }
        for (Transform firstTransform = firstObject.transform; firstTransform != null; firstTransform = firstTransform.parent)
        {
            for (Transform secondTransform = secondObject.transform; secondTransform != null; secondTransform = secondTransform.parent)
            {
                if (firstTransform == secondTransform)
                {
                    return firstTransform.gameObject;
                }
            }
        }
        return null;
    }

    private static RaycastResult _HitTest(Vector2 guiPoint, Canvas filterCanvas, bool overlayOnly)
    {
        Vector2 screenPosition = new(guiPoint.x * Prefs.UIScale, Screen.height - guiPoint.y * Prefs.UIScale);
        for (int index = UguiRuntime.Entries.Count - 1; index >= 0; index--)
        {
            UguiRuntime.Entry entry = UguiRuntime.Entries[index];
            if (!entry.Raycast)
            {
                continue;
            }
            if (overlayOnly && entry.Canvas.EffectiveMode != RenderMode.ScreenSpaceOverlay)
            {
                continue;
            }
            if (filterCanvas != null && entry.Canvas.rootCanvas != filterCanvas)
            {
                continue;
            }
            if (UguiRuntime.Contains(entry, guiPoint))
            {
                return new RaycastResult
                {
                    gameObject = entry.Rect.gameObject,
                    screenPosition = screenPosition,
                    depth = entry.Order,
                    sortingOrder = entry.Canvas.EffectiveOrder
                };
            }
        }
        return default;
    }

    private static Camera _GetCameraFor(GameObject targetObject)
    {
        if (targetObject == null)
        {
            return null;
        }
        Canvas canvas = targetObject.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }
        return canvas.rootCanvas.worldCamera != null ? canvas.rootCanvas.worldCamera : Camera.main;
    }
}
