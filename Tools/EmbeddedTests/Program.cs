using System.Reflection;
using System.Runtime.Loader;

//只加载托管程序集；图形、原生挂载与运行时初始化由离线边界钩子代替。
AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
{
    string name = new AssemblyName(request.Name).Name;
    string path = name == "0Harmony" ? args[2] : Path.Combine(args[1], name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
Harness.Run(args);
