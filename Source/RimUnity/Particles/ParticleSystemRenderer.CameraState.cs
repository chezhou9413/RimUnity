using System.Runtime.Intrinsics;

namespace RimUnity;

//一次粒子绘制共用相机快照，不在每个面片角点重新求逆矩阵和读取父级变换。
public sealed partial class ParticleSystemRenderer
{
    private readonly struct ParticleCameraState
    {
        internal readonly bool HasCamera;
        internal readonly Vector3 Position, Forward, Up, ViewRight, ViewUp, VerticalRight;
        internal readonly Quaternion ViewAlignment;
        private readonly bool overlay;
        private readonly UI.Runtime.UguiProjection guiProjection;
        private readonly Matrix4x4 viewProjection;
        private readonly Rect viewport;
        private readonly Vector2 screenSize;

        //快照只在当前绘制或可见性检查内使用，相机变化会在下一次调用重新读取。
        internal ParticleCameraState(Camera camera, Canvas overlayCanvas, bool allowRoll)
        {
            this = default;
            HasCamera = camera != null;
            Position = HasCamera ? camera.transform.position : Vector3.zero;
            Quaternion rotation = HasCamera ? camera.transform.rotation : Quaternion.identity;
            Forward = rotation * Vector3.forward;
            Up = allowRoll ? rotation * Vector3.up : Vector3.up;
            ViewAlignment = Align(Forward);
            ViewRight = ViewAlignment * Vector3.right;
            ViewUp = ViewAlignment * Vector3.up;
            VerticalRight = Vector3.Cross(Vector3.up, Forward).normalized;
            overlay = overlayCanvas != null;
            guiProjection = overlay ? new UI.Runtime.UguiProjection(overlayCanvas) : default;
            viewProjection = HasCamera ? camera.projectionMatrix * camera.worldToCameraMatrix : Matrix4x4.identity;
            viewport = HasCamera ? camera.pixelRect : default;
            screenSize = overlay ? new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight)
                : new Vector2((int)viewport.width, (int)viewport.height);
        }

        //Facing 和 Velocity 仍按粒子方向计算；普通 View 朝向直接复用计算结果。
        internal Quaternion Align(Vector3 direction)
        {
            Vector3 forward = direction.sqrMagnitude > .000001f ? direction.normalized : Forward;
            Vector3 up = Up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > .999f) up = Mathf.Abs(forward.x) < .9f ? Vector3.right : Vector3.forward;
            return Quaternion.LookRotation(forward, up);
        }

        //保留相机视口、透视除法和近零 w 的原有结果；只省去重复的矩阵构造。
        internal Vector2 ViewportPoint(Vector3 point)
        {
            Vector2 screen;
            if (overlay) screen = guiProjection.ToGui(point);
            else
            {
                // Only x/y/w are consumed. Keep the original dot-product and
                // viewport arithmetic order, without copying a matrix or computing z.
                ref readonly Matrix4x4 m = ref viewProjection;
                float w = m.m30 * point.x + m.m31 * point.y + m.m32 * point.z + m.m33 * 1;
                if (Mathf.Abs(w) < 1e-7f) return Vector2.zero;
                float x = m.m00 * point.x + m.m01 * point.y + m.m02 * point.z + m.m03 * 1;
                float y = m.m10 * point.x + m.m11 * point.y + m.m12 * point.z + m.m13 * 1;
                screen = new Vector2(viewport.x + (x / w + 1) * .5f * viewport.width,
                    viewport.y + (1 - y / w) * .5f * viewport.height);
            }
            return new Vector2(screen.x / screenSize.x, screen.y / screenSize.y);
        }

        // Each lane projects one endpoint with the same arithmetic and near-zero w rule.
        internal float BillboardExtent(Vector3 center, Vector3 x, Vector3 y)
        {
            Vector2 horizontal, vertical;
            if (!overlay && Vector128.IsHardwareAccelerated)
            {
                var px = Vector128.Create(center.x + x.x, center.x - x.x, center.x + y.x, center.x - y.x);
                var py = Vector128.Create(center.y + x.y, center.y - x.y, center.y + y.y, center.y - y.y);
                var pz = Vector128.Create(center.z + x.z, center.z - x.z, center.z + y.z, center.z - y.z);
                ref readonly Matrix4x4 m = ref viewProjection;
                var w = ProjectRow(px, py, pz, m.m30, m.m31, m.m32, m.m33);
                var invalid = Vector128.LessThan(Vector128.Abs(w), Vector128.Create(1e-7f));
                var one = Vector128.Create(1f);
                var half = Vector128.Create(.5f);
                var normalizedX = ProjectRow(px, py, pz, m.m00, m.m01, m.m02, m.m03) / w;
                var normalizedY = ProjectRow(px, py, pz, m.m10, m.m11, m.m12, m.m13) / w;
                var screenX = Vector128.Create(viewport.x) + (normalizedX + one) * half * Vector128.Create(viewport.width);
                var screenY = Vector128.Create(viewport.y) + (one - normalizedY) * half * Vector128.Create(viewport.height);
                screenX = Vector128.ConditionalSelect(invalid, Vector128<float>.Zero, screenX / Vector128.Create(screenSize.x));
                screenY = Vector128.ConditionalSelect(invalid, Vector128<float>.Zero, screenY / Vector128.Create(screenSize.y));
                horizontal = new(screenX.GetElement(0) - screenX.GetElement(1), screenY.GetElement(0) - screenY.GetElement(1));
                vertical = new(screenX.GetElement(2) - screenX.GetElement(3), screenY.GetElement(2) - screenY.GetElement(3));
            }
            else
            {
                horizontal = ViewportPoint(center + x) - ViewportPoint(center - x);
                vertical = ViewportPoint(center + y) - ViewportPoint(center - y);
            }
            return Mathf.Max(Mathf.Abs(horizontal.x) + Mathf.Abs(vertical.x), Mathf.Abs(horizontal.y) + Mathf.Abs(vertical.y));
        }

        private static Vector128<float> ProjectRow(Vector128<float> x, Vector128<float> y, Vector128<float> z,
            float a, float b, float c, float d)
            => Vector128.Create(a) * x + Vector128.Create(b) * y + Vector128.Create(c) * z + Vector128.Create(d * 1);
    }
}
