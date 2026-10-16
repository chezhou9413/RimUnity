using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//仅在载入时反射；帧更新不经过序列化绑定。
internal sealed class ValueReader
{
    private static readonly Dictionary<(Type, string, bool), MemberInfo> bindings = new();
    private readonly AssetCatalog catalog;
    private readonly IReadOnlyDictionary<string, RimUnity.Object> objects;
    internal ValueReader(AssetCatalog catalog, IReadOnlyDictionary<string, RimUnity.Object> objects)
    { this.catalog = catalog; this.objects = objects; }

    //按目标字段真实类型恢复数值，整数使用文本保存以避免精度丢失。
    internal object Read(ValueData data, Type type)
    {
        switch (data.kind)
        {
            case "null": return null;
            case "object": return Checked(objects[data.text], type);
            case "asset": return Checked(catalog.Get(data.text), type);
            case "string": return data.text;
            case "integer": return type.IsEnum ? Enum.ToObject(type, Convert.ChangeType(data.text, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)) : Convert.ChangeType(data.text, type, CultureInfo.InvariantCulture);
            case "number": return Convert.ChangeType(data.number, type, CultureInfo.InvariantCulture);
            case "specialNumber": return Convert.ChangeType(double.Parse(data.text, CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
            case "bool": return data.number != 0;
            case "vector": return Vector(data.vector, type);
            case "array":
                Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                if (type.IsArray)
                {
                    Array result = Array.CreateInstance(element, data.items.Count);
                    for (int i = 0; i < result.Length; i++) result.SetValue(Read(data.items[i], element), i);
                    return result;
                }
                IList list = (IList)Activator.CreateInstance(type);
                foreach (ValueData item in data.items) list.Add(Read(item, element));
                return list;
            case "data":
                object value = Activator.CreateInstance(type);
                Apply(value, data.fields);
                return value;
            default: throw new NotSupportedException($"不能把 {data.kind} 解码为 {type.FullName}");
        }
    }

    //引用类型不匹配时直接指出需要调整的脚本引用。
    private static object Checked(RimUnity.Object value, Type type)
        => type.IsInstanceOfType(value) ? value : throw new InvalidCastException($"导出引用 {value.GetType().FullName} 不能赋给 {type.FullName}；请使用 RimUnity 对应类型。");

    //常用 Unity 值类型保留原有 API 和内存布局。
    private static object Vector(float[] v, Type t)
    {
        if (t == typeof(Vector2)) return new Vector2(v[0], v[1]);
        if (t == typeof(Vector3)) return new Vector3(v[0], v[1], v[2]);
        if (t == typeof(Vector4)) return new Vector4(v[0], v[1], v[2], v[3]);
        if (t == typeof(Quaternion)) return new Quaternion(v[0], v[1], v[2], v[3]);
        if (t == typeof(Color)) return new Color(v[0], v[1], v[2], v[3]);
        if (t == typeof(Color32)) return new Color32((byte)v[0], (byte)v[1], (byte)v[2], (byte)v[3]);
        if (t == typeof(Rect)) return new Rect(v[0], v[1], v[2], v[3]);
        if (t == typeof(Vector2Int)) return new Vector2Int((int)v[0], (int)v[1]);
        if (t == typeof(Vector3Int)) return new Vector3Int((int)v[0], (int)v[1], (int)v[2]);
        if (t == typeof(Matrix4x4)) { Matrix4x4 m = new(); for (int i = 0; i < 16; i++) m[i] = v[i]; return m; }
        if (t == typeof(Bounds)) return new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
        throw new NotSupportedException($"未实现的数学类型：{t.FullName}");
    }

    //可序列化私有字段包括基类字段；属性仅用于显式内置组件适配。
    internal void Apply(object target, IEnumerable<MemberData> members)
    {
        foreach (MemberData member in members)
        {
            try
            {
                MemberInfo binding = ResolveMember(target.GetType(), member);
                if (binding is PropertyInfo property)
                {
                    if (member.value.kind == "event") EventBinder.Bind(property.GetValue(target), member.value.calls, this, objects);
                    else property.SetValue(target, Read(member.value, property.PropertyType));
                }
                else
                {
                    var field = (FieldInfo)binding;
                    if (member.value.kind == "event")
                    {
                        object ev = Activator.CreateInstance(field.FieldType);
                        EventBinder.Bind(ev, member.value.calls, this, objects);
                        field.SetValue(target, ev);
                    }
                    else field.SetValue(target, Read(member.value, field.FieldType));
                }
            }
            catch (Exception ex) { throw new InvalidOperationException($"还原 {target.GetType().FullName}.{member.name} 失败", ex); }
        }
    }

    //实例化只缓存类型元数据，不持有模板、组件或存档实例。
    private static MemberInfo ResolveMember(Type type, MemberData member)
    {
        var key = (type, member.name, member.property);
        if (bindings.TryGetValue(key, out MemberInfo binding)) return binding;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        //派生类可以隐藏基类成员，仍按最具体声明优先查找。
        if (member.property)
            for (Type current = type; current != null && binding == null; current = current.BaseType)
                binding = current.GetProperty(member.name, flags);
        for (Type current = type; current != null && binding == null; current = current.BaseType)
            binding = current.GetField(member.name, flags);
        if (binding == null) throw new MissingFieldException(type.FullName, member.name);
        bindings.Add(key, binding);
        return binding;
    }
}
