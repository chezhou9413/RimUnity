using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimUnity.Embedded;

//游戏只提前扫描这个入口；实际运行时与脚本在首次 Mod 实例创建完成后加载。
public sealed class EmbeddedBootstrap : Mod
{
    private const string Installed = "RimUnity.Embedded.Installed";
    private const string Finished = "RimUnity.Embedded.Finished";

    //首次构造时原 CreateModClasses 已在执行，故拦截紧接着的 XML 读取入口。
    public EmbeddedBootstrap(ModContentPack content) : base(content)
    {
        if (AppDomain.CurrentDomain.GetData(Installed) != null) return;
        AppDomain.CurrentDomain.SetData(Installed, true);
        new Harmony("Chezhou.RimUnity.Embedded").Patch(AccessTools.Method(typeof(LoadedModManager), "LoadModXML"),
            prefix: new HarmonyMethod(typeof(EmbeddedBootstrap), nameof(Prepare)));
    }

    //在 Def 解析前完成运行时、资源和内容脚本的唯一一次装载。
    private static void Prepare()
    {
        if (AppDomain.CurrentDomain.GetData(Finished) != null) return;
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
