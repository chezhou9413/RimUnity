using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Verse;
using HarmonyLib;

namespace Fixture;

//仅存在于测试模组，用三个不同 Def 验证资源和实例归属。
public sealed class MarkerDef : Def { }

//游戏完成加载后记录实际结果，测试专用启动参数允许自动退出。
[StaticConstructorOnStartup]
public static class StartupReport
{
    static StartupReport()
    {
        if (GenCommandLine.CommandLineArgPassed("RimUnityEmbeddedTest"))
            new Harmony("RimUnity.EmbeddedVerification.Menu").Patch(
                AccessTools.Method(typeof(RimWorld.MainMenuDrawer), "MainMenuOnGUI"),
                postfix: new HarmonyMethod(typeof(StartupReport), nameof(MenuDrawn)));
    }

    private static int menuDraws;
    private static bool reported;
    private static void MenuDrawn()
    {
        if (reported || ++menuDraws < 3) return;
        reported = true;
        Write();
    }

    //不替换真实运行时、Shader 注册或原生挂载；报告来自游戏内实际状态。
    private static void Write()
    {
        string error = null;
        bool ordinaryDll = false;
        try
        {
            if (!RimUnity.RimUnityMod.IsRuntimeReady) throw new Exception("Runtime host not installed");
            if (LoadedModManager.ModHandles.Count(m => m.GetType().FullName == "RimUnity.RimUnityMod") != 1) throw new Exception("Runtime Mod count != 1");
            foreach (string name in new[] { "RimUnity.Core", "RimUnity", "RimUnity.Spine" })
                if (AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == name) != 1) throw new Exception("Duplicate runtime assembly: " + name);
            foreach (string name in new[] { "RimUnity_Unlit", "RimUnity_StandardParticle0" })
                if (DefDatabase<ShaderDef>.AllDefsListForReading.Count(d => d.defName == name) != 1) throw new Exception("Built-in ShaderDef count != 1: " + name);
            foreach (string id in new[] { "a", "b", "c" })
                if (DefDatabase<MarkerDef>.GetNamed("EmbeddedTest_" + id).modContentPack.PackageId != "test." + id) throw new Exception("Def ownership mismatch: " + id);
            foreach (var mod in LoadedModManager.ModHandles.Where(m => m.GetType().Namespace == "Fixture"))
                if (!mod.Content.assemblies.loadedAssemblies.Contains(mod.GetType().Assembly)) throw new Exception("Mod ownership mismatch");
            var instance = new RimUnity.GameObject("EmbeddedVerification");
            instance.AddComponent<BaseScript>();
            if (LoadedModManager.RunningMods.Any(m => m.PackageId == "test.ordinary"))
            {
                var owner = LoadedModManager.RunningMods.Single(m => m.PackageId == "test.ordinary");
                var assembly = owner.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "OrdinaryFixture");
                if (AppDomain.CurrentDomain.GetData("RimUnity.Test.OrdinaryConstructed") is not string)
                    throw new Exception("Ordinary DLL Mod constructor did not run");
                instance.AddComponent(assembly.GetType("Fixture.OrdinaryScript", true));
                ordinaryDll = true;
            }
            RimUnity.Object.Destroy(instance);
        }
        catch (Exception exception) { error = exception.ToString(); Log.Error("[EmbeddedTest] " + error); }
        var report = new { menuDraws, passed = error == null, error, ordinaryDll, source = RimUnity.RuntimeInfo.IsStandalone ? "standalone" : "embedded",
            version = RimUnity.RuntimeInfo.Version, provider = RimUnity.RuntimeInfo.Provider, copies = RimUnity.RuntimeInfo.EmbeddedCopies,
            resourceRoot = RimUnity.RuntimeInfo.ResourceRoot, runtimeOwner = RimUnity.RimUnityMod.OwnContent.PackageId, markers = DefDatabase<MarkerDef>.AllDefsListForReading.Select(d => new { d.defName, owner = d.modContentPack.PackageId }).ToArray() };
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity-embedded-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Log.Message("[EmbeddedTest] " + JsonSerializer.Serialize(report));
        Environment.Exit(0);
    }
}
