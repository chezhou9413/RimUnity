using System.IO;
using Verse;

namespace RimUnity.Assets;

//按模组与逻辑路径加载共享模板，通过 RimUnity.Object.Instantiate 创建独立实例。
public static class PrefabLoader
{
    //每个内容模组自行携带 1.7/Data/RimUnity。
    public static GameObject Load(ModContentPack mod, string path)
        => LoadDirectory(Path.Combine(mod.RootDir, "1.7", "Data", "RimUnity"), path);

    //支持工具与开发期明确指定资源目录；不会搜索其他模组兜底。
    public static GameObject LoadDirectory(string dataDirectory, string path)
        => AssetLoader.LoadDirectory<GameObject>(dataDirectory, path);
}
