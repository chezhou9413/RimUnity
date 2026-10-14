using System;
using System.Collections.Generic;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//宿主 Pawn 相机预览复用地图 Bloom；只有显式开始的目标会被接管。
public sealed class BloomPreview : IDisposable
{
    private static BloomPreview active;
    private UnityEngine.RenderTexture target;
    private BloomPreviewRenderer renderer;
    private int mask;
    private Exception error;

    //调用方负责在 finally 中 End，不接管普通头像或其他窗口。
    public void Begin(UnityEngine.RenderTexture texture, int layerMask)
    {
        if (active != null) throw new InvalidOperationException("不能嵌套 Bloom 预览会话。");
        target = texture ?? throw new ArgumentNullException(nameof(texture));
        mask = layerMask; error = null; active = this;
    }

    //渲染回调错误交回所属编辑器，避免被宿主相机吞掉。
    public void CheckError()
    {
        if (error != null) throw new InvalidOperationException("RimUnity Bloom 预览失败。", error);
    }

    //结束捕获但保留 GPU 缓存，下一帧可复用。
    public void End() { if (active == this) active = null; target = null; }
    public void Dispose() { End(); renderer?.Dispose(); renderer = null; }

    //CameraTextureRenderer 仅保存 Current；实际 Render 在宿主的 CameraTextureBaker 上。
    [HarmonyPatch(typeof(RimWorldWaylith.Rendering.CameraTextureBaker), nameof(RimWorldWaylith.Rendering.CameraTextureBaker.Render),
        new[] { typeof(UnityEngine.RenderTexture), typeof(IReadOnlyList<MeshDrawCommand>), typeof(CameraView),
            typeof(UnityEngine.Rect), typeof(UnityEngine.Color?) })]
    private static class RenderHook
    {
        //仅支持明确背景的完整预览，不能静默改变视口或透明画布语义。
        private static bool Prefix(UnityEngine.RenderTexture target, IReadOnlyList<MeshDrawCommand> commands,
            CameraView view, UnityEngine.Rect viewportRect, UnityEngine.Color? clearColor)
        {
            var owner = active;
            if (owner == null || owner.target != target) return true;
            try
            {
                owner.renderer ??= new BloomPreviewRenderer();
                owner.renderer.Render(target, commands, view, viewportRect, clearColor, owner.mask);
            }
            catch (Exception error) { owner.error = error; }
            return false;
        }
    }
}
