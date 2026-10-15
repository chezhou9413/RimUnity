using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Vma;
using LudeonTK;
using RimWorld;
using Verse;
using Waylith;

namespace RimUnity.Diagnostics;

//Explicit, one-shot diagnostic capture. Stores text only; never restores/uploads textures or changes render state.
internal static partial class RenderDebugReport
{
    private static EngineApplication application;
    private static Stopwatch timer;
    private static StringBuilder initial;
    internal static volatile bool Collecting;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const int MaxText = 4 * 1024 * 1024;

    //The host ANDs state flags; zero adds no scene constraint (Entry | Playing is impossible).
    [DebugAction("RimUnity", "复制调试信息", allowedGameStates = (AllowedGameStates)0)]
    private static void Copy()
    {
        if (!Prefs.DevMode) return;
        if (Collecting) { Messages.Message("正在采集渲染信息，请稍候。", MessageTypeDefOf.NeutralEvent, false); return; }
        initial = new StringBuilder(32768);
        initial.AppendLine("RimUnity 场景与显存诊断 / report schema 1");
        initial.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        initial.AppendLine("Compatibility SystemInfo values may be placeholders; prefer native Vulkan GPU descriptions and VMA heap budgets below.");
        initial.AppendLine("Evidence: CPU state, engine allocation accounting and observed render calls. Not a GPU pixel/descriptor readback. No forced GC, device idle, texture upload, eviction or render-setting changes.");
        initial.AppendLine("Paths are redacted for the current user profile. Save contents, account tokens and full environment are not collected.");
        ClearStages();
        Section(initial, "系统、进程与显示", SystemSection);
        Section(initial, "游戏状态、模组和入口", GameSection);
        Section(initial, "设备、显存与宿主渲染器（采集前）", GraphicsSection);
        Section(initial, "资源、相机和设置（采集前）", ResourcesSection);
        application = EngineApplication.Instance;
        if (application == null) { Finish(); return; }
        timer = Stopwatch.StartNew(); Collecting = true;
        application.OnUpdate += Tick;
        application.BeforeGraphicsShutdown += Cancel;
        Messages.Message("正在采集 3 秒渲染诊断，完成后自动复制并保存报告。", MessageTypeDefOf.NeutralEvent, false);
    }
    private static void Tick()
    {
        if (timer.Elapsed.TotalSeconds < 3) return;
        Finish();
    }
    private static void Cancel(EngineApplication app) { Stop(); initial = null; ClearStages(); }
    private static void Stop()
    {
        Collecting = false;
        if (application != null) { application.OnUpdate -= Tick; application.BeforeGraphicsShutdown -= Cancel; }
        application = null; timer?.Stop();
    }
    private static void Finish()
    {
        Stop();
        var report = initial ?? new StringBuilder(); initial = null;
        Section(report, "三秒渲染调用采样", FrameSection);
        Section(report, "设备、显存与宿主渲染器（采集后）", GraphicsSection);
        Section(report, "资源、相机和设置（采集后）", ResourcesSection);
        Section(report, "程序集身份与文件指纹", AssembliesSection);
        Section(report, "已安装 Harmony 补丁", PatchesSection);
        Section(report, "近期游戏日志", LogsSection);
        report.AppendLine("\nEND OF REPORT / UTC " + DateTime.UtcNow.ToString("O"));
        string text = Redact(report.ToString());

        string path = null;
        try
        {
            string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimUnityReports"); Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "render-debug-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception e) { text += "\nReport file failed: " + Error(e); }
        try { UnityEngine.GUIUtility.systemCopyBuffer = ClipboardText(text, path); Messages.Message("调试信息已复制。报告副本：" + (path ?? "保存失败，请使用剪贴板"), MessageTypeDefOf.TaskCompletion, false); }
        catch (Exception e) { Log.Warning("[RimUnity] 剪贴板失败：" + Error(e) + "；报告：" + path); }
        ClearStages();
    }
    private static string ClipboardText(string text, string path) => text.Length <= MaxText ? text
        : text.Substring(0, MaxText) + "\n[CLIPBOARD TRUNCATED; full report file: " + Redact(path ?? "file save failed") + "]";

