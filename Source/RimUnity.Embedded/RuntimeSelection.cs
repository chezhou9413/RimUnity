using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimUnity.Embedded;

//独立前置优先；内置载荷只在没有前置时参与版本选择。
internal static class RuntimeSelection
{
    private static readonly string[] Names = { "RimUnity.Core", "RimUnity", "RimUnity.Spine" };

    //仅遍历启用模组的有效目录，忽略未启用及条件禁用的内置包。
    internal static void Initialize()
    {
        var mods = LoadedModManager.RunningModsListForReading;
        //原版已完成 DLL 扫描，直接复用登记结果；不遍历无内置入口的模组目录。
        var participants = mods.Where(m => m.assemblies.loadedAssemblies.Any(a =>
            a.GetName().Name.StartsWith("RimUnity.Embedded.", StringComparison.Ordinal))).ToArray();
        var content = new DeferredContent(participants);
        var copies = new List<(string Path, ModContentPack Owner)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in participants)
        foreach (string folder in mod.foldersToLoadDescendingOrder)
        {
            string directory = Path.Combine(folder, "Assemblies");
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.GetFiles(directory, "RimUnity.Embedded.dll", SearchOption.AllDirectories))
                if (paths.Add(file)) copies.Add((file, mod));
        }
        var standalone = mods.SingleOrDefault(m => string.Equals(m.PackageId, "Chezhou.RimUnity", StringComparison.OrdinalIgnoreCase));
        Assembly runtime;
        string root, provider, hash = "";
        if (standalone != null)
        {
            runtime = standalone.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
            string protocol = runtime.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RimUnityProtocol")?.Value;
            var version = Version.Parse(runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            if (protocol != "1" || version < content.Minimum)
                throw new InvalidOperationException($"独立 RimUnity 版本不兼容，必须升级前置；不会改用内置版。/ Update standalone RimUnity; embedded fallback is disabled. Required: {content.Minimum}, installed: {version}");
            root = Path.Combine(standalone.RootDir, "1.7");
            provider = standalone.Name;
            VerifyLoadedRuntime(standalone.assemblies.loadedAssemblies);
        }
        else
        {
            //副本逐个校验，但相同载荷只保留一份字节，避免副本数放大常驻内存。
            var unique = new Dictionary<string, RuntimePayload>(StringComparer.Ordinal);
            var payloads = new List<(RuntimePayload Payload, ModContentPack Owner)>();
            foreach (var copy in copies)
            {
                var payload = RuntimePayload.Read(copy.Path);
                if (!unique.TryGetValue(payload.Hash, out var shared)) unique.Add(payload.Hash, shared = payload);
                payloads.Add((shared, copy.Owner));
            }
            foreach (var group in payloads.GroupBy(c => c.Payload.Version))
                if (group.Select(c => c.Payload.Hash).Distinct().Count() != 1)
                    throw new InvalidDataException("相同版本内置包载荷冲突 / Conflicting embedded payloads: " + group.Key);
            var compatible = payloads.Where(c => c.Payload.Protocol == 1 && c.Payload.Version >= content.Minimum)
                .OrderByDescending(c => c.Payload.Version).ThenBy(c => c.Owner.PackageId, StringComparer.Ordinal).ToArray();
            if (compatible.Length == 0) throw new InvalidOperationException("没有兼容内置包 / No compatible embedded runtime: " + content.Minimum);
            var selected = compatible[0];
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => Names.Contains(a.GetName().Name)))
                throw new InvalidOperationException("检测到提前加载的 RimUnity DLL，请勿将 References 放入模组。/ Runtime DLLs loaded early; do not deploy References.");
            root = selected.Payload.Extract(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity", "Embedded"));
            hash = selected.Payload.Hash;
            provider = selected.Owner.Name;
            foreach (string name in Names)
                selected.Owner.assemblies.loadedAssemblies.Add(Assembly.LoadFrom(Path.Combine(root, "Assemblies", name + ".dll")));
            runtime = selected.Owner.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
            selected.Owner.foldersToLoadDescendingOrder.Add(root);
            UnityStubs.ContentPacks.MountLoadFolder(root);
            ShaderDefRegistry.RegisterShaderDirectories();
        }
        runtime.GetType("RimUnity.RuntimeInfo", true).GetMethod("Configure").Invoke(null,
            new object[] { root, standalone != null, provider, copies.Count, hash });
        if (standalone == null)
        {
            GenTypes.ClearCache();
            LoadedModManager.CreateModClasses();
        }
        if (!LoadedModManager.ModHandles.Any(m => m.GetType().Assembly == runtime && m.GetType().FullName == "RimUnity.RimUnityMod"))
            throw new InvalidOperationException("运行时初始化失败，停止加载内容脚本。/ Runtime initialization failed; content scripts were not loaded.");
        if (content.Files.Count != 0)
        {
            content.Load();
            GenTypes.ClearCache();
            LoadedModManager.CreateModClasses();
        }
        Log.Message($"[RimUnity] {(standalone != null ? "Standalone" : "Embedded")} provider: {provider}; embedded copies: {copies.Count}; payload: {hash}");
    }

    //前置必须实际拥有全套程序集，不能与其他模组的散装引用混用。
    private static void VerifyLoadedRuntime(IEnumerable<Assembly> owned)
    {
        foreach (string name in Names)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == name).ToArray();
            if (matches.Length != 1 || !owned.Contains(matches[0]))
                throw new InvalidOperationException("RimUnity assembly ownership conflict: " + name);
        }
    }
}
