using System.Collections.Generic;
using System.Linq;

namespace RimUnity.UI;

//维护组内互斥关系，禁用的控件不参与选择。
public class ToggleGroup : MonoBehaviour
{
    public bool allowSwitchOff { get; set; }
    private readonly List<Toggle> toggles = new();
    internal void Register(Toggle toggle) { if (!toggles.Contains(toggle)) toggles.Add(toggle); }
    internal void Unregister(Toggle toggle) => toggles.Remove(toggle);
    internal bool CanDisable(Toggle toggle) => allowSwitchOff || toggles.Any(t => t != toggle && t.isOn);

    //先改变其它成员，再由发起控件发出自己的事件。
    internal void Activate(Toggle selected, bool notify)
    {
        foreach (Toggle toggle in toggles)
            if (toggle != selected) toggle.Set(false, notify);
    }

    public IEnumerable<Toggle> ActiveToggles() => toggles.Where(t => t.isOn);
    public bool AnyTogglesOn() => toggles.Any(t => t.isOn);
}
