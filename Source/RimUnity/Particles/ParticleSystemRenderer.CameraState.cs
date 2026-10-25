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
    }
}
