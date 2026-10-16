namespace RimUnity;

//相机保存桥接自己的变换与投影；地图相机由宿主边界同步一次快照。
public sealed class Camera : MonoBehaviour
{
    internal static readonly System.Collections.Generic.HashSet<Camera> Active = new();
    public RenderTexture targetTexture { get; set; }
    internal RenderTexture ScreenTarget;
    public Color backgroundColor { get; set; } = Color.black;
    public CameraClearFlags clearFlags { get; set; } = CameraClearFlags.SolidColor;
    public float depth { get; set; }
    public Rect rect { get; set; } = new(0, 0, 1, 1);
    private void OnEnable() => Active.Add(this);
    private void OnDisable() { Active.Remove(this); ReleaseAttachments(); }
    internal Rendering.DirectionalShadow Shadow;
    private void OnDestroy() { Active.Remove(this); ReleaseAttachments(); }
    //隐藏相机只释放自身附件，外部指定的目标仍由调用者管理。
    private void ReleaseAttachments()
    {
        Shadow?.Dispose(); Shadow = null;
        if (ScreenTarget) Destroy(ScreenTarget);
        ScreenTarget = null;
    }
    //独立相机绘制显式目标或屏幕附件，地图主相机仍由宿主负责。
    public void Render() => Rendering.CameraRenderer.Render(this);
    //将画布绘入预乘透明附件，供 IMGUI 合成；不扫描地图模型和阴影。
    public void RenderUI() => Rendering.CameraRenderer.Render(this, true);
    public static Camera main { get; set; }
    public static Camera current => main;
    private Matrix4x4? projectionOverride, viewOverride;
    private float aspectOverride;
    public bool orthographic { get; set; }
    public float orthographicSize { get; set; } = 5;
    public float nearClipPlane { get; set; } = .3f;
    public float farClipPlane { get; set; } = 1000;
    public float fieldOfView { get; set; } = 60;
    public int cullingMask { get; set; } = -1;
    private Rect? pixelOverride;
    public Rect pixelRect
    {
        get => pixelOverride ?? new Rect(rect.x * (targetTexture?.width ?? Screen.width), rect.y * (targetTexture?.height ?? Screen.height),
            rect.width * (targetTexture?.width ?? Screen.width), rect.height * (targetTexture?.height ?? Screen.height));
        set => pixelOverride = value;
    }
    public int pixelWidth => (int)pixelRect.width;
    public int pixelHeight => (int)pixelRect.height;
    public float aspect { get => aspectOverride > 0 ? aspectOverride : pixelRect.width / pixelRect.height; set => aspectOverride = value; }
    public Matrix4x4 worldToCameraMatrix { get => viewOverride ?? (Matrix4x4.Scale(new(1, 1, -1)) * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse); set => viewOverride = value; }
    public Matrix4x4 cameraToWorldMatrix => worldToCameraMatrix.inverse;
    public Matrix4x4 projectionMatrix
    {
        get
        {
            if (projectionOverride.HasValue) return projectionOverride.Value;
            //Waylith 使用 Vulkan 的反向深度：近平面为一，远平面为零。
            float near = nearClipPlane, far = farClipPlane;
            if (orthographic) return new Matrix4x4 { m00 = 1 / (orthographicSize * aspect), m11 = -1 / orthographicSize, m22 = 1 / (far - near), m23 = far / (far - near), m33 = 1 };
            float f = 1 / Mathf.Tan(fieldOfView * Mathf.Deg2Rad * .5f);
            return new Matrix4x4 { m00 = f / aspect, m11 = -f, m22 = near / (far - near), m23 = near * far / (far - near), m32 = -1 };
        }
        set => projectionOverride = value;
    }
    public void ResetProjectionMatrix() => projectionOverride = null;
    public void ResetWorldToCameraMatrix() => viewOverride = null;
    public void ResetAspect() => aspectOverride = 0;

    //公开屏幕坐标保持 Unity 左下原点，投影的 Y 翻转在这里统一处理。
    public Vector3 WorldToScreenPoint(Vector3 point)
    {
        Vector4 view = worldToCameraMatrix * new Vector4(point.x, point.y, point.z, 1);
        Vector4 clip = projectionMatrix * view;
        if (Mathf.Abs(clip.w) < 1e-7f) return Vector3.zero;
        return new(pixelRect.x + (clip.x / clip.w + 1) * .5f * pixelRect.width,
            pixelRect.y + (1 - clip.y / clip.w) * .5f * pixelRect.height, -view.z);
    }
    public Ray ScreenPointToRay(Vector3 point)
    {
        float x = (point.x - pixelRect.x) / pixelRect.width * 2 - 1;
        float y = 1 - (point.y - pixelRect.y) / pixelRect.height * 2;
        Matrix4x4 inverse = (projectionMatrix * worldToCameraMatrix).inverse;
        Vector4 near = inverse * new Vector4(x, y, 1, 1), far = inverse * new Vector4(x, y, 0, 1);
        Vector3 origin = (Vector3)near / near.w;
        return new Ray(origin, (Vector3)far / far.w - origin);
    }
    public Vector3 ScreenToWorldPoint(Vector3 point)
    {
        Ray ray = ScreenPointToRay(point);
        float along = Vector3.Dot(ray.direction, transform.forward);
        float originDepth = Vector3.Dot(ray.origin - transform.position, transform.forward);
        return ray.GetPoint((point.z - originDepth) / along);
    }
}

public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = Color, Depth = 3, Nothing = 4 }
