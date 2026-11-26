namespace RimUnity;

//按视口比例限制粒子面片尺寸，不改变模拟尺寸、网格粒子或拖尾宽度。
public sealed partial class ParticleSystemRenderer
{
    public float minParticleSize;
    public float maxParticleSize = .5f;

    //沿两个面片轴计算屏幕覆盖范围，统一缩放以保留长宽比和旋转。
    private void ClampBillboardSize(Vector3 center, in ParticleCameraState cameraState, ref Vector3 x, ref Vector3 y)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Projection);
#endif

        if (uiEntry == null && !cameraState.HasCamera) return;
        float extent = cameraState.BillboardExtent(center, x, y);
        if (extent <= 0) return;
        float maximum = Mathf.Max(0, maxParticleSize);
        float minimum = Mathf.Clamp(minParticleSize, 0, maximum);
        float scale = Mathf.Clamp(extent, minimum, maximum) / extent;
        x *= scale;
        y *= scale;
    }
}
