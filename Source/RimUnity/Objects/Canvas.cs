
using RimUnity.UI;
namespace RimUnity;

public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }

//管理画布的显示模式、缩放和绘制顺序。
public class Canvas : MonoBehaviour
{
    public RenderMode renderMode { get; set => SetProperty(ref field, value); }
    public Camera worldCamera { get; set => SetProperty(ref field, value); }
    public int sortingOrder { get; set; }
    public bool overrideSorting { get; set; }
    public float planeDistance { get; set; } = 100;
    public Canvas rootCanvas { get { Canvas root = this; for (Transform t = transform.parent; t != null; t = t.parent) { var c = t.GetComponent<Canvas>(); if (c != null) root = c; } return root; } }
    public bool isRootCanvas => rootCanvas == this;
    internal RenderMode EffectiveMode => rootCanvas.renderMode;
    internal int EffectiveOrder => overrideSorting || isRootCanvas ? sortingOrder : rootCanvas.sortingOrder;
    public float scaleFactor { get; set => SetProperty(ref field, value); } = 1f;
    public float referencePixelsPerUnit { get; set => SetProperty(ref field, value); } = 100f;

    //支持模组通过 Ugui.CreateObject 动态创建画布。
    private void OnEnable() => UI.Runtime.UguiRuntime.Register(this);
    private void OnDestroy() => UI.Runtime.WorldUiSubmission.Release(this);

    //让动态添加内容后需要立刻测量尺寸的调用者刷新布局。
    public static void ForceUpdateCanvases() => UI.Runtime.UguiRuntime.Prepare();

    //屏幕变化后按 CanvasScaler 的 Unity 规则更新逻辑分辨率。
    internal void Prepare()
    {
        if (!isRootCanvas) { scaleFactor = rootCanvas.scaleFactor; referencePixelsPerUnit = rootCanvas.referencePixelsPerUnit; return; }
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler != null && scaler.enabled)
        {
            referencePixelsPerUnit = scaler.referencePixelsPerUnit;
            scaleFactor = scaler.scaleFactor;
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPhysicalSize)
            {
                float dpi = Screen.dpi > 0 ? Screen.dpi : scaler.fallbackScreenDPI;
                float units = scaler.physicalUnit switch { CanvasScaler.Unit.Centimeters => 2.54f, CanvasScaler.Unit.Millimeters => 25.4f, CanvasScaler.Unit.Points => 72, CanvasScaler.Unit.Picas => 6, _ => 1 };
                scaleFactor = dpi / units; referencePixelsPerUnit = scaler.referencePixelsPerUnit * units / scaler.defaultSpriteDPI;
            }
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                float x = Screen.width / scaler.referenceResolution.x;
                float y = Screen.height / scaler.referenceResolution.y;
                scaleFactor = scaler.screenMatchMode switch
                {
                    CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(x, y),
                    CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(x, y),
                    _ => Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(x, 2f), Mathf.Log(y, 2f), scaler.matchWidthOrHeight))
                };
            }
        }
        if (scaleFactor <= 0f) throw new System.InvalidOperationException("Canvas 的缩放系数必须大于零。");
        if (renderMode == RenderMode.WorldSpace) return;
        RectTransform rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(Screen.width / scaleFactor, Screen.height / scaleFactor);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        if (renderMode == RenderMode.ScreenSpaceCamera)
        {
            Camera camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null) throw new System.InvalidOperationException("Camera Canvas 需要 worldCamera 或 Camera.main。");
            float distance = Mathf.Max(camera.nearClipPlane + .01f, planeDistance);
            float height = camera.orthographic ? camera.orthographicSize * 2 : 2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
            rt.pivot = new Vector2(.5f, .5f); rt.sizeDelta = new Vector2(camera.pixelWidth / scaleFactor, camera.pixelHeight / scaleFactor);
            rt.localScale = Vector3.one * (height / rt.sizeDelta.y);
            rt.position = camera.transform.position + camera.transform.forward * distance; rt.rotation = camera.transform.rotation;
        }
    }
}
