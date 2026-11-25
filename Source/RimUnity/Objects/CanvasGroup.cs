using RimUnity.UI;
namespace RimUnity;

//统一控制子树透明度与鼠标命中。
public class CanvasGroup : MonoBehaviour
{
    private float _groupAlpha = 1f;
    public float alpha { get => _groupAlpha; set => _groupAlpha = Mathf.Clamp01(value); }
    public bool interactable { get; set; } = true;
    public bool blocksRaycasts { get; set; } = true;
    public bool ignoreParentGroups { get; set; }
}
