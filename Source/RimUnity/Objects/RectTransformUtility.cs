using System;
using Verse;

using RimUnity.UI;
namespace RimUnity;

//在画布中转换 Unity 屏幕坐标和 UI 局部坐标。
public static class RectTransformUtility
{
    //相机画布使用实际投影，无相机的叠加画布保持屏幕坐标。
    public static Vector2 WorldToScreenPoint(Camera camera, Vector3 position)
        => camera != null ? (Vector2)camera.WorldToScreenPoint(position) : (Vector2)position;
    //在指定根节点坐标系内合并所有活动矩形的四角。
    public static Bounds CalculateRelativeRectTransformBounds(Transform root, Transform child)
    {
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        Vector3[] corners = new Vector3[4];
        bool any = false;
        foreach (RectTransform rect in child.GetComponentsInChildren<RectTransform>())
        {
            rect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 point = root.InverseTransformPoint(corner);
                min = Vector3.Min(min, point); max = Vector3.Max(max, point); any = true;
            }
        }
        return any ? new Bounds((min + max) * 0.5f, max - min) : new Bounds(Vector3.zero, Vector3.zero);
    }

    //省略子树参数时测量根节点自身的子树。
    public static Bounds CalculateRelativeRectTransformBounds(Transform root) => CalculateRelativeRectTransformBounds(root, root);
    //Unity 指针原点在左下，与 RimWorld GUI 的左上原点不同。
    public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera camera, out Vector2 localPoint)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) { localPoint = default; return false; }
        if (canvas.EffectiveMode != RenderMode.ScreenSpaceOverlay || camera != null)
        {
            camera ??= canvas.rootCanvas.worldCamera != null ? canvas.rootCanvas.worldCamera : Camera.main;
            if (camera == null) { localPoint = default; return false; }
            Ray ray = camera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y));
            Vector3 normal = rect.rotation * Vector3.forward;
            float denominator = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denominator) < .000001f) { localPoint = default; return false; }
            float distance = Vector3.Dot(rect.position - ray.origin, normal) / denominator;
            localPoint = rect.InverseTransformPoint(ray.origin + ray.direction * distance);
            return distance >= 0;
        }
        localPoint = rect.InverseTransformPoint(screenPoint / canvas.scaleFactor);
        return true;
    }

    //矩形命中采用相同的局部坐标换算。
    public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera camera = null)
        => ScreenPointToLocalPointInRectangle(rect, screenPoint, camera, out Vector2 p) && rect.rect.Contains(p);
    //先求屏幕射线与矩形平面的交点，再转换回世界位置。
    public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera camera, out Vector3 point)
    {
        bool hit = ScreenPointToLocalPointInRectangle(rect, screenPoint, camera, out var local);
        point = hit ? rect.TransformPoint(local) : Vector3.zero; return hit;
    }

    //把已经计算好的世界 UI 坐标转为当前 GUI 坐标。
    internal static Vector2 ToGui(Canvas canvas, Vector3 point)
    {
        if (canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay) return new Vector2(point.x * canvas.scaleFactor / Prefs.UIScale, (Screen.height - point.y * canvas.scaleFactor) / Prefs.UIScale);
        Camera camera = canvas.rootCanvas.worldCamera != null ? canvas.rootCanvas.worldCamera : Camera.main;
        Vector3 screen = camera.WorldToScreenPoint(point);
        return new Vector2(screen.x, Screen.height - screen.y) / Prefs.UIScale;
    }
}
