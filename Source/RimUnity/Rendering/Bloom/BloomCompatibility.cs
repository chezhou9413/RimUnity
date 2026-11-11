using System;

namespace RimUnity.Rendering;

//区分永久接口漂移与单个后处理实例的暂时构造失败。
internal static class BloomCompatibility
{
    internal static bool IsPermanentInterfaceFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is MissingMethodException or TypeLoadException or MissingFieldException)
                return true;
        return false;
    }
}
