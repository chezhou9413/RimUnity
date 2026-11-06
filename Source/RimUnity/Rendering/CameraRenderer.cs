using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//在地图材质采样 RenderTexture 之前完成离屏绘制。
internal static class CameraRenderer
{
    private static readonly List<MeshDrawCommand> Commands = new();
    private static readonly List<(MeshDrawCommand command, int queue, int order)> OrderedCommands = new();
    private static bool rendering;
    internal static void Render(Camera camera, bool uiOnly = false)
    {
        RenderTexture target = camera.targetTexture;
        if (!target)
        {
            if (camera == Camera.main) throw new InvalidOperationException("地图主相机由宿主渲染。");
            //先在浮点附件中叠加 HDR 粒子，再由屏幕合成限制最终显示范围。
            GraphicsFormat format = camera.allowHDR ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
            if (camera.ScreenTarget && (camera.ScreenTarget.width != Screen.width || camera.ScreenTarget.height != Screen.height ||
                camera.ScreenTarget.Target.graphicsFormat != format))
            { RimUnity.Object.Destroy(camera.ScreenTarget); camera.ScreenTarget = null; }
            camera.ScreenTarget ??= new RenderTexture(Screen.width, Screen.height, 24, (int)format);
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
            if (!uiOnly) SortSceneCommands();
            var view = Matrix4x4.ToNumerics(camera.worldToCameraMatrix);
            var projection = Matrix4x4.ToNumerics(camera.projectionMatrix);
            var viewport = camera.rect;
            var color = camera.backgroundColor;
            if (composite) { color.r *= color.a; color.g *= color.a; color.b *= color.a; }
            target.PrepareCapture();
            renderer.Render(target.CaptureTarget, Commands, new CameraView(view, projection, projection * view, camera.cullingMask),
                new UnityEngine.Rect(viewport.x, viewport.y, viewport.width, viewport.height),
                camera.clearFlags == CameraClearFlags.SolidColor ? new UnityEngine.Color(color.r, color.g, color.b, color.a) : null);
            target.Resolve();
            if (!uiOnly) AnimationVisibility.RecordRender(camera);
        }
        finally
        {
            Commands.Clear(); OrderedCommands.Clear(); rendering = false;
            ShaderGlobals.UpdateMapLighting();
            if (Camera.main != null) ShaderGlobals.SetCamera(Camera.main);
        }
    }

    //宿主即时捕获不排序；场景先画低队列，同队列保留原来的对象和多 Pass 顺序。
    private static void SortSceneCommands()
    {
        OrderedCommands.Clear();
        for (int i = 0; i < Commands.Count; i++)
        {
            MeshDrawCommand command = Commands[i];
            int queue = command.RenderQueueOverride > 0 ? command.RenderQueueOverride : command.Material.RenderQueue;
            var grab = command.Material.Shader.SceneGrab;
            if (grab != null && queue < grab.Queue) queue = grab.Queue;
            OrderedCommands.Add((command, queue, i));
        }
        OrderedCommands.Sort(static (a, b) =>
        {
            int comparison = a.queue.CompareTo(b.queue);
            return comparison != 0 ? comparison : a.order.CompareTo(b.order);
        });
        for (int i = 0; i < Commands.Count; i++) Commands[i] = OrderedCommands[i].command;
        OrderedCommands.Clear();
    }
}
