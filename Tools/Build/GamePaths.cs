using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace RimUnity.Build;

//只在 Steam 已登记的库中发现引用，不扫描整个磁盘或下载游戏文件。
internal sealed record GamePaths(string Game, string Harmony)
{
    //显式设置无效时直接报错；自动发现多个游戏安装时要求选择。
    internal static GamePaths Resolve(string root, Dictionary<string, string> options)
    {
        var local = new Dictionary<string, string>();
        string config = Path.Combine(root, "build.local.json");
        if (File.Exists(config))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is not ("game" or "harmony")) throw new ArgumentException("Unknown build.local.json key: " + property.Name);
                string? value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value)) local.Add(property.Name, Path.GetFullPath(value, root));
            }
        }
        string? Setting(string key, string environment) => options.GetValueOrDefault("--" + key)
            ?? local.GetValueOrDefault(key) ?? Environment.GetEnvironmentVariable(environment);
        var libraries = SteamLibraries().ToArray();
        string? requestedGame = Setting("game", "RIMWORLD_DIR");
        string game;
        if (!string.IsNullOrWhiteSpace(requestedGame))
            game = FindAssemblies(Path.GetFullPath(requestedGame))
                ?? throw new DirectoryNotFoundException("Game path must contain RimWorld 1.7 Assembly-CSharp.dll, Waylith.dll and UnityStubs.dll: " + requestedGame);
        else
        {
            var games = libraries.Select(library => FindAssemblies(Path.Combine(library, "steamapps/common/RimWorld")))
                .Where(path => path != null).Cast<string>().Distinct(PathComparer).ToArray();
            game = games.Length == 1 ? games[0] : throw new DirectoryNotFoundException(games.Length == 0
                ? "RimWorld 1.7 was not found. Use --game <assembly directory> or set game in build.local.json."
                : "Multiple RimWorld installations found; choose --game:\n" + string.Join("\n", games));
        }
        string? requestedHarmony = Setting("harmony", "HARMONY_PATH");
        string harmony;
        if (!string.IsNullOrWhiteSpace(requestedHarmony)) harmony = Path.GetFullPath(requestedHarmony);
        else
        {
            var candidates = new List<string>();
            for (var parent = new DirectoryInfo(game); parent != null; parent = parent.Parent)
            {
                string mods = Path.Combine(parent.FullName, "Mods");
                if (Directory.Exists(mods))
                    foreach (string mod in Directory.EnumerateDirectories(mods))
                    {
                        string resolvedMod = new DirectoryInfo(mod).ResolveLinkTarget(true)?.FullName ?? mod;
                        string dll = Path.Combine(resolvedMod, "1.7/Assemblies/0Harmony.dll"), about = Path.Combine(resolvedMod, "About/About.xml");
                        if (File.Exists(dll) && File.Exists(about)
                            && string.Equals(XDocument.Load(about).Root?.Element("packageId")?.Value, "brrainz.harmony", StringComparison.OrdinalIgnoreCase))
                            candidates.Add(dll);
                    }
                if (parent.Name == "steamapps")
                {
                    candidates.Add(Path.Combine(parent.FullName, "workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll"));
                    break;
                }
            }
            candidates.AddRange(libraries.Select(library => Path.Combine(library, "steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll")));
            var found = candidates.Where(File.Exists).Select(Path.GetFullPath).Distinct(PathComparer).ToArray();
            harmony = found.Length == 1 ? found[0] : throw new FileNotFoundException(found.Length == 0
                ? "Harmony 1.7 was not found. Subscribe to Harmony or use --harmony <0Harmony.dll>."
                : "Multiple Harmony copies found; choose --harmony:\n" + string.Join("\n", found));
        }
        if (!File.Exists(harmony)) throw new FileNotFoundException("Harmony DLL does not exist: " + harmony);
        return new GamePaths(game, harmony);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    //根目录与 macOS 应用包内的程序集目录都以真实 DLL 是否齐全为准。
    private static string? FindAssemblies(string root)
    {
        if (!Directory.Exists(root)) return null;
        var candidates = new List<string> { root };
        var apps = root.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? new[] { root } : Directory.GetDirectories(root, "*.app");
        foreach (string app in apps)
        {
            candidates.Add(Path.Combine(app, "Contents/MacOS"));
            candidates.Add(Path.Combine(app, "Contents/Resources"));
        }
        return candidates.FirstOrDefault(path => new[] { "Assembly-CSharp.dll", "Waylith.dll", "UnityStubs.dll", "RimWorld.dll" }
            .All(file => File.Exists(Path.Combine(path, file))));
    }

    //读取当前用户 Steam 位置与 libraryfolders.vdf，支持额外库和 Linux Flatpak 安装。
    private static IEnumerable<string> SteamLibraries()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam) roots.Add(steam);
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        }
        else if (OperatingSystem.IsMacOS()) roots.Add(Path.Combine(profile, "Library/Application Support/Steam"));
        else
        {
            roots.Add(Path.Combine(profile, ".local/share/Steam"));
            roots.Add(Path.Combine(profile, ".steam/steam"));
            roots.Add(Path.Combine(profile, ".var/app/com.valvesoftware.Steam/.local/share/Steam"));
        }
        var libraries = new HashSet<string>(PathComparer);
        foreach (string root in roots.Where(Directory.Exists))
        {
            string canonical = new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(root);
            libraries.Add(canonical);
            string vdf = Path.Combine(root, "steamapps/libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\").Replace("\\\"", "\"");
                if (Directory.Exists(path)) libraries.Add(new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(path));
            }
        }
        return libraries;
    }
}
