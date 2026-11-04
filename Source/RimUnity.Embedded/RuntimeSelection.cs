using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Verse;

namespace RimUnity.Embedded;

//独立版优先，兼容普通 DLL 的提前类型扫描；无独立版时选择最高兼容内置载荷。
internal static class RuntimeSelection
{
    private static readonly string[] Names = { "RimUnity.Core", "RimUnity", "RimUnity.Spine" };

    internal static void Initialize()
    {
        var mods = LoadedModManager.RunningModsListForReading;
        //内容参与者不必携带运行时；仅扫描启用模组的有效加载目录。
        var content = new DeferredContent(mods);
        var copies = new List<(string Path, ModContentPack Owner)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.OrderBy(m => m.PackageId, StringComparer.Ordinal)
            .ThenBy(m => Path.GetFullPath(m.RootDir), StringComparer.Ordinal))
        foreach (string folder in mod.foldersToLoadDescendingOrder)
        {
            string directory = Path.Combine(folder, "Assemblies");
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.GetFiles(directory, "RimUnity.Embedded.dll", SearchOption.AllDirectories))
                if (paths.Add(Path.GetFullPath(file))) copies.Add((Path.GetFullPath(file), mod));
        }
        Assembly runtime;
        ModContentPack runtimeOwner;
        string root, provider, hash = "";
        bool standalone;
        bool deferred;
        //以原版登记的真实程序集归属识别运行时，不依赖管理器可改写的包 ID、名称或工坊链接。
        var runtimeOwners = mods.Where(m => m.assemblies.loadedAssemblies.Any(a => a.GetName().Name == "RimUnity")).ToArray();
        if (runtimeOwners.Length > 1)
            throw new InvalidOperationException("RimUnity runtime owner is ambiguous: " + string.Join(", ", runtimeOwners.Select(m => m.PackageId)));
        var standaloneMod = runtimeOwners.SingleOrDefault();
        if (standaloneMod != null)
        {
            runtimeOwner = standaloneMod;
            //独立版已由原版加载，必须共用其类型，不能再加载或替换第二套运行时。
            runtime = standaloneMod.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
            string protocol = runtime.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RimUnityProtocol")?.Value;
            var version = Version.Parse(runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version);
            if (protocol != "1" || version < content.Minimum)
                throw new InvalidOperationException($"独立 RimUnity 版本不兼容，请更新独立版。/ Update standalone RimUnity. Required: {content.Minimum}, installed: {version}");
            VerifyLoadedRuntime(standaloneMod.assemblies.loadedAssemblies);
            root = FindRuntimeResources(standaloneMod, runtime);
            provider = standaloneMod.Name;
            standalone = true;
            deferred = false;
        }
        else
        {
            var payloads = new List<(RuntimePayload Payload, ModContentPack Owner)>();
            var rejected = new List<string>();
            foreach (var copy in copies.OrderBy(c => c.Owner.PackageId, StringComparer.Ordinal)
                .ThenBy(c => c.Path, StringComparer.Ordinal))
            {
                try { payloads.Add((RuntimePayload.Read(copy.Path), copy.Owner)); }
                catch (Exception error) when (CandidateError(error))
                { Reject(rejected, copy.Path, error.Message); }
            }
            foreach (var group in payloads.GroupBy(c => c.Payload.Version).Where(g => g.Select(c => c.Payload.Hash).Distinct().Count() > 1))
                Log.Warning($"[RimUnity] Different embedded payloads at version {group.Key}; selecting by UTC build time then ordinal hash. " +
                    string.Join("; ", group.Select(c => $"{c.Owner.PackageId}: {c.Payload.Path}, {c.Payload.BuiltAtUtc:O}, {c.Payload.Hash}")));
            foreach (var candidate in payloads.Where(c => c.Payload.Protocol != 1 || c.Payload.Version < content.Minimum))
                Reject(rejected, candidate.Payload.Path, $"incompatible protocol/version: {candidate.Payload.Protocol}/{candidate.Payload.Version}; minimum: {content.Minimum}");
            var compatible = payloads.Where(c => c.Payload.Protocol == 1 && c.Payload.Version >= content.Minimum)
                .OrderByDescending(c => c.Payload.Version).ThenByDescending(c => c.Payload.BuiltAtUtc)
                .ThenBy(c => c.Payload.Hash, StringComparer.Ordinal)
                .ThenBy(c => c.Owner.PackageId, StringComparer.Ordinal).ThenBy(c => c.Payload.Path, StringComparer.Ordinal)
                .GroupBy(c => c.Payload.Hash, StringComparer.Ordinal).Select(g => g.First()).ToArray();
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => Names.Contains(a.GetName().Name)))
                throw new InvalidOperationException("检测到提前加载的 RimUnity DLL，请勿将 References 放入模组。/ Runtime DLLs loaded early; do not deploy References.");
            (RuntimePayload Payload, ModContentPack Owner) selected = default;
            root = null;
            foreach (var candidate in compatible)
            {
                try
                {
                    root = candidate.Payload.Prepare(Path.Combine(GenFilePaths.SaveDataFolderPath, "RimUnity", "Embedded"));
                    selected = candidate;
                    break;
                }
                catch (Exception error) when (CandidateError(error))
                { Reject(rejected, candidate.Payload.Path, error.Message); }
            }
            if (root == null) throw new InvalidOperationException("没有兼容运行库 / No compatible embedded runtime: " + content.Minimum +
                "\n" + string.Join("\n", rejected));
            runtimeOwner = selected.Owner;
            Log.Message($"[RimUnity] Selected embedded runtime: {selected.Payload.Version}; builtAtUtc: {selected.Payload.BuiltAtUtc:O}; provider: {selected.Owner.PackageId}; path: {selected.Payload.Path}; payload: {selected.Payload.Hash}; order: version, UTC build time, ordinal hash");
            //不可逆边界：从这里开始禁止切换载荷，即使后续初始化失败。
            hash = selected.Payload.Hash;
            provider = selected.Owner.Name;
            standalone = false;
            deferred = true;
            try
            {
                foreach (string name in Names)
                    selected.Owner.assemblies.loadedAssemblies.Add(Assembly.LoadFrom(Path.Combine(root, "Assemblies", name + ".dll")));
                runtime = selected.Owner.assemblies.loadedAssemblies.Single(a => a.GetName().Name == "RimUnity");
                selected.Owner.foldersToLoadDescendingOrder.Add(root);
                string assetPackPath = Path.Combine(root, "Assets.zip");
                if (File.Exists(assetPackPath))
                {
                    var pack = Waylith.Assets.AssetPack.TryOpen(assetPackPath)
                        ?? throw new InvalidDataException("Cannot open runtime asset pack: " + assetPackPath);
                    try { Waylith.Assets.ContentPack.Mount(root, pack, Waylith.Assets.AssetPackManifest.ReadStamps(assetPackPath)); }
                    catch { pack.Dispose(); throw; }
                }
                ShaderDefRegistry.RegisterShaderDirectories();
                VerifyLoadedRuntime(selected.Owner.assemblies.loadedAssemblies);
            }
            catch (Exception error)
            { throw new InvalidOperationException("Runtime assembly load/resource mount failed after commit; fallback is disabled. Payload: " + hash, error); }
        }
        try
        {
            runtime.GetType("RimUnity.RuntimeInfo", true).GetMethod("Configure").Invoke(null,
                new object[] { root, standalone, provider, copies.Count, hash });
            if (deferred)
            {
                GenTypes.ClearCache();
                DeferredModInitialization.Initialize(runtime, runtimeOwner);
            }
            if (!LoadedModManager.ModHandles.Any(m => m.GetType().Assembly == runtime && m.GetType().FullName == "RimUnity.RimUnityMod"))
                throw new InvalidOperationException("运行时初始化失败，停止加载内容脚本。/ Runtime initialization failed; content scripts were not loaded.");
            if (content.Files.Count != 0)
            {
                var loaded = content.Load();
                GenTypes.ClearCache();
                foreach (var entry in loaded)
                    DeferredModInitialization.Initialize(entry.Assembly, entry.Owner);
            }
            Log.Message($"[RimUnity] {(standalone ? "Standalone" : "Embedded")} provider: {provider}; version: {runtime.GetCustomAttribute<AssemblyFileVersionAttribute>().Version}; candidates: {copies.Count}; payload: {hash}");
        }
        catch (Exception error)
        { throw new InvalidOperationException("Runtime/content initialization failed after runtime selection; fallback is disabled. Provider: " + provider, error); }

    }

    private static bool CandidateError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        BadImageFormatException or InvalidOperationException or FormatException or JsonException or ArgumentException or OverflowException;

    private static void Reject(List<string> rejected, string path, string reason)
    {
        string message = path + ": " + reason;
        rejected.Add(message);
        Log.Warning("[RimUnity] Skipping embedded candidate: " + message);
    }

    //资源必须来自该运行时的有效加载目录；散装 References 不能冒充完整独立包。
    private static string FindRuntimeResources(ModContentPack owner, Assembly runtime)
    {
        string location = Path.GetFullPath(runtime.Location);
        string root = owner.foldersToLoadDescendingOrder.OrderByDescending(f => f.Length).FirstOrDefault(folder =>
            location.StartsWith(Path.GetFullPath(Path.Combine(folder, "Assemblies")) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        if (root == null || !File.Exists(Path.Combine(root, "Defs", "RimUnityShaders.xml")) ||
            !Directory.Exists(Path.Combine(root, "Shaders", "RimUnity")))
            throw new InvalidDataException("RimUnity runtime resources are missing from its active load folder: " + owner.RootDir);
        return root;
    }

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
