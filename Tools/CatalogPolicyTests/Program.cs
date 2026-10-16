using RimUnity.Assets;
using Verse;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
var mod = new ModContentPack();
ModLister.Metadata.Folders = new() { new() { ShouldLoad = false }, new() { ShouldLoad = false } };
Check(CatalogLoadPolicy.IsConditionallyDisabled(mod), "all conditions excluded is intentionally disabled");
try { CatalogLoadPolicy.RequireRoot(mod); throw new Exception("disabled load accepted"); } catch (InvalidOperationException) { }
Check(true, "disabled explicit resource load rejected");
ModLister.Metadata.Folders[0].ShouldLoad = true;
Check(!CatalogLoadPolicy.IsConditionallyDisabled(mod), "missing selected content is not suppressed");
ModLister.Metadata.Folders = new();
Check(!CatalogLoadPolicy.IsConditionallyDisabled(mod), "empty configuration is not suppressed");
ModLister.Metadata.Folders = null;
Check(!CatalogLoadPolicy.IsConditionallyDisabled(mod), "missing version configuration is not suppressed");
mod.foldersToLoadDescendingOrder.Add("core/1.7/Core");
Check(CatalogLoadPolicy.HasLoadFolders(mod) && CatalogLoadPolicy.RequireRoot(mod) == Path.Combine("core", "1.7", "Data", "RimUnity"), "core split folders retain fixed catalog root");
ModLister.Metadata.Folders = new() { new() { folderName = "1.7", ShouldLoad = false }, new() { folderName = "MergedContentPlaceholder", ShouldLoad = true } };
mod.foldersToLoadDescendingOrder.Clear(); mod.foldersToLoadDescendingOrder.Add("core/MergedContentPlaceholder");
Check(!CatalogLoadPolicy.HasLoadFolders(mod), "translation placeholder does not activate excluded version catalog");
ModLister.Metadata.Folders.Add(new() { folderName = "1.7", ShouldLoad = true });
Check(CatalogLoadPolicy.HasLoadFolders(mod), "an enabled duplicate version entry activates catalog");
namespace RimWorld { public static class VersionControl { public static string CurrentVersionString => "1.7.1234"; public static string CurrentVersionStringWithoutBuild => "1.7"; } }
namespace Verse {
 public class ModContentPack { public List<string> foldersToLoadDescendingOrder = new(); public string PackageId => "test"; public string PackageIdPlayerFacing => "test"; public string RootDir => "core"; }
 public class LoadFolder { public bool ShouldLoad; public string folderName; }
 public class ModMetaData { public List<LoadFolder> Folders; public List<LoadFolder> LoadFoldersForVersion(string version) => version == "1.7" ? Folders : null; }
 public static class ModLister { public static ModMetaData Metadata = new(); public static ModMetaData GetModWithIdentifier(string id) => Metadata; }
}
