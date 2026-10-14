using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using Waylith.Graphics;
using Waylith.Rendering;

namespace RimUnity.UI.Runtime;

//宿主只提供 ImGui 字体表和帧队列，顶点、材质和排版全部由 RimUnity 生成。
internal static class NativeGuiHost
{
    private delegate bool ResolveTexture(GpuTextureAllocation allocation, string source, out ImTextureRef reference);
    private static object host;
    private static Func<ImDrawListPtr> drawList;
    private static Func<bool> active, drawing;
    private static Func<IList> materialDraws;
    private static Func<float, float, float2> toScreen;
    private static ResolveTexture resolveTexture;
    private static Func<string, byte[], float, int, int> registerFont;
    private static Func<int, FontStyle, ImFontPtr> styledFont;
    internal static object Host { get { Bind(); return host; } }
    internal static ImDrawListPtr DrawList { get { Bind(); return drawList(); } }
    internal static IList MaterialDraws { get { Bind(); return materialDraws(); } }
    internal static bool CanDraw { get { Bind(); return active() && drawing(); } }

    //仅绑定已核对的宿主成员，使用缓存委托避免逐顶点反射。
    private static void Bind()
    {
        if (host != null) return;
        var bootstrap = AccessTools.TypeByName("RimWorldWaylith.Bootstrap") ?? throw new TypeLoadException("Waylith 启动类型不存在。");
        object current = AccessTools.Field(bootstrap, "_imguiHost").GetValue(null)
            ?? throw new InvalidOperationException("Waylith ImGui 尚未初始化。");
        object gui = AccessTools.Field(current.GetType(), "guiBackend").GetValue(current);
        object renderer = AccessTools.Property(current.GetType(), "Renderer").GetValue(current);
        drawList = FieldGetter<ImDrawListPtr>(gui, "drawList");
        active = FieldGetter<bool>(gui, "frameActive");
        drawing = FieldGetter<bool>(gui, "drawingEnabled");
        materialDraws = FieldGetter<IList>(gui, "materialDraws");
        toScreen = AccessTools.Method(gui.GetType(), "ToScreen", new[] { typeof(float), typeof(float) }).CreateDelegate<Func<float, float, float2>>(gui);
        resolveTexture = AccessTools.Method(renderer.GetType(), "TryGetTexRef").CreateDelegate<ResolveTexture>(renderer);
        registerFont = AccessTools.Method(current.GetType(), "RegisterFont").CreateDelegate<Func<string, byte[], float, int, int>>(current);
        //将字体样式数值转换为字体表参数类型。
        MethodInfo method = AccessTools.Method(current.GetType(), "StyledFont");
        var index = Expression.Parameter(typeof(int));
        var style = Expression.Parameter(typeof(FontStyle));
        styledFont = Expression.Lambda<Func<int, FontStyle, ImFontPtr>>(
            Expression.Call(Expression.Constant(current), method, index, Expression.Convert(style, method.GetParameters()[1].ParameterType)), index, style).Compile();
        host = current;
    }
    private static Func<T> FieldGetter<T>(object instance, string name)
        => Expression.Lambda<Func<T>>(Expression.Convert(Expression.Field(Expression.Constant(instance), AccessTools.Field(instance.GetType(), name)), typeof(T))).Compile();
    internal static float2 Position(Vector2 value) { Bind(); return toScreen(value.x, value.y); }
    internal static ImFontBakedPtr Font(int index, FontStyle style, float size) { Bind(); return styledFont(index, style).GetFontBaked(size); }
    internal static int RegisterFont(string name, byte[] bytes, float size) { Bind(); return registerFont(name, bytes, size, 0); }

    //直接上传 Waylith 贴图并向宿主原生 ImGui 渲染器登记纹理引用。
    internal static ImTextureRef TextureReference(Texture texture)
    {
        Bind();
        var native = texture.NativeTexture;
        if (!native.EnsureResident() || native.GpuResource == null)
            throw new InvalidOperationException("UI 纹理无法上传：" + texture.name);
        string source = (native as Waylith.Rendering.Texture2D)?.SourcePath;
        if (!resolveTexture(native.GpuResource, source, out var result))
            throw new InvalidOperationException("ImGui 无法引用纹理：" + texture.name);
        return result;
    }

    //嵌套裁切直接入原生绘制列表，不改变宿主鼠标事件状态。
    internal static void PushClip(Rect rect)
    {
        float2 a = Position(rect.min), b = Position(rect.max);
        float2 min = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), max = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        DrawList.PushClipRect(in min, in max, true);
    }
    internal static void PopClip() => DrawList.PopClipRect();
}
