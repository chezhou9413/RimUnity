using Verse;

//模拟安装部分补丁后构造失败的普通模组，观察加载器是否重复执行构造。
public sealed class FailingMod : Mod
{
    internal const string Failure = "RimUnity fixture: unrelated Mod construction failed";
    internal static int Attempts;

    //计数先于异常，重复执行会保留与真实失败构造相同的副作用。
    public FailingMod(ModContentPack content) : base(content)
    {
        Attempts++;
        throw new InvalidOperationException(Failure);
    }
}
