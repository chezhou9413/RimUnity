
namespace RimUnity.UI.Runtime;

//世界画布在场景快照冻结前提交，屏幕覆盖画布仍由 GUI 事件提交。
internal static partial class UguiRuntime
{
    internal static void DrawWorld(Camera targetCamera = null)
    {
        bool any = false;
        Camera drawing = targetCamera ?? Camera.main;
        //其他相机的屏幕画布不触发当前视图的整树布局与图形收集。
        foreach (var canvas in Canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay) continue;
            if (canvas.EffectiveMode == RenderMode.ScreenSpaceCamera &&
                ((canvas.rootCanvas.worldCamera ?? Camera.main) != drawing ||
                drawing != null && (drawing.cullingMask & (1 << canvas.rootCanvas.gameObject.layer)) == 0)) continue;
            any = true; break;
        }
        if (!any) return;
        Prepare(); WorldUiSubmission.BeginFrame();
        foreach (var entry in Entries)
        {
            if (entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay || entry.Clip.width <= 0 || entry.Clip.height <= 0) continue;
            Camera owner = entry.Canvas.rootCanvas.worldCamera ?? Camera.main;
            if (entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceCamera && owner != drawing) continue;
            if (drawing != null && (drawing.cullingMask & (1 << entry.RenderLayer)) == 0) continue;
            WorldUiSubmission.Current = entry;
            UiMaskData.Current = entry;
            try
            {
                if (entry.Particles != null) { entry.Particles.GetComponent<ParticleSystemRenderer>()?.DrawCameraUI(entry, drawing); continue; }
                var graphic = entry.Graphic;
                if (graphic == null || !graphic.isActiveAndEnabled) continue;
                Color tint = graphic.color; tint.a *= entry.Alpha;
                var toggle = graphic.GetComponentInParent<Toggle>();
                if (toggle != null && toggle.graphic == graphic) tint.a *= toggle.GraphicAlpha;
                var selectable = graphic.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.targetGraphic == graphic) tint *= selectable.Tint;
                if (graphic is RimUnity.UI.Image image) UguiImageRenderer.Draw(entry, image, tint);
                else if (graphic is RawImage raw) UguiImageRenderer.DrawRaw(entry, raw, tint);
                else if (graphic is TextMeshProUGUI tmp) TmpRenderer.Draw(entry, tmp, tint);
                else if (graphic is MeshGraphic mesh) mesh.DrawMesh(entry, tint);
                else if (graphic is RimUnity.UI.Text text)
                {
                    UguiTextRenderer.DrawLocal(entry, text, tint);
                }
            }
            finally { WorldUiSubmission.Current = null; UiMaskData.Current = null; }
        }
    }
}
