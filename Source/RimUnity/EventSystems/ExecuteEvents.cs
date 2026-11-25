using System;
using System.Collections.Generic;
using Verse;

namespace RimUnity.EventSystems;

//对齐 UGUI 1.0.0 ExecuteEvents，统一处理组件激活检查、层级冒泡与重入安全的事件派发。
public static class ExecuteEvents
{
    private static readonly Stack<List<IEventSystemHandler>> s_handlerListPool = new();
    private static readonly Stack<List<Component>> s_componentListPool = new();
    private static readonly Stack<List<Transform>> s_transformListPool = new();
    private static readonly List<IEventSystemHandler> s_scratchHandlers = new();

    public delegate void EventFunction<T1>(T1 handler, BaseEventData eventData);

    public static EventFunction<IPointerEnterHandler> pointerEnterHandler { get; } = _Execute;
    public static EventFunction<IPointerExitHandler> pointerExitHandler { get; } = _Execute;
    public static EventFunction<IPointerMoveHandler> pointerMoveHandler { get; } = _Execute;
    public static EventFunction<IPointerDownHandler> pointerDownHandler { get; } = _Execute;
    public static EventFunction<IPointerUpHandler> pointerUpHandler { get; } = _Execute;
    public static EventFunction<IPointerClickHandler> pointerClickHandler { get; } = _Execute;
    public static EventFunction<IInitializePotentialDragHandler> initializePotentialDrag { get; } = _Execute;
    public static EventFunction<IBeginDragHandler> beginDragHandler { get; } = _Execute;
    public static EventFunction<IDragHandler> dragHandler { get; } = _Execute;
    public static EventFunction<IEndDragHandler> endDragHandler { get; } = _Execute;
    public static EventFunction<IDropHandler> dropHandler { get; } = _Execute;
    public static EventFunction<IScrollHandler> scrollHandler { get; } = _Execute;
    public static EventFunction<IUpdateSelectedHandler> updateSelectedHandler { get; } = _Execute;
    public static EventFunction<ISelectHandler> selectHandler { get; } = _Execute;
    public static EventFunction<IDeselectHandler> deselectHandler { get; } = _Execute;
    public static EventFunction<IMoveHandler> moveHandler { get; } = _Execute;
    public static EventFunction<ISubmitHandler> submitHandler { get; } = _Execute;
    public static EventFunction<ICancelHandler> cancelHandler { get; } = _Execute;

    public static T ValidateEventData<T>(BaseEventData data) where T : class
    {
        if (data is not T typedEventData)
        {
            throw new ArgumentException($"Invalid type: {data?.GetType()} passed to event expecting {typeof(T)}");
        }
        return typedEventData;
    }

    public static bool Execute<T>(GameObject target, BaseEventData eventData, EventFunction<T> functor) where T : IEventSystemHandler
    {
        if (target == null)
        {
            return false;
        }

        List<IEventSystemHandler> handlers = _Rent(s_handlerListPool);
        try
        {
            _GetEventList<T>(target, handlers);
            for (int index = 0; index < handlers.Count; index++)
            {
                T handler;
                try
                {
                    handler = (T)handlers[index];
                }
                catch (Exception exception)
                {
                    Log.Error($"[RimUnity] Type {handlers[index]?.GetType().Name} expected {typeof(T).Name} received.\n{exception}");
                    continue;
                }

                try
                {
                    functor(handler, eventData);
                }
                catch (Exception exception)
                {
                    Log.Error($"[RimUnity] Event callback exception on {target.name} ({typeof(T).Name}):\n{exception}");
                }
            }
            return handlers.Count > 0;
        }
        finally
        {
            _Return(s_handlerListPool, handlers);
        }
    }

    public static GameObject ExecuteHierarchy<T>(GameObject root, BaseEventData eventData, EventFunction<T> callbackFunction) where T : IEventSystemHandler
    {
        if (root == null)
        {
            return null;
        }

        List<Transform> transformChain = _Rent(s_transformListPool);
        try
        {
            _GetEventChain(root, transformChain);
            for (int index = 0; index < transformChain.Count; index++)
            {
                Transform currentTransform = transformChain[index];
                if (currentTransform == null)
                {
                    continue;
                }
                if (Execute(currentTransform.gameObject, eventData, callbackFunction))
                {
                    return currentTransform.gameObject;
                }
            }
            return null;
        }
        finally
        {
            _Return(s_transformListPool, transformChain);
        }
    }

