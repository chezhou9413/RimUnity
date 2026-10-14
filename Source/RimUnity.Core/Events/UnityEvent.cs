namespace RimUnity.Events;

//事件委托保留制作端的参数类型和数量。
public delegate void UnityAction();
public delegate void UnityAction<T>(T value);
public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
public delegate void UnityAction<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2);
public delegate void UnityAction<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3);

//导出器的持久回调由 EventBinder 绑定成与普通监听器一致的委托。
public class UnityEvent
{
    private event UnityAction listeners;
    public void AddListener(UnityAction listener) => listeners += listener;
    public void RemoveListener(UnityAction listener) => listeners -= listener;
    public void RemoveAllListeners() => listeners = null;
    public void Invoke() => listeners?.Invoke();
}

//单参数事件用于滑条、开关和滚动位置通知。
public class UnityEvent<T>
{
    private event UnityAction<T> listeners;
    public void AddListener(UnityAction<T> listener) => listeners += listener;
    public void RemoveListener(UnityAction<T> listener) => listeners -= listener;
    public void RemoveAllListeners() => listeners = null;
    public void Invoke(T value) => listeners?.Invoke(value);
}

//双参数事件支持制作端的动态参数绑定。
public class UnityEvent<T0, T1>
{
    private event UnityAction<T0, T1> listeners;
    public void AddListener(UnityAction<T0, T1> listener) => listeners += listener;
    public void RemoveListener(UnityAction<T0, T1> listener) => listeners -= listener;
    public void RemoveAllListeners() => listeners = null;
    public void Invoke(T0 arg0, T1 arg1) => listeners?.Invoke(arg0, arg1);
}

//三参数事件支持制作端的动态参数绑定。
public class UnityEvent<T0, T1, T2>
{
    private event UnityAction<T0, T1, T2> listeners;
    public void AddListener(UnityAction<T0, T1, T2> listener) => listeners += listener;
    public void RemoveListener(UnityAction<T0, T1, T2> listener) => listeners -= listener;
    public void RemoveAllListeners() => listeners = null;
    public void Invoke(T0 arg0, T1 arg1, T2 arg2) => listeners?.Invoke(arg0, arg1, arg2);
}

//四参数事件支持制作端的动态参数绑定。
public class UnityEvent<T0, T1, T2, T3>
{
    private event UnityAction<T0, T1, T2, T3> listeners;
    public void AddListener(UnityAction<T0, T1, T2, T3> listener) => listeners += listener;
    public void RemoveListener(UnityAction<T0, T1, T2, T3> listener) => listeners -= listener;
    public void RemoveAllListeners() => listeners = null;
    public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => listeners?.Invoke(arg0, arg1, arg2, arg3);
}
