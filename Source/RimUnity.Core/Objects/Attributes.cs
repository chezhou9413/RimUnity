using System;

namespace RimUnity;

//运行时字段标记；导出器按受支持的序列化字段恢复引用。
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeField : Attribute { }

//声明组件依赖，依赖实例与目标实例在同一事务中激活。
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireComponent : Attribute
{
    internal Type[] Types { get; }
    public RequireComponent(params Type[] types) => Types = types;
}

//禁止同一对象重复挂接组件。
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DisallowMultipleComponent : Attribute { }
