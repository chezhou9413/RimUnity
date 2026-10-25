using System;

namespace RimUnity;

//面片边界与实际绘制共用朝向、旋转、拉伸及屏幕尺寸限制。
public sealed partial class ParticleSystemRenderer
{
    //只检查有活跃粒子的实际面片，不把发射器原点当作粒子边界。
    internal bool AnimationVisible(ParticleSystem system, Camera camera, Matrix4x4 viewProjection)
    {
        if (system.trails.enabled || renderMode == ParticleSystemRenderMode.Mesh)
            throw new NotSupportedException("Legacy Animation 的粒子可见性裁剪尚未接通网格和拖尾。");
        if (sharedMaterial == null || renderMode == ParticleSystemRenderMode.None || maxParticleSize <= 0) return false;
        var cameraState = new ParticleCameraState(camera, null, allowRoll);
        for (int i = 0; i < system.count; i++)
        {
            ParticleSystem.Particle particle = system.particles[i];
            Vector3 center = system.WorldPosition(particle), size = Vector3.Scale(system.SizeAt(particle), system.ParticleScale(particle));
            BillboardAxes(system, particle, in cameraState, center, size, out Vector3 x, out Vector3 y);
            if (Rendering.AnimationVisibility.Intersects(viewProjection, BillboardBounds(center, x, y, pivot))) return true;
        }
        return false;
    }

    //轴向绝对值给出四个面片顶点的精确AABB，枢轴偏移与绘制保持一致。
    internal static Bounds BillboardBounds(Vector3 center, Vector3 x, Vector3 y, Vector3 pivot)
        => new(center + x * (pivot.x * 2) + y * (pivot.y * 2),
            new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y), Mathf.Abs(x.z) + Mathf.Abs(y.z)) * 2);

    //绘制和裁剪共用此步骤，避免拉伸粒子或最小屏幕尺寸被低估。
    private void BillboardAxes(ParticleSystem s, ParticleSystem.Particle p, in ParticleCameraState cameraState, Vector3 center, Vector3 size,
        out Vector3 x, out Vector3 y)
        => BillboardAxesPrepared(s, p, in cameraState, center, size,
            alignment == ParticleSystemRenderSpace.Velocity || renderMode == ParticleSystemRenderMode.Stretch ? s.WorldVelocity(p) : default,
            out x, out y);

    // Rendering already evaluated this velocity for vertex streams. Visibility
    // computes it once only when alignment/stretch actually needs it.
    private void BillboardAxesPrepared(ParticleSystem s, in ParticleSystem.Particle p, in ParticleCameraState cameraState, Vector3 center, Vector3 size,
        Vector3 worldVelocity, out Vector3 x, out Vector3 y)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Axes);
#endif

        // Stretch changes size even when emission/3D rotation overrides its axes.
        if (renderMode == ParticleSystemRenderMode.Stretch)
            size.y = Mathf.Abs(size.y) * lengthScale + worldVelocity.magnitude * velocityScale;
        if (p.alignedToDirection)
        {
            Quaternion rotation = s.ParticleRotation(p) * Quaternion.Euler(p.rotation3D);
            x = rotation * Vector3.right; y = rotation * Vector3.up;
        }
        else if (s.main.startRotation3D && renderMode == ParticleSystemRenderMode.Billboard)
        {
            Quaternion view = BillboardAlignment(s, in cameraState, center, worldVelocity);
            Quaternion rotation = view * Quaternion.Euler(p.rotation3D.x, p.rotation3D.y, -p.rotation3D.z);
            x = rotation * Vector3.right; y = rotation * Vector3.up;
        }
        else
        {
            Vector3 right, up;
            if (renderMode == ParticleSystemRenderMode.HorizontalBillboard)
            { right = Vector3.right; up = Vector3.forward; }
            else if (renderMode == ParticleSystemRenderMode.VerticalBillboard)
            { right = cameraState.VerticalRight; up = Vector3.up; }
            else if (renderMode == ParticleSystemRenderMode.Stretch)
            {
                up = worldVelocity.sqrMagnitude > .000001f ? worldVelocity.normalized
                    : BillboardAlignment(s, in cameraState, center, worldVelocity) * Vector3.up;
                right = Vector3.Cross(up, cameraState.Forward).normalized;
            }
            else if (alignment == ParticleSystemRenderSpace.View || alignment == ParticleSystemRenderSpace.Facing && !cameraState.HasCamera)
            { right = cameraState.ViewRight; up = cameraState.ViewUp; }
            else
            {
                Quaternion view = BillboardAlignment(s, in cameraState, center, worldVelocity);
                right = view * Vector3.right; up = view * Vector3.up;
            }
            float angle = p.rotation * Mathf.Deg2Rad, co = Mathf.Cos(angle), si = Mathf.Sin(angle);
            x = right * co - up * si; y = up * co + right * si;
        }
        x *= size.x * .5f; y *= size.y * .5f;
        ClampBillboardSize(center, in cameraState, ref x, ref y);
    }
}
