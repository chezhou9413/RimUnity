using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

//用实际 CLR、游戏 Mod 工厂和发行版 DLL 检查启动顺序，禁止进入游戏渲染。
internal static class Harness
{
    private static readonly Harmony harmony = new("RimUnity.OfflineVerification");
    private static int runtimeInitializations, mounted, shaderRegistrations;
    private static string cache;
    private static readonly List<string> errors = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(string[] args)
    {
        string repo = args[0], fixtures = args[3], scenario = args[4];
        cache = Path.Combine(fixtures, "Cache-" + scenario);
        Patch(AccessTools.PropertyGetter(typeof(GenFilePaths), "SaveDataFolderPath"), nameof(CachePath));
        Patch(AccessTools.Method(typeof(UnityStubs.ContentPacks), "MountLoadFolder"), nameof(Mount));
        Patch(AccessTools.Method(typeof(ShaderDefRegistry), "RegisterShaderDirectories"), nameof(Shaders));
        Patch(AccessTools.PropertyGetter(typeof(GenTypes), "AllTypes"), nameof(Types));
        Patch(AccessTools.Method(typeof(DeepProfiler), "Start"), nameof(Defer));
        Patch(AccessTools.Method(typeof(DeepProfiler), "End"), nameof(Defer));
        foreach (var method in typeof(Log).GetMethods().Where(m => new[] { "Message", "Warning", "Error" }.Contains(m.Name) && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
            Patch(method, nameof(LogLine));
        Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteWhenFinished"), nameof(Defer));
        //XML 原文读取不属于离线测试；其前置引导器仍按真实 Harmony 顺序执行。
        harmony.Patch(AccessTools.Method(typeof(LoadedModManager), "LoadModXML"), prefix: new HarmonyMethod(typeof(Harness), nameof(NoXml)) { priority = Priority.Last });
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name == "RimUnity")
                Patch(e.LoadedAssembly.GetType("RimUnity.RimUnityMod").GetConstructor(new[] { typeof(ModContentPack) }), nameof(RuntimeConstructor));
        };
        var a = Mod(Path.Combine(fixtures, "A"), "test.a");
        var b = Mod(Path.Combine(fixtures, "B"), "test.b");
        bool workshop = scenario.Contains("workshop");
        bool renamed = scenario == "renamed-runtime-provider";
        bool incomplete = scenario == "incomplete-runtime-provider";
        bool missingResources = scenario == "missing-runtime-resources";
        bool ambiguous = scenario == "ambiguous-runtime-provider";
        bool failedMod = scenario.StartsWith("failed-mod") || scenario.StartsWith("coordinator-");
        string standaloneId = renamed ? "manager.renamed.provider__custom_suffix" : "Chezhou.RimUnity";
        var independent = Mod(Path.Combine(fixtures, scenario.StartsWith("actual-workshop") ? "Workshop" : "Standalone"),
            standaloneId.ToLowerInvariant() + (workshop ? "_steam" : ""), standaloneId);
        var ordinary = Mod(Path.Combine(fixtures, "Ordinary"), "test.ordinary");
        var mods = scenario switch
        {
            "legacy-first" => new[] { a, b },
            "legacy-last" => new[] { b, a },
            "single" => new[] { a },
            "reverse" => new[] { b, a },
            "workshop-first" or "actual-workshop-first" or "workshop-legacy-first" or "workshop-legacy-last" => new[] { independent, a, b },
            "workshop-last" or "actual-workshop-last" => new[] { a, b, independent },
            "ordinary-workshop" or "actual-workshop-ordinary" => new[] { independent, ordinary, a, b },
            "renamed-runtime-provider" or "incomplete-runtime-provider" or "ambiguous-runtime-provider" or "missing-runtime-resources" => new[] { independent, a, b },
            "standalone-only" => new[] { independent },
            "ordinary-standalone" => new[] { independent, ordinary, a, b },
            "failed-mod-standalone" => new[] { independent, a, b },
            "ordinary-standalone-only" => new[] { independent, ordinary },
            "standalone-newer-payload" => new[] { independent, a, b },
            "standalone-conflicting-payloads" => new[] { independent, a, b },
            "standalone-first" => new[] { independent, a, b },
            "standalone-middle" => new[] { a, independent, b },
            "standalone-last" => new[] { b, a, independent },
            "incompatible-standalone" => new[] { a, independent, b },
            _ => new[] { a, b }
        };
        var active = LoadedModManager.RunningModsListForReading;
        active.AddRange(mods);
        if (scenario == "scale")
            for (int i = 0; i < 5000; i++)
            {
                var unrelated = Mod(Path.Combine(fixtures, "Unrelated", i.ToString()), "scale." + i);
                unrelated.assemblies.loadedAssemblies.Add(typeof(Harness).Assembly);
                active.Add(unrelated);
            }
        //等价于原版 ReloadAll：先 LoadFrom，再 GetTypes，最后登记。
        foreach (var mod in mods)
        foreach (string file in Directory.GetFiles(Path.Combine(mod.foldersToLoadDescendingOrder[0], "Assemblies"), "*.dll"))
        {
            var assembly = Assembly.LoadFrom(file);
            assembly.GetTypes();
            mod.assemblies.loadedAssemblies.Add(assembly);
        }
        if (failedMod)
        {
            var unrelated = Mod(Path.Combine(fixtures, "Failed"), "test.failed");
            unrelated.assemblies.loadedAssemblies.Add(typeof(FailingMod).Assembly);
            active.Add(unrelated);
        }
        if (missingResources) independent.foldersToLoadDescendingOrder.Clear();
        if (incomplete)
            independent.assemblies.loadedAssemblies.RemoveAll(x => x.GetName().Name == "RimUnity.Spine");
        if (ambiguous)
            a.assemblies.loadedAssemblies.AddRange(independent.assemblies.loadedAssemblies);
        Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "EmbeddedFixture.Library"), "content is deferred before Mod construction");
        bool standalone = mods.Contains(independent);
        Assert(AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "RimUnity") == standalone,
            "standalone runtime is available during ordinary DLL scanning; embedded stays deferred");
        if (mods.Contains(ordinary))
        {
            var ordinaryAssembly = ordinary.assemblies.loadedAssemblies.Single(x => x.GetName().Name == "OrdinaryFixture");
            Assert(ordinaryAssembly.GetType("Fixture.OrdinaryScript").BaseType.FullName == "RimUnity.MonoBehaviour", "ordinary DLL inheritance resolves before Mod construction");
        }
        if (scenario.StartsWith("legacy") || scenario.StartsWith("workshop-legacy") || scenario.StartsWith("coordinator-"))
        {
            var first = scenario.EndsWith("first") ? a : b;
            var bootstrap = first.assemblies.loadedAssemblies.Single(x => x.GetName().Name.StartsWith("RimUnity.Embedded."))
                .GetType("RimUnity.Embedded.EmbeddedBootstrap", true);
            bootstrap.GetConstructor(new[] { typeof(ModContentPack) }).Invoke(new object[] { first });
            string owner = scenario.EndsWith("first")
                ? scenario.StartsWith("coordinator-") ? "Chezhou.RimUnity.Embedded.Coordinator"
                    : scenario.StartsWith("workshop-legacy") ? "Chezhou.RimUnity.Embedded.Deferred" : "Chezhou.RimUnity.Embedded"
                : "Chezhou.RimUnity.Embedded.ScopedCoordinator";
            Assert(Harmony.GetPatchInfo(AccessTools.Method(typeof(LoadedModManager), "LoadModXML")).Prefixes.Any(p => p.owner == owner), "requested bootstrap really constructs first");
        }
        LoadedModManager.CreateModClasses();
        if (failedMod)
        {
            Assert(FailingMod.Attempts == 1 && errors.Count == 1 && errors[0].Contains(FailingMod.Failure),
                "unrelated Mod fails once during the game's initial construction pass");
            errors.Clear();
        }
        if (mods.Contains(ordinary))
        {
            Assert(AppDomain.CurrentDomain.GetData("RimUnity.Test.OrdinaryConstructed") is string, "ordinary Mod accesses runtime before XML bootstrap");
            Assert(LoadedModManager.ModHandles.Single(m => m.GetType().FullName == "Fixture.OrdinaryMod").Content == ordinary, "ordinary Mod retains its owner");
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        LoadedModManager.LoadModXML();
        Console.WriteLine($"TIMING {scenario}: {active.Count} active mods; bootstrap {timer.Elapsed.TotalMilliseconds:F2} ms");
        LoadedModManager.LoadModXML();
        if (failedMod)
        {
            Assert(FailingMod.Attempts == 1, "runtime, deferred scripts and repeated XML never retry an unrelated failed Mod");
            Assert(!LoadedModManager.ModHandles.Any(m => m is FailingMod), "failed Mod is not falsely registered as initialized");
        }
        if (scenario == "incompatible-standalone" || scenario == "conflict" || incomplete || ambiguous || missingResources)
        {
            string expected = missingResources ? "runtime resources are missing" : incomplete ? "assembly ownership conflict" : ambiguous ? "runtime owner is ambiguous" : scenario == "conflict" ? "Conflicting embedded payloads" : "Update standalone RimUnity";
            Assert(errors.Count == 1 && errors[0].Contains(expected), "explicit error: " + scenario);
            Assert(mounted == 0 && !Directory.Exists(cache), "failure never mounts or extracts fallback resources");
            Assert(runtimeInitializations == (standalone ? 1 : 0), "no extra runtime on failure");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "EmbeddedFixture.Library"), "incompatible content not loaded");
            return;
        }
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
        var selectedRuntime = AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "RimUnity");
        Assert((bool)selectedRuntime.GetType("RimUnity.RuntimeInfo").GetProperty("IsStandalone").GetValue(null) == standalone, "standalone is used when enabled; otherwise embedded supplies runtime");
        if (standalone && scenario != "standalone-only" && scenario != "ordinary-standalone-only")
            Assert((string)selectedRuntime.GetType("RimUnity.RuntimeInfo").GetProperty("ResourceRoot").GetValue(null) == Path.Combine(independent.RootDir, "1.7"), "runtime and resources retain actual standalone owner");
        Assert(runtimeInitializations == 1, "one runtime initialization across repeated XML calls");
        Assert(mounted == (standalone ? 0 : 1), "only selected payload resources mount");
        Assert(shaderRegistrations == (standalone ? 0 : 1), "shader registration matches selected source");
        Assert(active.Sum(m => m.foldersToLoadDescendingOrder.Count - 1) == (standalone ? 0 : 1), "one Def resource folder, independent content folders preserved");
        Assert(Directory.Exists(cache) == !standalone, "standalone never extracts or mounts an embedded payload");
        foreach (string name in new[] { "RimUnity", "RimUnity.Core", "RimUnity.Spine" })
            Assert(AppDomain.CurrentDomain.GetAssemblies().Count(x => x.GetName().Name == name) == 1, "unique CLR identity: " + name);
        if (scenario == "standalone-only" || scenario == "ordinary-standalone-only") { Console.WriteLine("PASS scenario " + scenario); return; }
        var library = a.assemblies.loadedAssemblies.Single(x => x.GetName().Name == "EmbeddedFixture.Library");
        if (scenario == "newest")
            Assert((string)AppDomain.CurrentDomain.GetAssemblies().Single(x => x.GetName().Name == "RimUnity").GetType("RimUnity.RuntimeInfo").GetProperty("Provider").GetValue(null) == "test.b", "newer compatible payload wins over first-loaded container");
        Assert(library.GetType("Fixture.BaseScript").BaseType.FullName == "RimUnity.MonoBehaviour", "MonoBehaviour inheritance resolves to real Core");
        Assert(LoadedModManager.ModHandles.Single(m => m.GetType().FullName == "Fixture.LibraryMod").Content == a, "library Mod retains owner");
        if (mods.Contains(b))
        {
            var consumer = b.assemblies.loadedAssemblies.Single(x => x.GetName().Name == "EmbeddedFixture.Consumer");
            Assert(consumer.GetType("Fixture.DerivedScript").BaseType == library.GetType("Fixture.BaseScript"), "cross-assembly inheritance shares type identity");
            Assert(LoadedModManager.ModHandles.Single(m => m.GetType().FullName == "Fixture.ConsumerMod").Content == b, "consumer Mod retains owner");
        }
        Console.WriteLine("PASS scenario " + scenario);
    }

    //避免构造 ModContentPack 时调用原生内容系统，字段结构来自当前游戏源码。
    private static ModContentPack Mod(string root, string id, string declaredId = null)
    {
        var mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(mod, new DirectoryInfo(root));
        AccessTools.Field(typeof(ModContentPack), "packageIdInt").SetValue(mod, id);
        AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(mod, declaredId ?? id);
        AccessTools.Field(typeof(ModContentPack), "nameInt").SetValue(mod, id);
        mod.foldersToLoadDescendingOrder = new() { Path.Combine(root, "1.7") };
        mod.assemblies = new ModAssemblyHandler(mod);
        return mod;
    }

    //实际工厂仍构造 Mod，只有需要原生宿主的运行时初始化被隔离。
    private static bool RuntimeConstructor(Mod __instance, ModContentPack content)
    {
        runtimeInitializations++;
        AccessTools.Field(typeof(Mod), "intContent").SetValue(__instance, content);
        return false;
    }
    private static void Patch(MethodBase target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(Harness), prefix));
    private static bool CachePath(ref string __result) { __result = cache; return false; }
    private static bool Mount() { mounted++; return false; }
    private static bool Shaders() { shaderRegistrations++; return false; }
    private static bool Types(ref List<Type> __result) { __result = LoadedModManager.RunningMods.SelectMany(m => m.assemblies.loadedAssemblies).Distinct().SelectMany(a => a.GetTypes()).Where(t => t != typeof(FailingMod) || LoadedModManager.RunningMods.Any(m => m.PackageId == "test.failed")).ToList(); return false; }
    private static bool LogLine(MethodBase __originalMethod, string __0) { if (__originalMethod.Name == "Error") errors.Add(__0); Console.WriteLine(__0); return false; }
    private static bool Defer() => false;
    private static bool NoXml(ref List<LoadableXmlAsset> __result) { __result = new(); return false; }
    private static void Assert(bool value, string message) { if (!value) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); }
}
