using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Verse;

namespace RimUnity.Embedded;

//延迟脚本保留原程序集身份，并始终登记到自己的内容模组。
internal sealed class DeferredContent
{
    internal readonly Dictionary<string, (string Path, ModContentPack Owner)> Files = new(StringComparer.OrdinalIgnoreCase);
    internal Version Minimum = new(0, 1, 9);

    //扫描启用的加载目录，拒绝未声明版本或重复身份的内容程序集。
    internal DeferredContent(IEnumerable<ModContentPack> mods)
    {
        foreach (var mod in mods)
        foreach (string folder in mod.foldersToLoadDescendingOrder)
        {
            string directory = Path.Combine(folder, "Assemblies");
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.GetFiles(directory, "*.rimunity", SearchOption.AllDirectories))
            {
                string name = AssemblyName.GetAssemblyName(path).Name;
                if (Files.TryGetValue(name, out var previous))
                {
                    if (previous.Path == path && previous.Owner == mod) continue;
                    throw new InvalidDataException("内容程序集身份重复 / Duplicate content assembly: " + name);
                }
                using var json = JsonDocument.Parse(File.ReadAllText(path + ".json"));
                if (json.RootElement.GetProperty("protocol").GetInt32() != 1)
                    throw new InvalidDataException("Unsupported RimUnity content protocol: " + path);
                var version = Version.Parse(json.RootElement.GetProperty("minimumVersion").GetString());
                if (version > Minimum) Minimum = version;
                Files.Add(name, (path, mod));
            }
        }
    }

    //先安装解析器再逐个登记，支持跨模组脚本继承及交叉程序集引用。
    internal void Load()
    {
        foreach (string name in Files.Keys)
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name))
                throw new InvalidOperationException("内容 DLL 被提前加载，请移除 Assemblies 中的普通 DLL / Content loaded too early: " + name);
        AssemblyLoadContext.Default.Resolving += Resolve;
        foreach (var file in Files.Values)
        {
            var assembly = Assembly.LoadFrom(file.Path);
            if (!file.Owner.assemblies.loadedAssemblies.Contains(assembly)) file.Owner.assemblies.loadedAssemblies.Add(assembly);
        }
        foreach (var file in Files.Values) Assembly.LoadFrom(file.Path).GetTypes();
    }

    //解析器保留到进程退出，以支持脚本的延迟 JIT 引用。
    private Assembly Resolve(AssemblyLoadContext context, AssemblyName name) => Files.TryGetValue(name.Name, out var file)
        ? Assembly.LoadFrom(file.Path) : null;
}
