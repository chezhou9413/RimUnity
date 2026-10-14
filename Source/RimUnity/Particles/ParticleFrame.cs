
namespace RimUnity;

//原生 LateUpdate 完成后提交，所有相机冻结绘制队列和上传网格之前完成。
internal static class ParticleFrame
{
    private static int frame = -1;
    internal static void Draw()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        Rendering.ShaderGlobals.Update();
        Camera camera = Camera.main;
        if (camera == null) return;
        foreach (var offscreen in System.Linq.Enumerable.OrderBy(Camera.Active, c => c.depth))
            if (offscreen.isActiveAndEnabled && offscreen.targetTexture != null) offscreen.Render();
        Rendering.DirectionalShadow.Prepare(camera);
        DrawScene(camera);
        UI.Runtime.UguiRuntime.DrawWorld();
    }
    //各相机独立筛选图层，离屏与地图共享模型、粒子和拖尾绘制逻辑。
    internal static void DrawScene(Camera camera)
    {
        bool Visible(Component component) => (camera.cullingMask & (1 << component.gameObject.layer)) != 0;
        foreach (var renderer in MeshRenderer.Active)
            if (Visible(renderer)) renderer.Draw(camera);
        foreach (var system in ParticleSystem.Active)
            if (Visible(system) && system.GetComponentInParent<Canvas>() == null) system.GetComponent<ParticleSystemRenderer>()?.Draw(system, camera);
        foreach (var renderer in SkinnedMeshRenderer.Active)
            if (Visible(renderer)) renderer.Draw(camera);
        foreach (var renderer in TrailRenderer.Active)
            if (Visible(renderer)) renderer.Draw(camera);
        foreach (var renderer in LineRenderer.Active)
            if (Visible(renderer)) renderer.Draw(camera);
    }
}