    public static bool CanHandleEvent<T>(GameObject go) where T : IEventSystemHandler
    {
        _GetEventList<T>(go, s_scratchHandlers);
        bool canHandle = s_scratchHandlers.Count > 0;
        s_scratchHandlers.Clear();
        return canHandle;
    }

    public static GameObject GetEventHandler<T>(GameObject root) where T : IEventSystemHandler
    {
        if (root == null)
        {
            return null;
        }
        Transform currentTransform = root.transform;
        while (currentTransform != null)
        {
            if (CanHandleEvent<T>(currentTransform.gameObject))
            {
                return currentTransform.gameObject;
            }
            currentTransform = currentTransform.parent;
        }
        return null;
    }

    private static void _Execute(IPointerEnterHandler handler, BaseEventData eventData)
        => handler.OnPointerEnter(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IPointerExitHandler handler, BaseEventData eventData)
        => handler.OnPointerExit(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IPointerMoveHandler handler, BaseEventData eventData)
        => handler.OnPointerMove(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IPointerDownHandler handler, BaseEventData eventData)
        => handler.OnPointerDown(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IPointerUpHandler handler, BaseEventData eventData)
        => handler.OnPointerUp(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IPointerClickHandler handler, BaseEventData eventData)
        => handler.OnPointerClick(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IInitializePotentialDragHandler handler, BaseEventData eventData)
        => handler.OnInitializePotentialDrag(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IBeginDragHandler handler, BaseEventData eventData)
        => handler.OnBeginDrag(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IDragHandler handler, BaseEventData eventData)
        => handler.OnDrag(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IEndDragHandler handler, BaseEventData eventData)
        => handler.OnEndDrag(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IDropHandler handler, BaseEventData eventData)
        => handler.OnDrop(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IScrollHandler handler, BaseEventData eventData)
        => handler.OnScroll(ValidateEventData<PointerEventData>(eventData));
    private static void _Execute(IUpdateSelectedHandler handler, BaseEventData eventData)
        => handler.OnUpdateSelected(eventData);
    private static void _Execute(ISelectHandler handler, BaseEventData eventData)
        => handler.OnSelect(eventData);
    private static void _Execute(IDeselectHandler handler, BaseEventData eventData)
        => handler.OnDeselect(eventData);
    private static void _Execute(IMoveHandler handler, BaseEventData eventData)
        => handler.OnMove(ValidateEventData<AxisEventData>(eventData));
    private static void _Execute(ISubmitHandler handler, BaseEventData eventData)
        => handler.OnSubmit(eventData);
    private static void _Execute(ICancelHandler handler, BaseEventData eventData)
        => handler.OnCancel(eventData);

    private static bool _ShouldSendToComponent<T>(Component component) where T : IEventSystemHandler
    {
        if (component is not T)
        {
            return false;
        }
        if (component is Behaviour behaviour)
        {
            return behaviour.isActiveAndEnabled;
        }
        return true;
    }

    private static void _GetEventList<T>(GameObject targetObject, IList<IEventSystemHandler> results) where T : IEventSystemHandler
    {
        results.Clear();
        if (targetObject == null || !targetObject.activeInHierarchy)
        {
            return;
        }

        List<Component> components = _Rent(s_componentListPool);
        try
        {
            targetObject.GetComponents(components);
            for (int index = 0; index < components.Count; index++)
            {
                if (_ShouldSendToComponent<T>(components[index]))
                {
                    results.Add(components[index] as IEventSystemHandler);
                }
            }
        }
        finally
        {
            _Return(s_componentListPool, components);
        }
    }

    private static void _GetEventChain(GameObject root, IList<Transform> eventChain)
    {
        eventChain.Clear();
        if (root == null)
        {
            return;
        }
        Transform currentTransform = root.transform;
        while (currentTransform != null)
        {
            eventChain.Add(currentTransform);
            currentTransform = currentTransform.parent;
        }
    }

    private static List<T> _Rent<T>(Stack<List<T>> pool)
        => pool.Count > 0 ? pool.Pop() : new List<T>();

    private static void _Return<T>(Stack<List<T>> pool, List<T> list)
    {
        list.Clear();
        pool.Push(list);
    }
}
