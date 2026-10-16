using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//在地图材质采样 RenderTexture 之前完成离屏绘制。
internal static class CameraRenderer
{
    private static readonly List<MeshDrawCommand> Commands = new();
    private static bool rendering;
    internal static void Render(Camera camera, bool uiOnly = false)
    {
        RenderTexture target = camera.targetTexture;
        if (!target)
        {
            if (camera == Camera.main) throw new InvalidOperationException("地图主相机由宿主渲染。");
            if (camera.ScreenTarget && (camera.ScreenTarget.width != Screen.width || camera.ScreenTarget.height != Screen.height))
            { RimUnity.Object.Destroy(camera.ScreenTarget); camera.ScreenTarget = null; }
            camera.ScreenTarget ??= new RenderTexture(Screen.width, Screen.height, 24, (int)UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
            target = camera.ScreenTarget;
        }
        if (rendering) throw new InvalidOperationException("不能递归渲染离屏相机。");
        var renderer = UnityStubs.Rendering.CameraTextureRenderer.Current
            ?? throw new InvalidOperationException("宿主尚未提供离屏相机渲染服务。");
        rendering = true;
        try
        {
            Commands.Clear();
            if (!uiOnly) DirectionalShadow.Prepare(camera);
            ShaderGlobals.SetCamera(camera);
            Graphics.Capture(Commands, () =>
            {
                if (!uiOnly) ParticleFrame.DrawScene(camera);
                UI.Runtime.UguiRuntime.DrawWorld(camera);
            });
            bool composite = uiOnly || camera.targetTexture == null;
            if (composite) ScreenCameraMaterials.Prepare(Commands);
            var view = Matrix4x4.ToNumerics(camera.worldToCameraMatrix);
            var projection = Matrix4x4.ToNumerics(camera.projectionMatrix);
            var viewport = camera.rect;
            var color = camera.backgroundColor;
            if (composite) { color.r *= color.a; color.g *= color.a; color.b *= color.a; }
            renderer.Render(target.CaptureTarget, Commands, new CameraView(view, projection, projection * view, camera.cullingMask),
                new UnityEngine.Rect(viewport.x, viewport.y, viewport.width, viewport.height),
                camera.clearFlags == CameraClearFlags.SolidColor ? new UnityEngine.Color(color.r, color.g, color.b, color.a) : null);
            target.Resolve();
        }
        finally
        {
            Commands.Clear(); rendering = false;
            ShaderGlobals.UpdateMapLighting();
            if (Camera.main != null) ShaderGlobals.SetCamera(Camera.main);
        }
    }
}
