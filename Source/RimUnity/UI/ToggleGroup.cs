using System.Collections.Generic;
using System.Linq;

namespace RimUnity.UI;

//维护组内互斥关系，对齐 UGUI 1.0.0 ToggleGroup。
public class ToggleGroup : MonoBehaviour
{
    public bool allowSwitchOff { get; set; }
    private readonly List<Toggle> toggles = new();

    protected virtual void Start() => EnsureValidState();
    protected virtual void OnEnable() => EnsureValidState();

    public void RegisterToggle(Toggle toggle) { if (toggle != null && !toggles.Contains(toggle)) toggles.Add(toggle); }
    public void UnregisterToggle(Toggle toggle) => toggles.Remove(toggle);

    public void NotifyToggleOn(Toggle toggle, bool sendCallback = true)
    {
        for (int i = 0; i < toggles.Count; i++)
        {
            if (i >= toggles.Count) break;
            Toggle current = toggles[i];
            if (current == toggle || current == null) continue;
            if (sendCallback) current.isOn = false;
            else current.SetIsOnWithoutNotify(false);
        }
    }

    public void EnsureValidState()
    {
        if (!allowSwitchOff && !AnyTogglesOn() && toggles.Count != 0)
        {
            toggles[0].isOn = true;
            NotifyToggleOn(toggles[0]);
        }
        IEnumerable<Toggle> active = ActiveToggles();
        if (active.Count() > 1)
        {
            Toggle first = GetFirstActiveToggle();
            for (int i = 0; i < toggles.Count; i++)
            {
                if (toggles[i] != first && toggles[i].isOn)
                    toggles[i].isOn = false;
            }
        }
    }

    public void SetAllTogglesOff(bool sendCallback = true)
    {
        bool oldAllowSwitchOff = allowSwitchOff;
        allowSwitchOff = true;
        for (int i = 0; i < toggles.Count; i++)
        {
            if (i >= toggles.Count) break;
            if (sendCallback) toggles[i].isOn = false;
            else toggles[i].SetIsOnWithoutNotify(false);
        }
        allowSwitchOff = oldAllowSwitchOff;
    }

    public IEnumerable<Toggle> ActiveToggles() => toggles.Where(t => t != null && t.isOn);
    public Toggle GetFirstActiveToggle() => ActiveToggles().FirstOrDefault();
    public bool AnyTogglesOn() => toggles.Find(t => t != null && t.isOn) != null;
}
