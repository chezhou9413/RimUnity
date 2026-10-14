namespace RimUnity;

//组件开关和对象层级开关共同决定是否参与生命周期。
public class Behaviour : Component
{
    private bool active = true;
    public bool enabled
    {
        get => active;
        set
        {
            Runtime.NativeRuntime.AssertThread();
            if (active == value) return;
            active = value;
            if (this is MonoBehaviour mono) Runtime.NativeRuntime.Refresh(mono);
            NotifyPropertyChanged();
        }
    }
    public bool isActiveAndEnabled => !Destroyed && !Destroying && active && Owner != null && Owner.activeInHierarchy;
}
