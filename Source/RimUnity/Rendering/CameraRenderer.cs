using System;
using System.Collections.Generic;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//在地图材质采样 RenderTexture 之前完成离屏绘制。
internal static class CameraRenderer
{
    private static readonly List<MeshDrawCommand> Commands = new();
    private static bool rendering;
    internal static void Render(Camera camera)
    {
        if (camera.targetTexture == null) throw new InvalidOperationException("RimUnity 相机需要 targetTexture。");
        if (rendering) throw new InvalidOperationException("不能递归渲染离屏相机。");
        var renderer = UnityStubs.Rendering.CameraTextureRenderer.Current
            ?? throw new InvalidOperationException("宿主尚未提供离屏相机渲染服务。");
        rendering = true;
        try
        {
            Commands.Clear();
            DirectionalShadow.Prepare(camera);
            ShaderGlobals.SetCamera(camera);
            Graphics.Capture(Commands, () => ParticleFrame.DrawScene(camera));
            var view = Matrix4x4.ToNumerics(camera.worldToCameraMatrix);
            var projection = Matrix4x4.ToNumerics(camera.projectionMatrix);
            var viewport = camera.rect;
            var color = camera.backgroundColor;
            renderer.Render(camera.targetTexture.CaptureTarget, Commands, new CameraView(view, projection, projection * view, camera.cullingMask),
                new UnityEngine.Rect(viewport.x, viewport.y, viewport.width, viewport.height),
                camera.clearFlags == CameraClearFlags.SolidColor ? new UnityEngine.Color(color.r, color.g, color.b, color.a) : null);
            camera.targetTexture.Resolve();
        }
        finally
        {
            Commands.Clear(); rendering = false;
            ShaderGlobals.UpdateMapLighting();
            if (Camera.main != null) ShaderGlobals.SetCamera(Camera.main);
        }
    }
}
