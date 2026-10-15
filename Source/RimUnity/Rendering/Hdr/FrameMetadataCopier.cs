using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace RimUnity.Rendering;

//Bind the running host's value metadata once. No compile-time references to optional frame accessors.
internal static class FrameMetadataCopier
{
    internal static Action<T, T> Build<T>(out string layout) where T : class
    {
        var source = Expression.Parameter(typeof(T), "source");
        var target = Expression.Parameter(typeof(T), "target");
        var copies = new List<Expression>();
        var members = new List<string>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var property in typeof(T).GetProperties(flags).OrderBy(p => p.Name))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true
                || property.SetMethod?.IsPublic != true || !ValueMetadata(property.PropertyType)) continue;
            copies.Add(Expression.Assign(Expression.Property(target, property), Expression.Property(source, property)));
            members.Add("property " + property.Name + ":" + property.PropertyType.Name);
        }
        foreach (var field in typeof(T).GetFields(flags).OrderBy(f => f.Name))
        {
            //Readonly data is supplied by the host constructor; never mutate resource ownership or private state.
            if (field.IsInitOnly || field.IsLiteral || !ValueMetadata(field.FieldType)) continue;
            copies.Add(Expression.Assign(Expression.Field(target, field), Expression.Field(source, field)));
            members.Add("field " + field.Name + ":" + field.FieldType.Name);
        }
        layout = typeof(T).Assembly.ManifestModule.ModuleVersionId + " | " + string.Join(", ", members);
        copies.Add(Expression.Empty());
        return Expression.Lambda<Action<T, T>>(Expression.Block(copies), source, target).Compile();
    }

    private static bool ValueMetadata(Type type) => type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)
        || type.IsEnum || type == typeof(decimal)
        || type.IsValueType && (type.Namespace == "System.Numerics" || type.Namespace == "Brutal.Numerics");
}
