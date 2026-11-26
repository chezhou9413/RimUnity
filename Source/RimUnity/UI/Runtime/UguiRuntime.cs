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
        internal bool Visible = true;
        //相机画布作为一个 UI 渲染层提交，子图形可以保留制作时的对象层。
        internal int RenderLayer => Canvas.EffectiveMode == RenderMode.ScreenSpaceCamera
            ? Canvas.rootCanvas.gameObject.layer : (Particles != null ? Particles.gameObject.layer : Rect.gameObject.layer);
        internal Mask[] Masks = System.Array.Empty<Mask>();
        internal RectMask2D[] RectMasks = System.Array.Empty<RectMask2D>();
    }
    private static readonly List<Canvas> Canvases = new();
    internal static readonly List<Entry> Entries = new();
    internal static int PreparationVersion { get; private set; }
    private static readonly HashSet<Graphic> WarnedGraphics = new();
    private static readonly List<Graphic> CollectedGraphics = new();

    //登记场景实例；资源模板不会进入绘制列表。
    internal static void Register(Canvas canvas)
    {
        if (canvas.gameObject.IsAssetToken) return;
        if (!Canvases.Contains(canvas)) Canvases.Add(canvas);
    }

    //每次 UI 事件前刷新布局，活动状态、文本和填充量即时生效。
    internal static void Prepare()
    {
        PreparationVersion++;
        Canvases.RemoveAll(c => c == null);
        WarnedGraphics.RemoveWhere(g => g == null);
        Canvases.Sort((a, b) => a.sortingOrder != b.sortingOrder ? a.sortingOrder.CompareTo(b.sortingOrder) : a.GetInstanceID().CompareTo(b.GetInstanceID()));
        Entries.Clear();
        //窗口没有可绘制面积时清空提交，保留画布上一次有效布局，恢复尺寸后重新准备。
        if (Screen.width <= 0 || Screen.height <= 0) return;
        Rect screenClip = new(0, 0, Screen.width / Prefs.UIScale, Screen.height / Prefs.UIScale);
        foreach (Canvas canvas in Canvases)
        {
            if (!canvas.isActiveAndEnabled || !canvas.isRootCanvas) continue;
            canvas.Prepare();
            UguiDirty.EnsureLayout((RectTransform)canvas.transform);
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            Collect(canvas, (RectTransform)canvas.transform, raycaster != null && raycaster.enabled,
                screenClip, screenClip, new UguiProjection(canvas),
                CollectContext.FromAncestors(canvas.transform.parent));
        }
        Entries.Sort((a, b) => a.Canvas.EffectiveOrder != b.Canvas.EffectiveOrder ? a.Canvas.EffectiveOrder.CompareTo(b.Canvas.EffectiveOrder) : a.Order.CompareTo(b.Order));
    }

    //以层级顺序收集图形，让后绘制元素先接收命中。
    private static void Collect(Canvas canvas, RectTransform rect, bool raycast, Rect clip, Rect screenClip, UguiProjection projection, CollectContext context)
    {
        if (!rect.gameObject.activeInHierarchy) return;
        Canvas nested = rect.GetComponent<Canvas>();
        if (nested != null && nested != canvas && nested.isActiveAndEnabled)
        {
            nested.Prepare(); canvas = nested; projection = new UguiProjection(canvas);
            GraphicRaycaster nestedRaycaster = nested.GetComponent<GraphicRaycaster>();
            if (nestedRaycaster != null) raycast = nestedRaycaster.enabled;
        }
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        context = context.WithGroup(group);
        Mask stencil = rect.GetComponent<Mask>();
        bool maskHidden = stencil != null && stencil.MaskEnabled() && !stencil.showMaskGraphic;
        //先处理当前节点，再递归子节点；showMaskGraphic = false 仅隐藏绘制，仍保留射线命中（如 ScrollView Viewport）。
        rect.GetComponents(CollectedGraphics);
        foreach (Graphic graphic in CollectedGraphics)
        {
            if (!graphic.enabled) continue;
            bool isMaskable = graphic is not MaskableGraphic maskableGraphic || maskableGraphic.maskable;
            Rect effectiveClip = isMaskable ? clip : screenClip;
            if (!IntersectsClip(projection, rect, graphic, effectiveClip)) continue;
            bool acceptsRaycast = raycast && graphic.raycastTarget && context.GroupsAllowRaycast;
            if (maskHidden && !acceptsRaycast) continue;
            Entries.Add(CreateEntry(canvas, rect, graphic, context.Alpha, acceptsRaycast, effectiveClip, context, isMaskable, !maskHidden));
        }
        CollectedGraphics.Clear();
        var particles = rect.GetComponent<ParticleSystem>();
        if (particles != null && particles.enabled)
        {
            Entry entry = CreateEntry(canvas, rect, null, context.Alpha, false, clip, context);
            entry.Particles = particles; Entries.Add(entry);
        }
        //Mask 的包围矩形只用于粗裁剪，圆角和透明像素仍由原来的遮罩处理。
        if (stencil != null && stencil.MaskEnabled())
            clip = IntersectClip(clip, projection.Bounds(rect, rect.rect));
        RectMask2D mask = rect.GetComponent<RectMask2D>();
        if (mask != null && mask.enabled)
        {
            Rect local = rect.rect;
            local.xMin += mask.padding.x; local.yMin += mask.padding.y; local.xMax -= mask.padding.z; local.yMax -= mask.padding.w;
            if (local.width <= 0f || local.height <= 0f)
                clip = default;
            else
            {
                Rect r = projection.Bounds(rect, local);
                clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, r.xMin), Mathf.Max(clip.yMin, r.yMin), Mathf.Min(clip.xMax, r.xMax), Mathf.Min(clip.yMax, r.yMax));
            }
        }
        context = context.WithMasks(stencil, mask);
        for (int i = 0; i < rect.childCount; i++)
            if (rect.GetChild(i) is RectTransform child) Collect(canvas, child, raycast, clip, screenClip, projection, context);
            else CollectTransform(canvas, rect.GetChild(i), rect, raycast, clip, screenClip, projection, context);
    }

    //普通 Transform 的粒子节点也保留层级顺序，不要求作者替换成 RectTransform。
    private static void CollectTransform(Canvas canvas, Transform node, RectTransform parentRect, bool raycast, Rect clip, Rect screenClip, UguiProjection projection, CollectContext context)
    {
        if (!node.gameObject.activeInHierarchy) return;
        CanvasGroup group = node.GetComponent<CanvasGroup>();
        context = context.WithGroup(group);
        var particles = node.GetComponent<ParticleSystem>();
        if (particles != null && particles.enabled)
        {
            Entry entry = CreateEntry(canvas, parentRect, null, context.Alpha, false, clip, context);
            entry.Particles = particles; Entries.Add(entry);
        }
        context = context.WithMasks(node.GetComponent<Mask>(), node.GetComponent<RectMask2D>());
        for (int i = 0; i < node.childCount; i++)
            if (node.GetChild(i) is RectTransform rect) Collect(canvas, rect, raycast, clip, screenClip, projection, context);
            else CollectTransform(canvas, node.GetChild(i), parentRect, raycast, clip, screenClip, projection, context);
    }

    //绘制只发生在 Repaint，所有全局 GUI 状态由 finally 恢复。
    internal static void Draw()
    {
        if (Event.current.type != EventType.Repaint) return;
        TMP_InputField.ActiveFocused?.SyncComposition();
        Rendering.ShaderGlobals.Update();
        ScreenCameraUi.Draw();
        //整帧字形先入图集，避免逐个 Text 绘制时反复扩容、复制和上传同一张图集。
        foreach (Entry entry in Entries)
            if (entry.Visible && entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay && entry.Graphic is Text text
                && text.isActiveAndEnabled && entry.Clip.width > 0 && entry.Clip.height > 0)
                UguiTextRenderer.Warm(entry, text);
        try
        {
            foreach (Entry entry in Entries)
            {
                if (!entry.Visible || entry.Canvas.EffectiveMode != RenderMode.ScreenSpaceOverlay) continue;
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
                    Color multiplier = new(1f, 1f, 1f, entry.Alpha);
                    var toggle = graphic.GetComponentInParent<Toggle>();
                    if (toggle != null && toggle.graphic == graphic) multiplier.a *= toggle.GraphicAlpha;
                    Selectable button = graphic.GetComponentInParent<Selectable>();
                    if (button != null && button.targetGraphic == graphic) multiplier *= button.Tint;
                    Color tint = graphic.color * multiplier;
                    if (graphic.material != null && (graphic.material.shader == null || !graphic.material.shader.isSupported))
                    {
                        if (WarnedGraphics.Add(graphic)) Log.Error("[RimUnity] 自定义 UI 材质没有可用的 Waylith Shader：" + graphic.gameObject.name);
                        continue;
                    }
                    if (graphic is RimUnity.UI.Image image) UguiImageRenderer.Draw(entry, image, tint);
                    else if (graphic is RawImage raw) UguiImageRenderer.DrawRaw(entry, raw, tint);
                    else if (graphic is TextMeshProUGUI tmp) TmpRenderer.Draw(entry, tmp, tint, multiplier);
                    else if (graphic is MeshGraphic mesh) mesh.DrawMesh(entry, tint);
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
                        if (previousMaterial == null && (entry.Masks.Length > 0 || UiRectMaskData.NeedsShader(entry))) text.material = UiMaskData.DefaultMaterial();
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
        => new UguiProjection(entry.Canvas).Bounds(entry.Rect, rect);

    //只在当前鼠标位于有效 Graphic 的矩形内时命中，包括透明拖动层。
    internal static bool Contains(Entry entry, Vector2 point)
    {
        if (!entry.Clip.Contains(point)) return false;
        Vector2 screen = new(point.x * Prefs.UIScale, Screen.height - point.y * Prefs.UIScale);
        Camera eventCamera = entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay
            ? null
            : (entry.Canvas.rootCanvas.worldCamera != null ? entry.Canvas.rootCanvas.worldCamera : Camera.main);
        foreach (var mask in entry.Masks)
            if (!RectTransformUtility.RectangleContainsScreenPoint(mask.transform, screen, eventCamera)) return false;
        foreach (var mask in entry.RectMasks)
        {
            var rect = (RectTransform)mask.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, eventCamera, out var local)) return false;
            Rect r = rect.rect; r.xMin += mask.padding.x; r.yMin += mask.padding.y; r.xMax -= mask.padding.z; r.yMax -= mask.padding.w;
            if (!r.Contains(local)) return false;
        }
        return RectTransformUtility.RectangleContainsScreenPoint(entry.Rect, screen, eventCamera);
    }

    //在宿主把鼠标/滚轮事件派发给 IMGUI 窗口之前处理顶层 Overlay，之后对原版窗口上的捕获拖拽/释放做收尾。
    [HarmonyPatch(typeof(UnityEngine.GUI), nameof(UnityEngine.GUI.DispatchInputToWindows))]
    private static class GuiWindowInputPatch
    {
        private static void Prefix() => UguiPointer.ProcessPreWindow();
        private static void Postfix() => UguiPointer.ProcessPostWindow();
    }

    //宿主在 Repaint 结束清理未绘制 IMGUI 文本焦点后同步 RimUnity 输入框焦点，确保 Waylith TextInputBridge 持续开启字符/IME 输入。
    [HarmonyPatch(typeof(UnityEngine.GUI), nameof(UnityEngine.GUI.ProcessPendingWindows))]
    private static class GuiPendingWindowsPatch
    {
        private static void Postfix() => TMP_InputField.SyncHostTextFocus();
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
