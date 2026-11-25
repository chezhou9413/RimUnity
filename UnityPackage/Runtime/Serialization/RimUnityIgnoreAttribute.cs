using System;

namespace RimUnity
{
    //标记仅在 Unity 编辑器中使用的序列化字段，RimUnity 导出时跳过该字段及其引用的资源。
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class RimUnityIgnoreAttribute : Attribute
    {
    }
}
