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
        profile.mappings = profile.mappings.Concat(new[] { new RimUnityWorkflow.Mapping { bundle = "military", mod = "Conflict" } }).ToArray();
        var conflicted = ResourceInventory.Build(assets, new[] { root }, usage, profile).Single(r => r.path == shared.path);
        Check(conflicted.mod == null, "duplicate output mappings do not silently choose an owner");
        foreach (var size in new[] { (760f, 600f), (769f, 665f), (1100f, 650f), (1600f, 1000f) })
            foreach (bool error in new[] { false, true })
            {
                var layout = InventoryGeometry.Calculate(size.Item1, size.Item2, error);
                var repaint = InventoryGeometry.Calculate(size.Item1, size.Item2, error);
                Check(layout.Table.height >= 130 && layout.BundleList.height > 100, "visible list bounds at " + size + ", error=" + error);
                Check(layout.Search.height == 32 && layout.Main.x > layout.Sidebar.Right, "full-size controls and separated columns at " + size);
                Check(layout.Table.Bottom < layout.Selection.y && layout.Selection.Bottom < layout.Footer.y, "table, inspector and footer do not overlap at " + size);
                Check(layout.Table.height == repaint.Table.height && layout.Table.y == repaint.Table.y, "layout remains identical across GUI passes at " + size);
            }
    }
}
