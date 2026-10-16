using HarmonyLib;
using Verse;

namespace RimUnity;

//初始化明文资源运行时与地图渲染接线，资源按需加载。
public sealed class RimUnityMod : Mod
{
    public static ModContentPack OwnContent { get; private set; }
    //早期加载界面可据此等待主线程运行时完成安装。
    public static bool IsRuntimeReady => HostFrame.IsInstalled;
    public static Rendering.BloomSettings Bloom { get; private set; }
    internal static string ShaderDirectory { get; private set; }

    //安装帧调度、材质默认值和渲染回调。
    public RimUnityMod(ModContentPack content) : base(content)
    {
        OwnContent = content;
        if (RuntimeInfo.ResourceRoot == null)
            RuntimeInfo.Configure(System.IO.Path.Combine(content.RootDir, "1.7"), true, content.Name, 0, "");
        ShaderDirectory = System.IO.Path.Combine(RuntimeInfo.ResourceRoot, "Shaders", "RimUnity");
        Bloom = GetSettings<Rendering.BloomSettings>();
        Rendering.MaterialColorSpace.Install();
        Rendering.ShaderMaterialDefaults.Install();
        new Harmony("Chezhou.RimUnity").PatchAll(typeof(RimUnityMod).Assembly);
        LongEventHandler.ExecuteWhenFinished(HostFrame.Install);
        LongEventHandler.ExecuteWhenFinished(Rendering.LoadingWarmup.CompileBeforeMenu);
        Log.Message("[RimUnity] 运行时已加载；使用文档见 Docs/zh-CN 和 Docs/en。程序集：" + typeof(RimUnityMod).Assembly.Location
            + "；Core：" + typeof(Texture2D).Assembly.Location);
    }

    //渲染设置属于桥接框架，内容模组无需各自提供后处理窗口。
    public override string SettingsCategory() => "RimUnity";
    public override void DoSettingsWindowContents(UnityEngine.Rect rect) => Settings.SettingsWindow.Draw(rect, Bloom);
}
