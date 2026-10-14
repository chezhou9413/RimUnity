
using RimUnity.UI;
namespace RimUnity;

//统一控制子树透明度与鼠标命中。
public class CanvasGroup : MonoBehaviour
{
    public float alpha { get; set; } = 1f;
    public bool interactable { get; set; } = true;
    public bool blocksRaycasts { get; set; } = true;
    public bool ignoreParentGroups { get; set; }
}
