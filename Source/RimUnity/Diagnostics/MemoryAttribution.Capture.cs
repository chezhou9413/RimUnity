using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Vma;
using Verse;
using Waylith;
using Waylith.Graphics;
using Waylith.Rendering;

namespace RimUnity.Diagnostics;

internal static partial class MemoryAttributionCapture
{
    private const BindingFlags Fields = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Read(object value, string name) => value is Type t
        ? t.GetField(name, Fields)?.GetValue(null)
        : value?.GetType().GetField(name, Fields)?.GetValue(value);

    // Explicit main-thread snapshot, never called from Tick/Draw. No upload, GC, eviction or device-idle.
    internal static MemoryAttribution.Snapshot Capture()
    {
        var watch = Stopwatch.StartNew();
        var utc = DateTime.UtcNow;
        var notes = new List<string>();
        var sources = LoadedModManager.RunningModsListForReading.Select(m =>
            new MemoryAttribution.Source(m.PackageIdPlayerFacing, m.Name, m.RootDir)).ToArray();
        var placements = new Dictionary<GpuMemoryTracker.AllocKey, string>();
        var allocations = new List<GpuMemoryTracker.TrackedAllocation>();
        // The host protects this registry with System.Threading.Lock, not Monitor. Use the SAME lock.
        var gate = Read(typeof(TextureResourceManager), "Gate") as Lock;
        var live = Read(typeof(TextureResourceManager), "LiveAllocations") as List<System.WeakReference<GpuTextureAllocation>>;
        if (gate != null && live != null)
        {
            using (gate.EnterScope())
            {
                foreach (var weak in live)
                    if (weak.TryGetTarget(out var gpu) && gpu.Image.VkHandle != 0)
                        placements[new(gpu.Image.VkHandle, true)] = gpu.IsHostResident ? "系统内存" : "设备本地";
                GpuMemoryTracker.CollectAll(allocations);
            }
        }
        else
        {
            GpuMemoryTracker.CollectAll(allocations);
            notes.Add("宿主纹理分配注册表不可用：内存位置显示未知，未按资源尺寸估算。");
        }
        var hints = CatalogHints(sources, notes);
        var rows = new List<MemoryAttribution.Row>(allocations.Count);
        var seen = new HashSet<GpuMemoryTracker.AllocKey>();
        foreach (var item in allocations)
        {
            if (!seen.Add(item.Id)) throw new InvalidOperationException("分配快照出现重复句柄，已停止归责以免重复计算。");
            string category = item.Category.ToString();
            var owner = MemoryAttribution.Resolve(item.Name, category, sources);
            if (hints.TryGetValue(item.Id, out var hint))
                owner = hint;
            rows.Add(new(owner.Owner, item.Name ?? "（无名称）", category, item.Bytes,
                placements.GetValueOrDefault(item.Id, "未知"), owner.Evidence, item.Detail ?? "",
                (item.Id.IsImage ? "image:" : "buffer:") + item.Id.Handle.ToString("X")));
        }
        var heaps = new List<MemoryAttribution.Heap>();
        var device = EngineApplication.Instance?.GraphicsDevice;
        if (device != null)
        {
            var memory = device.PhysicalDevice.GetMemoryProperties();
            Span<VmaBudget> budgets = stackalloc VmaBudget[16];
            device.Allocator.GetHeapBudgets(budgets);
            for (int i = 0; i < Math.Min(16, (int)memory.MemoryHeapCount); i++)
            {
                var heap = memory.MemoryHeaps[i]; var budget = budgets[i];
                heaps.Add(new(i, heap.Flags.ToString(), heap.Size.Value, (ulong)budget.Budget,
                    (ulong)budget.Usage, (ulong)budget.Statistics.AllocationBytes, (ulong)budget.Statistics.BlockBytes));
            }
        }
        notes.Add("来源表示资源提供方，不表示是谁触发加载；共享图集不拆分给模组。");
        notes.Add("设备本地/系统内存表示 Vulkan 分配的内存类型，不保证驱动当下物理驻留；缓冲区等无法确认的位置保留未知。");
        notes.Add("资源快照与堆预算依次读取，不强制 GPU 空闲；驱动预算、VMA 块预留与资源分配是不同口径，不要求相等。");
        watch.Stop();
        return new(utc, watch.Elapsed.TotalMilliseconds, rows.ToArray(), heaps.ToArray(), notes.ToArray());
    }

    private static Dictionary<GpuMemoryTracker.AllocKey, (string Owner, string Evidence)> CatalogHints(
        MemoryAttribution.Source[] sources, List<string> notes)
    {
        var hints = new Dictionary<GpuMemoryTracker.AllocKey, (string Owner, string Evidence)>();
        // Native registry includes vanilla, ContentFinder and bridge textures, not just cached prefabs.
        var residencyGate = Read(typeof(TextureResidency), "Gate") as Lock;
        var registry = Read(typeof(TextureResidency), "Registry") as List<System.WeakReference<Waylith.Rendering.Texture>>;
        if (residencyGate != null && registry != null)
        {
            using (residencyGate.EnterScope())
                foreach (var weak in registry)
                    if (weak.TryGetTarget(out var texture) && texture is Waylith.Rendering.Texture2D file
                        && file.GpuResource is { } gpu && gpu.Image.VkHandle != 0 && !string.IsNullOrEmpty(file.SourcePath))
                    {
                        var owner = MemoryAttribution.Resolve(file.SourcePath, "GameTexture", sources);
                        if (owner.Owner != MemoryAttribution.Unknown)
                            hints[new(gpu.Image.VkHandle, true)] = owner;
                    }
        }
        else notes.Add("宿主纹理来源注册表不可用：仅使用分配标签和已缓存资源的路径证据。");
        if (Read(typeof(Assets.AssetLoader), "Catalogs") is not IDictionary catalogs) return hints;
        int unloaded = 0;
        foreach (DictionaryEntry catalog in catalogs)
        {
            if (Read(catalog.Value, "resources") is not IDictionary resources) continue;
            foreach (DictionaryEntry resource in resources)
            {
                if (resource.Value is not RimUnity.Texture texture) continue;
                var native = texture.NativeTexture;
                var gpu = native?.GpuResource;
                if (gpu == null) { unloaded++; continue; }
                // Only a physical source path proves ownership. A requesting catalog may reference another mod.
                string path = (native as Waylith.Rendering.Texture2D)?.SourcePath;
                if (string.IsNullOrEmpty(path)) continue;
                var owner = MemoryAttribution.Resolve(path, "GameTexture", sources);
                if (owner.Owner == MemoryAttribution.Unknown) continue;
                var key = new GpuMemoryTracker.AllocKey(gpu.Image.VkHandle, true);
                if (hints.TryGetValue(key, out var old) && old.Owner != owner.Owner)
                    hints[key] = (MemoryAttribution.Unknown, "同一句柄存在冲突来源");
                else hints[key] = owner;
            }
        }
        notes.Add("已缓存但没有 GPU 分配的 RimUnity 纹理引用：" + unloaded + "；不计入分配字节（不是全游戏未上传纹理总数）。");
        return hints;
    }
}
