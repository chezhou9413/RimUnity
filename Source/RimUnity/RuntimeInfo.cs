using System.IO;
using System.Reflection;

namespace RimUnity;

//加载入口与设置页共享只读状态，不改变公共运行时程序集身份。
public static class RuntimeInfo
{
    public static string Version => typeof(RuntimeInfo).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
    public static bool IsStandalone { get; private set; } = true;
    public static string Provider { get; private set; } = "RimUnity";
    public static string ResourceRoot { get; private set; }
    public static int EmbeddedCopies { get; private set; }
    public static string PayloadHash { get; private set; } = "";

    //引导器在创建内置运行时 Mod 实例前调用；独立版只更新诊断状态。
    public static void Configure(string resourceRoot, bool standalone, string provider, int embeddedCopies, string payloadHash)
    {
        //ShaderSourceLoader 将相对路径视为内置 Shader 路径；内容目录必须固定为文件系统绝对路径。
        ResourceRoot = Path.GetFullPath(resourceRoot);
        IsStandalone = standalone;
        Provider = provider;
        EmbeddedCopies = embeddedCopies;
        PayloadHash = payloadHash;
    }

    //独立运行时直接从自己的内容目录定位许可文件。
    internal static string LicenseRoot => File.Exists(Path.Combine(ResourceRoot, "LICENSE")) ? ResourceRoot : Directory.GetParent(ResourceRoot).FullName;
}
