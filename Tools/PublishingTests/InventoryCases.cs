using RimUnity.Editor;

internal static class InventoryCases
{
    internal static void Run()
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS inventory: " + label); }
        var profile = new RimUnityWorkflow.Profile
        {
            mappings = new[] { new RimUnityWorkflow.Mapping { bundle = "core", mod = "CoreMod" }, new RimUnityWorkflow.Mapping { bundle = "military", mod = "MilitaryMod" } },
            resourceOwners = new[] { new RimUnityWorkflow.ResourceOwner { path = "Assets/Shared.mat", bundle = "military" } }
        };
        var root = new RimUnityWorkflow.Entry { path = "Assets/Root.prefab", bundle = "core", mod = "CoreMod", status = "pending" };
        var assets = new[]
        {
            new ResourceInventory.Asset { path = root.path, taggedBundle = "old", type = "Prefab", bytes = 100 },
            new ResourceInventory.Asset { path = "Assets/Shared.mat", taggedBundle = "core", type = "Material", bytes = 20 },
            new ResourceInventory.Asset { path = "assets/SHARED.mat", type = "Material", bytes = 20 },
            new ResourceInventory.Asset { path = "Assets/Noise.png", type = "Texture2D", bytes = 200 },
            new ResourceInventory.Asset { path = "Assets/Unassigned.mat", type = "Material", bytes = 30 },
            new ResourceInventory.Asset { path = "Assets/AutoShared.png", type = "Texture2D", bytes = 45 }
        };
        var usage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [root.path] = new() { "core" }, ["Assets/Shared.mat"] = new() { "core", "military" },
            ["Assets/Noise.png"] = new() { "core" }, ["Assets/AutoShared.png"] = new() { "core", "military" }
        };
        var rows = ResourceInventory.Build(assets, new[] { root }, usage, profile);
        Check(rows.Length == 5, "shared paths appear once, ignoring path case");
        var shared = rows.Single(r => r.type == "Material" && r.Shared);
        Check(shared.bundle == "military" && shared.mod == "MilitaryMod", "explicit ownership overrides existing AB label");
        Check(shared.InBundle("core") && shared.InBundle("military"), "dependency remains visible in referencing and owning packages");
        Check(rows.Single(r => r.path == root.path).entry == root && rows.Single(r => r.path == root.path).bundle == "core", "manual/root assignment preserved");
        var automatic = rows.Single(r => r.path == "Assets/Noise.png");
        Check(automatic.bundle == null && automatic.mod == "CoreMod" && !automatic.Unassigned, "automatic dependency resolves a single output without relabeling");
        var autoShared = rows.Single(r => r.path == "Assets/AutoShared.png");
        Check(autoShared.Shared && autoShared.bundle == null && autoShared.mod == null, "cross-package automatic sharing is not misrepresented as one owner");
        Check(rows.Count(r => r.InBundle(ResourceInventory.UnassignedKey)) == 1, "unassigned excludes automatic dependencies");
        Check(rows.All(r => r.InBundle("")), "all-packages view contains every row");
        Check(shared.Matches("military") && shared.Matches("shared.MAT") && automatic.Matches("texture2d") && !automatic.Matches("no-such-token"), "search matches type, owner, and path");
        Check(rows.Sum(r => r.bytes) == 395, "source size counts shared paths once");
        var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "assets/shared.MAT" };
        var classified = ResourceInventory.Build(assets, new[] { root }, usage, profile, marked);
        Check(classified.Single(r => r.Shared && r.type == "Material").ui, "dependency UI flag is explicit and path-case independent");
        Check(classified.Count(r => r.MatchesUi(1)) == 1 && classified.Count(r => r.MatchesUi(2)) == 4 && classified.All(r => r.MatchesUi(0)), "UI and non-UI filters partition all resource rows");
        root.ui = true;
        Check(ResourceInventory.Build(assets, new[] { root }, usage, profile).Single(r => r.path == root.path).ui, "legacy entry UI flags remain readable");
        marked.Clear();
        Check(!ResourceInventory.Build(assets, new[] { root }, usage, profile, marked).Any(r => r.ui), "explicitly clearing flags overrides stale entry values");
        root.ui = false;

        var shader = new UnityEngine.Shader { Path = "Assets/Shared.shader" };
        var material = new UnityEngine.Material { Path = "Assets/World.mat", shader = shader };
        marked.Add("assets/SHARED.shader");
        Check(ExportUiPolicy.IsMarked(shader, marked) && ExportUiPolicy.IsMarked(material, marked),
            "shader-only UI flag propagates to referencing material before export identity is chosen");
        Check(ExportUiPolicy.IsMarked(new UnityEngine.Material { Path = "Assets/Other.mat", shader = shader }, marked),
            "shared marked shader applies to every referencing material");
        Check(!ExportUiPolicy.IsMarked(new UnityEngine.Material { Path = "Assets/Plain.mat" }, marked)
            && !ExportUiPolicy.IsMarked(new UnityEngine.Object { Path = shader.Path }, marked),
            "missing shaders and non-rendering assets do not acquire UI export mode");
        marked.Clear();
        Check(!ExportUiPolicy.IsMarked(shader, marked) && !ExportUiPolicy.IsMarked(material, marked),
            "clearing shader UI flag restores world export mode");
        marked.Add(material.Path);
        Check(ExportUiPolicy.IsMarked(material, marked) && !ExportUiPolicy.IsMarked(shader, marked),
            "material-only UI flag does not change its shared shader classification");
        profile.mappings = profile.mappings.Concat(new[] { new RimUnityWorkflow.Mapping { bundle = "military", mod = "Conflict" } }).ToArray();
        var conflicted = ResourceInventory.Build(assets, new[] { root }, usage, profile).Single(r => r.path == shared.path);
        Check(conflicted.mod == null, "duplicate output mappings do not silently choose an owner");
        foreach (var size in new[] { (760f, 600f), (769f, 665f), (1100f, 650f), (1600f, 1000f) })
            foreach (bool error in new[] { false, true })
            {
                var layout = InventoryGeometry.Calculate(size.Item1, size.Item2, error);
                var repaint = InventoryGeometry.Calculate(size.Item1, size.Item2, error);
                Check(layout.Table.height >= 130 && layout.BundleList.height > 100, "visible list bounds at " + size + ", error=" + error);
                Check(layout.Search.height == (layout.Main.width < 760 ? 70 : 32) && layout.Main.x > layout.Sidebar.Right, "full-size UI filters wrap in narrow windows at " + size);
                Check(layout.Search.Bottom + 8 <= layout.Table.y && layout.Search.width >= 440, "filters fit without overlapping table at " + size);
                Check(layout.Table.Bottom < layout.Selection.y && layout.Selection.Bottom < layout.Footer.y, "table, inspector and footer do not overlap at " + size);
                Check(layout.Table.height == repaint.Table.height && layout.Table.y == repaint.Table.y, "layout remains identical across GUI passes at " + size);
            }
    }
}