    private static void Section(StringBuilder text, string name, Action<StringBuilder> action)
    {
        text.AppendLine("\n========== " + name + " ==========");
        try { action(text); } catch (Exception e) { text.AppendLine("[SECTION FAILED] " + Error(e)); }
    }
    private static string Error(Exception e) => e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message;
    private static string Redact(string s)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            foreach (string path in new[] { home, home.Replace('\\', '/'), System.Text.Json.JsonSerializer.Serialize(home).Trim('"'), System.Text.Json.JsonSerializer.Serialize(home.Replace('\\', '/')).Trim('"') }.Distinct())
                s = s.Replace(path, "<USER>", StringComparison.OrdinalIgnoreCase);
        return s;
    }
    private static object Read(object target, string name)
    {
        if (target == null) return null;
        var t = target as Type ?? target.GetType(); var instance = target is Type ? null : target;
        var field = AccessTools.Field(t, name);
        if (field != null) return field.GetValue(instance);
        var property = AccessTools.Property(t, name);
        return property?.GetIndexParameters().Length == 0 ? property.GetValue(instance) : null;
    }
    private static void Value(StringBuilder b, string key, Func<object> get)
    {
        try { b.AppendLine(key + " = " + Scalar(get())); } catch (Exception e) { b.AppendLine(key + " = [UNAVAILABLE] " + Error(e)); }
    }
    private static string Scalar(object value)
    {
        if (value == null) return "<null / unavailable>";
        if (value is string s) return s.Length <= 16000 ? s : s.Substring(0, 16000) + " [truncated]";
        if (value is ICollection c) return value.GetType().Name + " Count=" + c.Count;
        if (value is Delegate d) return d.Method.DeclaringType?.FullName + "." + d.Method.Name;
        return value.ToString();
    }
    //Known diagnostic objects only. Never recursively walk a game/map graph or invoke arbitrary methods.
    private static void Dump(StringBuilder b, string label, object target, int depth = 0)
    {
        if (target == null) { b.AppendLine(label + " = <unavailable>"); return; }
        var t = target as Type ?? target.GetType();
        b.AppendLine(label + " [" + t.FullName + "]");
        if (target is string || t.IsPrimitive || t.IsEnum) { b.AppendLine(Scalar(target)); return; }
        foreach (var p in t.GetProperties(Flags).Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null && p.GetMethod.IsStatic == (target is Type)).OrderBy(p => p.Name))
        {
            if (p.Name is "deviceName" or "CommandLine" or "commandLine") continue;
            if (p.Name.Contains("Mapped", StringComparison.OrdinalIgnoreCase) || p.PropertyType.IsPointer || p.PropertyType.IsByRefLike) continue;
            Value(b, label + "." + p.Name, () => p.GetValue(target is Type ? null : target));
        }
        foreach (var f in t.GetFields(Flags).Where(f => !f.Name.Contains("k__BackingField") && f.IsStatic == (target is Type)).OrderBy(f => f.Name))
        {
            if (f.FieldType.IsPointer || f.FieldType.IsByRefLike) continue;
            if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.FieldType.IsValueType)
                Value(b, label + "." + f.Name, () => f.GetValue(target is Type ? null : target));
        }
        if (target is IEnumerable list && target is not string && depth < 2)
        {
            int n = 0; foreach (var row in list) { if (n == 256) { b.AppendLine("[list truncated at 256]"); break; } Dump(b, label + "[" + n++ + "]", row, depth + 1); }
        }
    }
    private static void SystemSection(StringBuilder b)
    {
        b.AppendLine("OS=" + RuntimeInformation.OSDescription + "; OS arch=" + RuntimeInformation.OSArchitecture + "; process=" + RuntimeInformation.ProcessArchitecture + "; runtime=" + RuntimeInformation.FrameworkDescription);
        b.AppendLine("Executable=" + Environment.ProcessPath + "; application base=" + AppContext.BaseDirectory);
        b.AppendLine("CPU threads=" + Environment.ProcessorCount + "; 64bit=" + Environment.Is64BitProcess);
        using var p = Process.GetCurrentProcess();
        b.AppendLine("Process working/private/virtual bytes=" + p.WorkingSet64 + "/" + p.PrivateMemorySize64 + "/" + p.VirtualMemorySize64 + "; peak working=" + p.PeakWorkingSet64 + "; uptime=" + (DateTime.Now - p.StartTime));
        var gc = GC.GetGCMemoryInfo(); b.AppendLine("GC managed=" + GC.GetTotalMemory(false) + "; available=" + gc.TotalAvailableMemoryBytes + "; memory load=" + gc.MemoryLoadBytes + "; high threshold=" + gc.HighMemoryLoadThresholdBytes);
        foreach (string name in new[] { "UnityEngine.SystemInfo", "UnityEngine.Screen", "UnityEngine.QualitySettings", "Verse.Prefs", "Verse.DebugViewSettings", "Verse.DebugSettings", "Verse.VersionControl" })
            Dump(b, name, AccessTools.TypeByName(name));
        foreach (string key in new[] { "WAYLITH_GPU", "WAYLITH_VRAM_BUDGET_MB", "WAYLITH_LOAD_ENCODE", "WAYLITH_VALIDATION", "VK_ICD_FILENAMES", "VK_DRIVER_FILES", "VK_INSTANCE_LAYERS", "VK_LAYER_PATH", "DRI_PRIME", "__NV_PRIME_RENDER_OFFLOAD" })
            b.AppendLine("env " + key + "=" + (Environment.GetEnvironmentVariable(key) ?? "<unset>"));
    }
    private static void GameSection(StringBuilder b)
    {
        b.AppendLine("ProgramState=" + Current.ProgramState + "; hasGame=" + (Current.Game != null) + "; map=" + (Find.CurrentMap == null ? "<none>" : Find.CurrentMap.Index + " / " + Find.CurrentMap.Size));
        b.AppendLine("LongEvent=" + LongEventHandler.AnyEventNowOrWaiting + "; Unity main thread=" + UnityData.IsInMainThread);
        b.AppendLine("Content root=" + RimUnityMod.OwnContent?.RootDir + "; package=" + RimUnityMod.OwnContent?.PackageIdPlayerFacing + "; shaders=" + RimUnityMod.ShaderDirectory);
        int i = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            b.AppendLine("MOD " + i++ + " | " + mod.PackageIdPlayerFacing + " | " + mod.Name + " | " + mod.RootDir);
            foreach (string key in new[] { "foldersToLoadDescendingOrder", "FoldersToLoad", "foldersToLoad" })
            {
                object folders = Read(mod, key);
                if (folders is IEnumerable values) b.AppendLine("  " + key + "=" + string.Join(" | ", values.Cast<object>().Select(Scalar)));
            }
        }
        foreach (string file in new[] { "BloomPrefilter.frag.glsl", "BloomDown.frag.glsl", "BloomUp.frag.glsl", "BloomComposite.frag.glsl", "HdrComposite.frag.glsl", "HdrTransfer.frag.glsl" }) Fingerprint(b, Path.Combine(RimUnityMod.ShaderDirectory, file));
    }
    private static void Fingerprint(StringBuilder b, string file)
    {
        try { var info = new FileInfo(file); if (!info.Exists) { b.AppendLine("MISSING " + file); return; } using var stream = File.OpenRead(file); b.AppendLine("FILE " + file + " | bytes=" + info.Length + " | UTC=" + info.LastWriteTimeUtc.ToString("O") + " | SHA256=" + Convert.ToHexString(SHA256.HashData(stream))); }
        catch (Exception e) { b.AppendLine("FILE " + file + " | " + Error(e)); }
    }
    private static void GraphicsSection(StringBuilder b)
    {
        var app = EngineApplication.Instance;
        Dump(b, "Engine", app);
        Dump(b, "Options", app?.Options);
        var graphics = app?.GraphicsDevice;
        Dump(b, "GraphicsDevice", graphics);
        Dump(b, "EnumeratedGPUs", Read(typeof(Waylith.Graphics.GraphicsDevice), "EnumeratedGpus"));
        Dump(b, "RenderContext", app?.RenderContext);
        Dump(b, "Swapchain surface format", Read(app?.RenderContext, "SurfaceFormat"));
        Dump(b, "Swapchain extent", Read(app?.RenderContext, "Extent"));
        Dump(b, "FrameRenderer", app?.FrameRenderer);
        Dump(b, "PhysicalDevice", app?.PhysicalDevice);
        Dump(b, "Allocator", graphics?.Allocator);
        if (graphics != null)
        {
            Section(b, "原生 Vulkan/VMA 堆预算与格式支持", info => HeapSection(info, graphics));
        }
        foreach (string name in new[] { "GetHeapBudgets", "GetBudget", "GetBudgets" })
        {
            var method = graphics?.Allocator?.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method != null) Value(b, "VMA." + name, () => { var value = method.Invoke(graphics.Allocator, null); Dump(b, "VMA heap budgets", value); return "recorded above"; });
        }
        foreach (string key in new[] { "DeviceEnabledExtensions", "InstanceEnabledExtensions", "InstanceEnabledLayers" })
            if (Read(graphics, key) is IEnumerable values) b.AppendLine(key + " = " + string.Join(", ", values.Cast<object>().Select(Scalar)));
        foreach (string name in new[] { "Waylith.Rendering.TextureResidency", "Waylith.Rendering.TextureResourceManager", "Waylith.Rendering.GpuTextureService" }) Dump(b, name, AccessTools.TypeByName(name));
        var tracker = AccessTools.TypeByName("Waylith.Graphics.GpuMemoryTracker");
        if (tracker == null) { b.AppendLine("GPU tracker unavailable"); return; }
        foreach (string name in new[] { "TotalBytes", "MeshBytes", "TextureBytes", "Snapshot" })
        {
            var method = tracker.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null) { b.AppendLine(name + " unavailable"); continue; }
            var value = method.Invoke(null, null); if (value is IEnumerable) Dump(b, "Tracked." + name, value); else b.AppendLine("Tracked." + name + "=" + value);
        }
        var allocation = tracker.GetNestedType("TrackedAllocation", BindingFlags.Public | BindingFlags.NonPublic);
        var collect = tracker.GetMethod("CollectAll", BindingFlags.Public | BindingFlags.Static);
        if (allocation != null && collect != null)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(allocation)); collect.Invoke(null, new object[] { list });
            b.AppendLine("Tracked allocations total=" + list.Count + "; largest 150 follows (allocation bytes are not device-local residency)");
            foreach (var row in list.Cast<object>().OrderByDescending(x => Convert.ToInt64(Read(x, "Bytes") ?? 0)).Take(150)) Dump(b, "allocation", row);
        }
    }
    private static void HeapSection(StringBuilder b, Waylith.Graphics.GraphicsDevice graphics)
    {
        var memory = graphics.PhysicalDevice.GetMemoryProperties();
        Span<VmaBudget> budgets = stackalloc VmaBudget[16];
        graphics.Allocator.GetHeapBudgets(budgets);
        b.AppendLine("VMA native heap budget/usage is a driver/allocator estimate, distinct from tracked allocation totals.");
        for (int i = 0; i < Math.Min(16, (int)memory.MemoryHeapCount); i++)
        {
            var heap = memory.MemoryHeaps[i];
            b.AppendLine("HEAP " + i + " size=" + heap.Size + " flags=" + heap.Flags);
            Dump(b, "heap budget " + i, budgets[i]);
            Dump(b, "heap statistics " + i, Read(budgets[i], "Statistics"));
        }
        var properties = graphics.PhysicalDevice.GetProperties();
        Dump(b, "Vulkan physical properties", properties);
        Dump(b, "Vulkan device limits", Read(properties, "Limits"));
        foreach (var format in new[] { VkFormat.R16G16B16A16SFloat, VkFormat.B8G8R8A8UNorm, VkFormat.R8G8B8A8UNorm, VkFormat.R8G8B8A8SRGB, VkFormat.D32SFloat, VkFormat.D24UNormS8UInt, VkFormat.Bc7UNormBlock })
            Dump(b, "Format " + format, graphics.PhysicalDevice.GetFormatProperties(format));
    }
    private static void AssembliesSection(StringBuilder b)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
        {
            try
            {
                b.AppendLine("ASM " + a.FullName + " | MVID=" + a.ManifestModule.ModuleVersionId + " | " + (a.IsDynamic ? "<dynamic>" : a.Location));
                string name = a.GetName().Name;
                if (!a.IsDynamic && (name.StartsWith("RimUnity") || name is "Assembly-CSharp" or "RimWorld" or "Waylith" or "UnityStubs" or "0Harmony" || a.Location.Contains("Mods", StringComparison.OrdinalIgnoreCase) || a.Location.Contains("workshop", StringComparison.OrdinalIgnoreCase))) Fingerprint(b, a.Location);
            }
            catch (Exception e) { b.AppendLine("ASM failed " + Error(e)); }
        }
        using var process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
            if (new[] { "vulkan", "nvidia", "nvoglv", "amd", "atid", "intel", "igd", "slang", "dxgi", "renderdoc" }.Any(x => module.ModuleName.Contains(x, StringComparison.OrdinalIgnoreCase)))
                b.AppendLine("NATIVE " + module.ModuleName + " | " + module.FileVersionInfo.FileVersion + " | " + module.FileName);
    }
    private static void PatchesSection(StringBuilder b)
    {
        foreach (var method in Harmony.GetAllPatchedMethods().OrderBy(m => m.DeclaringType?.FullName).ThenBy(m => m.Name))
        {
            string type = method.DeclaringType?.FullName ?? "";
            if (!new[] { "Render", "Graphics", "Camera", "Shader", "Texture", "UIRoot", "Background", "MainMenu", "Material" }.Any(type.Contains)) continue;
            var patches = Harmony.GetPatchInfo(method); b.AppendLine(type + "." + method.Name);
            foreach (var group in new[] { ("prefix", patches.Prefixes), ("postfix", patches.Postfixes), ("transpiler", patches.Transpilers), ("finalizer", patches.Finalizers) })
                foreach (var p in group.Item2) b.AppendLine(" " + group.Item1 + " owner=" + p.owner + " priority=" + p.priority + " method=" + p.PatchMethod.DeclaringType?.FullName + "." + p.PatchMethod.Name + " before=" + string.Join(",", p.before) + " after=" + string.Join(",", p.after));
        }
    }
    private static void LogTail(StringBuilder b, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { b.AppendLine("Log absent: " + path); return; }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - 262144); stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            if (start > 0) reader.ReadLine();
            b.AppendLine("LOG TAIL " + path + " total bytes=" + stream.Length + " (up to last 256 KiB)"); b.AppendLine(reader.ReadToEnd());
        }
        catch (Exception e) { b.AppendLine("Log tail failed: " + Error(e)); }
    }
    private static void LogsSection(StringBuilder b)
    {
        LogMessage[] messages;
        using (Log.LockMessages()) messages = Log.Messages.ToArray();
        var errors = messages.Where(m => (Read(m, "type")?.ToString() ?? "").Contains("Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        var warnings = messages.Where(m => (Read(m, "type")?.ToString() ?? "").Contains("Warning", StringComparison.OrdinalIgnoreCase)).ToArray();
        b.AppendLine("In-memory messages=" + messages.Length + "; errors=" + errors.Length + "; warnings=" + warnings.Length);
        b.AppendLine("Includes first 200 and last 300 errors, last 150 warnings, last 300 messages; overlapping entries deduplicated. Full disk tails follow.");
        var selected = errors.Take(200).Concat(errors.TakeLast(300)).Concat(warnings.TakeLast(150)).Concat(messages.TakeLast(300)).ToHashSet();
        foreach (var message in messages.Where(selected.Contains)) { Dump(b, "log", message); }
        string console = Read(typeof(UnityEngine.Application), "consoleLogPath") as string;
        LogTail(b, console);
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludeon Studios", "RimWorld by Ludeon Studios");
        foreach (string root in new[] { local, GenFilePaths.SaveDataFolderPath })
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.GetFiles(root, "*.log", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(4))
                if (!string.Equals(file, console, StringComparison.OrdinalIgnoreCase)) LogTail(b, file);
        }
    }
}
