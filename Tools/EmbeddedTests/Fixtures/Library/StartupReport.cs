using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Verse;

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
            LongEventHandler.ExecuteWhenFinished(Write);
    }

    //不替换真实运行时、Shader 注册或原生挂载；报告来自游戏内实际状态。
    private static void Write()
    {
        string error = null;
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
            RimUnity.Object.Destroy(instance);
        }
        catch (Exception exception) { error = exception.ToString(); Log.Error("[EmbeddedTest] " + error); }
        var report = new { passed = error == null, error, source = RimUnity.RuntimeInfo.IsStandalone ? "standalone" : "embedded",
            version = RimUnity.RuntimeInfo.Version, provider = RimUnity.RuntimeInfo.Provider, copies = RimUnity.RuntimeInfo.EmbeddedCopies,
            resourceRoot = RimUnity.RuntimeInfo.ResourceRoot, markers = DefDatabase<MarkerDef>.AllDefsListForReading.Select(d => new { d.defName, owner = d.modContentPack.PackageId }).ToArray() };
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity-embedded-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Log.Message("[EmbeddedTest] " + JsonSerializer.Serialize(report));
        UnityEngine.Application.Quit();
    }
}
