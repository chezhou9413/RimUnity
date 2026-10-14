using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using RimUnity.UI;
using Verse;
using Graphic = RimUnity.UI.Graphic;

namespace RimUnity.UI.Runtime;

//统一安排桥接画布的布局、输入命中与网格提交。
internal static partial class UguiRuntime
{
    internal sealed record Entry(Canvas Canvas, RectTransform Rect, Graphic Graphic, float Alpha, bool Raycast, Rect Clip)
    {
        internal ParticleSystem Particles;
        internal int Order;
        internal Mask[] Masks = System.Array.Empty<Mask>();
        internal RectMask2D[] RectMasks = System.Array.Empty<RectMask2D>();
    }
    private static readonly List<Canvas> Canvases = new();
    internal static readonly List<Entry> Entries = new();
    private static readonly HashSet<Graphic> WarnedGraphics = new();

    //登记场景实例；资源模板不会进入绘制列表。
    internal static void Register(Canvas canvas)
    {
        if (canvas.gameObject.IsAssetToken) return;
        if (!Canvases.Contains(canvas)) Canvases.Add(canvas);
    }

    //每次 UI 事件前刷新布局，活动状态、文本和填充量即时生效。
    internal static void Prepare()
    {
        Canvases.RemoveAll(c => c == null);
        WarnedGraphics.RemoveWhere(g => g == null);
        Canvases.Sort((a, b) => a.sortingOrder != b.sortingOrder ? a.sortingOrder.CompareTo(b.sortingOrder) : a.GetInstanceID().CompareTo(b.GetInstanceID()));
        Entries.Clear();
        foreach (Canvas canvas in Canvases)
        {
            if (!canvas.isActiveAndEnabled || !canvas.isRootCanvas) continue;
            canvas.Prepare();
            UguiDirty.EnsureLayout((RectTransform)canvas.transform);
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            Collect(canvas, (RectTransform)canvas.transform, 1f, raycaster != null && raycaster.enabled, new Rect(0, 0, Screen.width / Prefs.UIScale, Screen.height / Prefs.UIScale));
        }
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i]; entry.Order = i;
            var masks = new List<Mask>();
            var rectMasks = new List<RectMask2D>();
            for (Transform t = entry.Particles != null ? entry.Particles.transform.parent : entry.Rect.parent; t != null; t = t.parent)
            {
                var mask = t.GetComponent<Mask>(); if (mask != null && mask.MaskEnabled()) masks.Add(mask);
                var rectangle = t.GetComponent<RectMask2D>(); if (rectangle != null && rectangle.isActiveAndEnabled) rectMasks.Add(rectangle);
            }
            entry.Masks = masks.ToArray();
            entry.RectMasks = rectMasks.ToArray();
        }
        Entries.Sort((a, b) => a.Canvas.EffectiveOrder != b.Canvas.EffectiveOrder ? a.Canvas.EffectiveOrder.CompareTo(b.Canvas.EffectiveOrder) : a.Order.CompareTo(b.Order));
    }

    //以层级顺序收集图形，让后绘制元素先接收命中。
    private static void Collect(Canvas canvas, RectTransform rect, float alpha, bool raycast, Rect clip)
    {
        if (!rect.gameObject.activeInHierarchy) return;
        Canvas nested = rect.GetComponent<Canvas>();
        if (nested != null && nested != canvas && nested.isActiveAndEnabled) { nested.Prepare(); canvas = nested; }
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group != null && group.enabled)
        {
            alpha *= group.alpha;
        }
        Mask stencil = rect.GetComponent<Mask>();
        foreach (Graphic graphic in rect.GetComponents<Graphic>())
            if (graphic.enabled && (stencil == null || !stencil.MaskEnabled() || stencil.showMaskGraphic)) Entries.Add(new Entry(canvas, rect, graphic, alpha, raycast && graphic.raycastTarget && GroupsAllowRaycast(rect), clip));
        var particles = rect.GetComponent<ParticleSystem>();
        if (particles != null && particles.enabled) Entries.Add(new Entry(canvas, rect, null, alpha, false, clip) { Particles = particles });
        RectMask2D mask = rect.GetComponent<RectMask2D>();
        if (mask != null && mask.enabled)
        {
            Rect local = rect.rect;
            local.xMin += mask.padding.x; local.yMin += mask.padding.y; local.xMax -= mask.padding.z; local.yMax -= mask.padding.w;
            Rect r = Bounds(new Entry(canvas, rect, null, 1, false, clip), local);
            clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, r.xMin), Mathf.Max(clip.yMin, r.yMin), Mathf.Min(clip.xMax, r.xMax), Mathf.Min(clip.yMax, r.yMax));
        }
        for (int i = 0; i < rect.childCount; i++)
            if (rect.GetChild(i) is RectTransform child) Collect(canvas, child, alpha, raycast, clip);
            else CollectTransform(canvas, rect.GetChild(i), rect, alpha, raycast, clip);
    }

    //普通 Transform 的粒子节点也保留层级顺序，不要求作者替换成 RectTransform。
    private static void CollectTransform(Canvas canvas, Transform node, RectTransform parentRect, float alpha, bool raycast, Rect clip)
    {
        if (!node.gameObject.activeInHierarchy) return;
        CanvasGroup group = node.GetComponent<CanvasGroup>();
        if (group != null && group.enabled) alpha *= group.alpha;
        var particles = node.GetComponent<ParticleSystem>();
        if (particles != null && particles.enabled) Entries.Add(new Entry(canvas, parentRect, null, alpha, false, clip) { Particles = particles });
        for (int i = 0; i < node.childCount; i++)
            if (node.GetChild(i) is RectTransform rect) Collect(canvas, rect, alpha, raycast, clip);
            else CollectTransform(canvas, node.GetChild(i), parentRect, alpha, raycast, clip);
    }

    //射线规则从当前对象向上查找，ignoreParentGroups 只终止父组的过滤。
    private static bool GroupsAllowRaycast(Transform rect)
    {
        for (Transform t = rect; t != null; t = t.parent)
        {
            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group == null || !group.enabled) continue;
            if (!group.blocksRaycasts) return false;
            if (group.ignoreParentGroups) break;
        }
        return true;
    }

    //绘制只发生在 Repaint，所有全局 GUI 状态由 finally 恢复。
    internal static void Draw()
    {
        if (Event.current.type != EventType.Repaint) return;
        Rendering.ShaderGlobals.Update();
        //整帧字形先入图集，避免逐个 Text 绘制时反复扩容、复制和上传同一张图集。
        foreach (Entry entry in Entries)
            if (entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay && entry.Graphic is Text text
                && text.isActiveAndEnabled && entry.Clip.width > 0 && entry.Clip.height > 0)
                UguiTextRenderer.Warm(entry, text);
        try
        {
            foreach (Entry entry in Entries)
            {
                if (entry.Canvas.EffectiveMode != RenderMode.ScreenSpaceOverlay) continue;
                UiMaskData.Current = entry;
                Graphic graphic = entry.Graphic;
                if (entry.Particles != null)
                {
                    if (entry.Clip.width <= 0 || entry.Clip.height <= 0) continue;
                    NativeGuiHost.PushClip(entry.Clip);
                    try { entry.Particles.GetComponent<ParticleSystemRenderer>()?.DrawUI(entry); }
                    finally { NativeGuiHost.PopClip(); }
                    continue;
                }
                if (graphic == null || !graphic.isActiveAndEnabled) continue;
                if (entry.Clip.width <= 0 || entry.Clip.height <= 0) continue;
                NativeGuiHost.PushClip(entry.Clip);
                try
                {
                Color tint = graphic.color;
                tint.a *= entry.Alpha;
                var toggle = graphic.GetComponentInParent<Toggle>();
                if (toggle != null && toggle.graphic == graphic) tint.a *= toggle.GraphicAlpha;
                Selectable button = graphic.GetComponentInParent<Selectable>();
                if (button != null && button.targetGraphic == graphic) tint *= button.Tint;
                if (graphic.material != null && (graphic.material.shader == null || !graphic.material.shader.isSupported))
                {
                    if (WarnedGraphics.Add(graphic)) Log.Error("[RimUnity] 自定义 UI 材质没有可用的 Waylith Shader：" + graphic.gameObject.name);
                    continue;
                }
                if (graphic is RimUnity.UI.Image image) UguiImageRenderer.Draw(entry, image, tint);
                else if (graphic is RawImage raw) UguiImageRenderer.DrawRaw(entry, raw, tint);
                else if (graphic is TextMeshProUGUI tmp) TmpRenderer.Draw(entry, tmp, tint);
                else if (graphic is RimUnity.UI.Text text)
                {
                    Matrix4x4 matrix = entry.Rect.localToWorldMatrix;
                    if (Mathf.Abs(matrix.m01) > 0.0001f || Mathf.Abs(matrix.m10) > 0.0001f || matrix.m00 <= 0 || matrix.m11 <= 0)
                    {
                        UguiTextRenderer.DrawLocal(entry, text, tint);
                        continue;
                    }
                    UiTextStyle style = TextStyle(text);
                    float scale = entry.Canvas.scaleFactor / Prefs.UIScale * Mathf.Abs(entry.Rect.lossyScale.y);
                    style.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * scale));
                    Rect bounds = Bounds(entry, text is Text custom ? custom.ContentRect : entry.Rect.rect);
                    var previousMaterial = text.material;
                    if (previousMaterial == null && (entry.Masks.Length > 0 || entry.RectMasks.Length > 0)) text.material = UiMaskData.DefaultMaterial();
                    try { UguiTextRenderer.Draw(entry, text, style, bounds, tint, scale); }
                    finally { text.material = previousMaterial; }
                }
                }
                finally { NativeGuiHost.PopClip(); }
            }
        }
        finally { UiMaskData.Current = null; }
    }

    //同一文字样式用于测量和绘制，不修改 Verse.Text 的全局状态。
    internal static UiTextStyle TextStyle(RimUnity.UI.Text text)
    {
        UiTextStyle style = new()
        {
            fontSize = text.fontSize, fontStyle = text.fontStyle, wordWrap = text.wordWrap
        };
        return style;
    }

    //返回 GUI 中的包围矩形；精确图片命中使用逆矩阵完成。
    internal static Rect Bounds(Entry entry) => Bounds(entry, entry.Rect.rect);

    //文字排版矩形与 Graphic 命中矩形分别换算，边距不扩大鼠标点击区域。
    internal static Rect Bounds(Entry entry, Rect rect)
    {
        Vector2 a = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(new Vector3(rect.xMin, rect.yMin)));
        Vector2 b = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(new Vector3(rect.xMax, rect.yMax)));
        Vector2 c = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(new Vector3(rect.xMin, rect.yMax)));
        Vector2 d = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(new Vector3(rect.xMax, rect.yMin)));
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)),
            Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
    }

    //只在当前鼠标位于有效 Graphic 的矩形内时命中，包括透明拖动层。
    internal static bool Contains(Entry entry, Vector2 point)
    {
        Vector2 screen = new(point.x * Prefs.UIScale, Screen.height - point.y * Prefs.UIScale);
        foreach (var mask in entry.Masks) if (!RectTransformUtility.RectangleContainsScreenPoint(mask.transform, screen)) return false;
        foreach (var mask in entry.RectMasks)
        {
            var rect = (RectTransform)mask.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, entry.Canvas.worldCamera, out var local)) return false;
            Rect r = rect.rect; r.xMin += mask.padding.x; r.yMin += mask.padding.y; r.xMax -= mask.padding.z; r.yMax -= mask.padding.w;
            if (!r.Contains(local)) return false;
        }
        return entry.Clip.Contains(point) && RectTransformUtility.RectangleContainsScreenPoint(entry.Rect, screen);
    }

    //进入 UI 根边界前处理覆盖层输入，在游戏绘制结束后绘制覆盖层。
    [HarmonyPatch]
    private static class GuiPatch
    {
        //主菜单和游戏地图使用同一套 UI 调度。
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI));
            yield return AccessTools.Method(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI));
        }
        //先消费 UI 输入，防止按钮点击穿透到地图。
        private static void Prefix() { Prepare(); UguiPointer.Process(); }
        //后绘制以保持覆盖画布顺序。
        private static void Postfix() => Draw();
    }
}
