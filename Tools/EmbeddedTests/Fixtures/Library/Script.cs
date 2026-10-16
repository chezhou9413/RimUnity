namespace Fixture;
//真实 Core 类型的派生脚本，用于检测提前解析与程序集重复加载。
public class BaseScript : RimUnity.MonoBehaviour { }
//构造时检查运行时选择已完成。
public sealed class LibraryMod : Verse.Mod
{
    public LibraryMod(Verse.ModContentPack content) : base(content)
    {
        if (RimUnity.RuntimeInfo.ResourceRoot == null) throw new System.InvalidOperationException("Runtime not selected before content Mod");
    }
}
