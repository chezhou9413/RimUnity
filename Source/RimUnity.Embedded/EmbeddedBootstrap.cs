using System;
using HarmonyLib;
using Verse;

namespace RimUnity.Embedded;

//游戏只提前扫描这个入口；实际运行时与脚本在首次 Mod 实例创建完成后加载。
public sealed class EmbeddedBootstrap : Mod
{
    private const string Installed = "RimUnity.Embedded.Installed";
    //进程级入口注册状态；与旧包分开，允许在 XML 前接管旧补丁。
    private const string DeferredInstalled = "RimUnity.Embedded.DeferredInstalled";
    private const string CoordinatorInstalled = "RimUnity.Embedded.CoordinatorInstalled";
    private const string ScopedCoordinatorInstalled = "RimUnity.Embedded.ScopedCoordinatorInstalled";
    private const string Finished = "RimUnity.Embedded.Finished";

    //首次构造时原 CreateModClasses 已在执行，故拦截紧接着的 XML 读取入口。
    public EmbeddedBootstrap(ModContentPack content) : base(content)
    {
        if (AppDomain.CurrentDomain.GetData(ScopedCoordinatorInstalled) != null) return;
        new Harmony("Chezhou.RimUnity.Embedded.ScopedCoordinator").Patch(AccessTools.Method(typeof(LoadedModManager), "LoadModXML"),
            prefix: new HarmonyMethod(typeof(EmbeddedBootstrap), nameof(Prepare))
            {
                priority = Priority.First,
                before = new[] { "Chezhou.RimUnity.Embedded", "Chezhou.RimUnity.Embedded.Deferred", "Chezhou.RimUnity.Embedded.Coordinator" }
            });
        AppDomain.CurrentDomain.SetData(ScopedCoordinatorInstalled, true);
        //旧包先构造时由排序接管；本入口先构造时阻止旧包安装全局初始化入口。
        AppDomain.CurrentDomain.SetData(CoordinatorInstalled, true);
        //同时兼容旧式和 0.1.13 入口；旧入口先安装时仍由新版 Prefix 提前接管。
        AppDomain.CurrentDomain.SetData(DeferredInstalled, true);
        //新入口先构造时阻止旧包安装；旧入口先构造时由上面的 before 保证接管。
        AppDomain.CurrentDomain.SetData(Installed, true);
    }

    //在 Def 解析前完成运行时、资源和内容脚本的唯一一次装载。
    private static void Prepare()
    {
        if (AppDomain.CurrentDomain.GetData(Finished) != null) return;
        //沿用旧包的完成标记，在任何回调前登记，使旧 Prefix 和重入均跳过。
        AppDomain.CurrentDomain.SetData(Finished, true);
        try { RuntimeSelection.Initialize(); }
        catch (Exception error)
        {
            string message = "[RimUnity] 内置运行时加载失败；请检查前置版本和模组打包。/ Embedded runtime failed; check dependencies and packaging.\n" + error;
            Log.Error(message);
            LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(new Dialog_MessageBox(message)));
        }
    }
}
