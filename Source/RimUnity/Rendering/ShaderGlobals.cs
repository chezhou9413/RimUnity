
namespace RimUnity.Rendering;

//统一提供 Unity 常用时间向量，不占用宿主 Frame 中的光照字段。
internal static partial class ShaderGlobals
{
    private static int frame = -1;
    private static readonly int TimeId = Shader.PropertyToID("_Time"), SinId = Shader.PropertyToID("_SinTime"), CosId = Shader.PropertyToID("_CosTime"), DeltaId = Shader.PropertyToID("unity_DeltaTime");
    internal static void Update()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        UpdateMapLighting();
        Camera camera = Camera.main;
        if (camera != null) SetCamera(camera);
        float t = Time.time, dt = Time.deltaTime, smooth = Time.smoothDeltaTime;
        Shader.SetGlobalVector(TimeId, new Vector4(t / 20, t, t * 2, t * 3));
        Shader.SetGlobalVector(SinId, new Vector4(Mathf.Sin(t / 8), Mathf.Sin(t / 4), Mathf.Sin(t / 2), Mathf.Sin(t)));
        Shader.SetGlobalVector(CosId, new Vector4(Mathf.Cos(t / 8), Mathf.Cos(t / 4), Mathf.Cos(t / 2), Mathf.Cos(t)));
        Shader.SetGlobalVector(DeltaId, new Vector4(dt, dt > 0 ? 1 / dt : 0, smooth, smooth > 0 ? 1 / smooth : 0));
    }

    //离屏渲染前切换相机参数，完成后恢复地图相机。
    internal static void SetCamera(Camera camera)
    {
            Vector3 position = camera.transform.position;
            Shader.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(position.x, position.y, position.z, 1));
            Shader.SetGlobalVector("unity_OrthoParams", new Vector4(camera.orthographicSize * camera.aspect, camera.orthographicSize, 0, camera.orthographic ? 1 : 0));
            Shader.SetGlobalMatrix("_RimUnity_View", camera.worldToCameraMatrix);
            Shader.SetGlobalMatrix("_RimUnity_Projection", camera.projectionMatrix);
            SetDepthCamera(camera);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(camera.pixelWidth, camera.pixelHeight, 1 + 1f / camera.pixelWidth, 1 + 1f / camera.pixelHeight));
    }

    //地图和离屏预览分别使用自身投影，解析地表交点不需要额外 GPU 深度附件。
    internal static void SetDepthCamera(Camera camera)
    {
        Matrix4x4 vp = camera.projectionMatrix * camera.worldToCameraMatrix;
        Shader.SetGlobalMatrix("_RimUnity_DepthVP", vp);
        Shader.SetGlobalMatrix("_RimUnity_DepthInvVP", vp.inverse);
        float near = camera.nearClipPlane, far = camera.farClipPlane;
        Shader.SetGlobalVector("_ProjectionParams", new Vector4(1, near, far, 1 / far));
        Shader.SetGlobalVector("_ZBufferParams", new Vector4(far / near - 1, 1, 1 / near - 1 / far, 1 / far));
    }
}
