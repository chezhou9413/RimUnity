using System;
using System.Collections.Generic;
using System.Linq;

namespace RimUnity.Editor
{
    //A window-owned snapshot. No AssetDatabase queries are performed while filtering/drawing.
    internal static class ResourceInventory
    {
        internal sealed class Asset
        {
            public string path, type, taggedBundle;
            public long bytes;
        }
        internal sealed class Row
        {
            public string path, name, type, bundle, mod;
            public long bytes;
            public bool ui;
            public string changeReason;
            public bool Changed => !string.IsNullOrEmpty(changeReason);
            public string[] references;
            public RimUnityWorkflow.Entry entry;
            public bool MatchesUi(int filter) => filter == 0 || (filter == 1 ? ui : !ui);
            public bool Shared => references.Length > 1;
            public bool Unassigned => string.IsNullOrEmpty(bundle) && references.Length == 0;
            public bool InBundle(string selected) => string.IsNullOrEmpty(selected)
                || (selected == UnassignedKey ? Unassigned : bundle == selected || references.Contains(selected));
            public bool Matches(string text) => string.IsNullOrWhiteSpace(text)
                || (path + " " + type + " " + bundle + " " + mod + " " + string.Join(" ", references))
                    .IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal const string UnassignedKey = "\u0001unassigned";

        internal static Row[] Build(IEnumerable<Asset> assets, IEnumerable<RimUnityWorkflow.Entry> entries,
            IDictionary<string, HashSet<string>> usage, RimUnityWorkflow.Profile profile, ISet<string> uiPaths = null)
        {
            var roots = entries.GroupBy(e => e.path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var pins = (profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>())
                .GroupBy(p => p.path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().bundle, StringComparer.OrdinalIgnoreCase);
            var mappings = profile.mappings.GroupBy(m => m.bundle ?? "").Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().mod);
            return assets.GroupBy(a => a.path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Select(asset =>
            {
                roots.TryGetValue(asset.path, out var entry);
                string owner = pins.TryGetValue(asset.path, out string pin) ? pin : entry?.bundle ?? asset.taggedBundle;
                var references = usage.TryGetValue(asset.path, out var bundles) ? bundles.OrderBy(b => b, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
                string effective = !string.IsNullOrEmpty(owner) ? owner : references.Length == 1 ? references[0] : null;
                string mod = effective != null && mappings.TryGetValue(effective, out var output) ? output : null;
                return new Row { path = asset.path, name = System.IO.Path.GetFileNameWithoutExtension(asset.path), type = asset.type ?? "Unknown",
                    bytes = asset.bytes, bundle = owner, mod = mod, references = references, entry = entry, ui = uiPaths?.Contains(asset.path) ?? entry?.ui == true };
            }).OrderBy(r => r.name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.path, StringComparer.Ordinal).ToArray();
        }
    }
}
