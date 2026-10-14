using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//每个相机独占方向光深度附件，阴影使用原材质的裁切与顶点程序。
internal sealed class DirectionalShadow : IDisposable
{
    private const int Resolution = 1024;
    private readonly Camera camera;
    private readonly RenderTexture texture;
    private readonly List<MeshDrawCommand> commands = new();
    private readonly List<MeshRenderer> casters = new();

    internal DirectionalShadow()
    {
        camera = GameObject.CreateAssetToken("RimUnity 阴影相机").AddComponent<Camera>();
        camera.orthographic = true; camera.aspect = 1;
        texture = new RenderTexture(Resolution, Resolution, 24, (int)UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        camera.targetTexture = texture;
    }

    //离屏预览只使用同一预制体的光源，地图使用当前天空的主要方向光。
    internal static Light Lighting(Camera source)
    {
        ShaderGlobals.UpdateMapLighting();
        if (source.targetTexture == null) return null;
        Light selected = null;
        foreach (var light in Light.allLights)
            if (light.isActiveAndEnabled && light.type == LightType.Directional && light.transform.root == source.transform.root)
            {
                if (selected != null) throw new NotSupportedException("同一离屏预制体暂不支持多个方向光。");
                selected = light;
            }
        if (selected != null)
        {
            Vector3 direction = -selected.transform.forward;
            Shader.SetGlobalVector("_RU_MapSunDirection", new Vector4(direction.x, direction.y, direction.z, 0));
            Shader.SetGlobalColor("_RU_MapSunColor", selected.color * selected.intensity);
            Shader.SetGlobalVector("_RU_MapMoonColor", Vector4.zero);
        }
        return selected;
    }

    //只有当前视图存在投影网格时才分配 GPU 附件。
    internal static void Prepare(Camera source)
    {
        Light light = Lighting(source);
        Shader.SetGlobalVector("_RU_ShadowParams", Vector4.zero);
        if (light != null && light.shadows == LightShadows.None) return;
        bool any = false;
        foreach (var mesh in MeshRenderer.Active)
            if (mesh.shadowCastingMode != ShadowCastingMode.Off && Visible(source, mesh)) { any = true; break; }
        if (!any) return;
        source.Shadow ??= new DirectionalShadow();
        source.Shadow.Render(source, light);
    }

    //离屏根对象隔离预览模型，地图排除其他预览相机所属的模型。
    private static bool Visible(Camera source, MeshRenderer mesh)
    {
        if (!mesh.isActiveAndEnabled || (source.cullingMask & (1 << mesh.gameObject.layer)) == 0) return false;
        if (source.targetTexture != null) return mesh.transform.root == source.transform.root;
        foreach (var other in Camera.Active)
            if (other.targetTexture != null && other.transform.root == mesh.transform.root) return false;
        return true;
    }

    //包围盒覆盖所有投影网格，反向深度的清空值为零。
    private void Render(Camera source, Light light)
    {
        casters.Clear();
        Bounds bounds = default;
        foreach (var mesh in MeshRenderer.Active)
        {
            if (mesh.shadowCastingMode == ShadowCastingMode.Off || !Visible(source, mesh)
                || light != null && (light.cullingMask & (1 << mesh.gameObject.layer)) == 0) continue;
            if (casters.Count == 0) bounds = mesh.bounds; else bounds.Encapsulate(mesh.bounds);
            casters.Add(mesh);
        }
        if (casters.Count == 0) return;
        var sun = Shader.GetGlobalVector("_RU_MapSunDirection");
        var direction = new Vector3(sun.x, sun.y, sun.z).normalized;
        float radius = Math.Max(.1f, bounds.extents.magnitude) + .1f;
        camera.transform.position = bounds.center + direction * (radius * 2);
        camera.transform.rotation = Quaternion.LookRotation(-direction);
        camera.orthographicSize = radius;
        camera.nearClipPlane = .01f; camera.farClipPlane = radius * 4;
        commands.Clear();
        Graphics.Capture(commands, () => { foreach (var mesh in casters) mesh.Draw(camera, true); });
        Matrix4x4 vp = camera.projectionMatrix * camera.worldToCameraMatrix;
        var renderer = UnityStubs.Rendering.CameraTextureRenderer.Current
            ?? throw new InvalidOperationException("宿主未提供阴影附件渲染服务。");
        renderer.Render(texture.Target, commands, new CameraView(Matrix4x4.ToNumerics(camera.worldToCameraMatrix),
            Matrix4x4.ToNumerics(camera.projectionMatrix), Matrix4x4.ToNumerics(vp), source.cullingMask),
            new UnityEngine.Rect(0, 0, 1, 1), UnityEngine.Color.clear);
        commands.Clear();
        Shader.SetGlobalTexture("_RU_ShadowMap", texture);
        Shader.SetGlobalMatrix("_RU_ShadowVP", vp);
        float texel = radius * 2 / Resolution;
        Shader.SetGlobalVector("_RU_ShadowParams", new Vector4(1, light?.shadowStrength ?? 1,
            light?.shadows == LightShadows.Hard ? 0 : 1, (light?.shadowBias ?? .05f) * texel / camera.farClipPlane));
        Shader.SetGlobalVector("_RU_ShadowTexel", new Vector4(1f / Resolution, 1f / Resolution, (light?.shadowNormalBias ?? .4f) * texel, 0));
    }

    //附件与辅助相机由所属相机销毁时统一释放。
    public void Dispose() { Object.Destroy(texture); Object.Destroy(camera.gameObject); }
}
