using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimUnity.Embedded;

//每份新版容器登记入口，在 XML 前按元数据选举唯一协调者；不以 Mod 构造顺序决定加载逻辑。
public sealed class EmbeddedBootstrap : Mod
{
    private const string Finished = "RimUnity.Embedded.Finished";
    private const string Coordinator = "Chezhou.RimUnity.Embedded.TimestampCoordinator.";

    public EmbeddedBootstrap(ModContentPack content) : base(content)
    {
        var assembly = typeof(EmbeddedBootstrap).Assembly;
        string installed = Coordinator + assembly.FullName;
        if (AppDomain.CurrentDomain.GetData(installed) != null) return;
        var target = AccessTools.Method(typeof(LoadedModManager), "LoadModXML")
            ?? throw new MissingMethodException(typeof(LoadedModManager).FullName, "LoadModXML");
        new Harmony(installed).Patch(target, prefix: new HarmonyMethod(typeof(EmbeddedBootstrap), nameof(Prepare))
        {
            priority = Priority.First,
            before = new[] { "Chezhou.RimUnity.Embedded.ScopedCoordinator", "Chezhou.RimUnity.Embedded.Coordinator",
                "Chezhou.RimUnity.Embedded.Deferred", "Chezhou.RimUnity.Embedded", "Chezhou.RimUnity.Embedded.TimestampCoordinator" }
        });
        AppDomain.CurrentDomain.SetData(installed, true);
        foreach (string key in new[] { "ScopedCoordinatorInstalled", "CoordinatorInstalled", "DeferredInstalled", "Installed" })
            AppDomain.CurrentDomain.SetData("RimUnity.Embedded." + key, true);
    }

    private static void Prepare()
    {
        if (AppDomain.CurrentDomain.GetData(Finished) != null) return;
        var elected = LoadedModManager.RunningModsListForReading.SelectMany(m => m.assemblies.loadedAssemblies)
            .Distinct().Where(a => a.GetName().Name.StartsWith("RimUnity.Embedded.", StringComparison.Ordinal) &&
                BuildMetadata.Revision(a) > 0)
            .OrderByDescending(BuildMetadata.Revision).ThenByDescending(BuildMetadata.Time)
            .ThenBy(a => a.FullName, StringComparer.Ordinal).First();
        if (elected != typeof(EmbeddedBootstrap).Assembly) return;
        //在首次回调之前登记，阻止旧入口、重入及重复 XML 调用重新初始化。
        AppDomain.CurrentDomain.SetData(Finished, true);
        try
        {
            Log.Message($"[RimUnity] Embedded coordinator: {elected.GetName().Name}; revision: {BuildMetadata.Revision(elected)}; builtAtUtc: {BuildMetadata.Time(elected):O}");
            RuntimeSelection.Initialize();
        }
        catch (Exception error)
        {
            string message = "[RimUnity] 内置运行时加载失败；请检查前置版本和模组打包。/ Embedded runtime failed; check dependencies and packaging.\n" + error;
            Log.Error(message);
            LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(new Dialog_MessageBox(message)));
        }
    }
}
