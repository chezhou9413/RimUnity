using System;
using System.Collections.Generic;
using System.Linq;

namespace RimUnity.Diagnostics;

// Only immutable value snapshots leave the capture method. No retained Things, textures or GPU objects.
internal static class MemoryAttribution
{
    internal const string Unknown = "未归属";
    internal const string Shared = "共享 / 动态资源（未归责）";
    internal const string Builtin = "原版 / 宿主内置（未细分 DLC）";
    internal sealed record Source(string Id, string Name, string Root);
    internal sealed record Row(string Owner, string Name, string Category, long Bytes,
        string Placement, string Evidence, string Detail, string Handle);
    internal sealed record Heap(int Index, string Flags, ulong Size, ulong Budget, ulong Usage, ulong Allocated, ulong Reserved);
    internal sealed record Group(string Owner, long Bytes, long Device, long Host, long UnknownPlacement, int Count);
    internal sealed record Snapshot(DateTime Utc, double Milliseconds, Row[] Rows, Heap[] Heaps, string[] Notes)
    {
        internal long Total => Rows.Sum(x => x.Bytes);
        internal Group[] Groups => Rows.GroupBy(x => x.Owner).Select(g => new Group(g.Key, g.Sum(x => x.Bytes),
            g.Where(x => x.Placement == "设备本地").Sum(x => x.Bytes),
            g.Where(x => x.Placement == "系统内存").Sum(x => x.Bytes),
            g.Where(x => x.Placement == "未知").Sum(x => x.Bytes), g.Count()))
            .OrderByDescending(x => x.Bytes).ToArray();
    }

    internal static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');

    // Match complete path boundaries only. Ambiguous roots and names stay unknown, never fuzzy-match filenames.
    internal static (string Owner, string Evidence) Resolve(string path, string category, Source[] sources)
    {
        string name = Normalize(path);
        if (name.Split('/').Any(x => x == ".." || x == ".")) return (Unknown, "路径包含相对跳转，拒绝猜测");
        var absolute = sources.Where(x => name.StartsWith(Normalize(x.Root) + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (absolute.Length > 0)
        {
            int longest = absolute.Max(x => Normalize(x.Root).Length);
            var best = absolute.Where(x => Normalize(x.Root).Length == longest).ToArray();
            if (best.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                return (best[0].Name + " [" + best[0].Id + "]", "实际文件路径属于启用模组目录");
            return (Unknown, "多个模组使用同一目录");
        }
        if (name.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase)) return (Builtin, "宿主 Resources 路径；不能区分具体 DLC");
        // Waylith file-backed texture labels include the content root directory name.
        var relative = sources.Where(x => name.StartsWith(Normalize(x.Root).Split('/').Last() + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (relative.Length == 1) return (relative[0].Name + " [" + relative[0].Id + "]", "分配标签中的内容根目录前缀");
        if (relative.Length > 1) return (Unknown, "内容根目录名重复，来源不唯一");
        if (category is "Atlas" or "RenderTarget" or "DynamicDraw" or "UniformBuffer" or "ComputeBuffer" or "Staging" or "MeshVertex" or "MeshIndex" or "Font" or "UITexture")
            return (Shared, "共享或动态生成的分配；不推断触发它的模组");
        return (Unknown, "无可验证的来源路径");
    }

    internal static string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("F2") + " GiB"
        : (bytes / (1024d * 1024)).ToString("F2") + " MiB";
}
