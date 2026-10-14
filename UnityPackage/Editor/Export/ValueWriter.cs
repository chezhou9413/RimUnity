using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimUnity.Serialization;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    //只读取 Unity 实际序列化的字段；属性由内置适配器显式选取。
    internal sealed class ValueWriter
    {
        private readonly Dictionary<Object, string> objects;
        private readonly ResourceWriter resources;
        private readonly HashSet<object> visiting = new HashSet<object>(ReferenceComparer.Instance);
        internal bool CameraUi => resources.Options.cameraUi;
        internal ValueWriter(Dictionary<Object, string> objects, ResourceWriter resources)
        { this.objects = objects; this.resources = resources; }

        //UI 与世界材质分别导出，保留各自编译配置。
        internal ValueData Material(Material material, string path, bool ui, bool cameraUi = false) =>
            material == null ? new ValueData { kind = "null" } : new ValueData { kind = "asset", text = resources.Add(material, path, ui, cameraUi) };

        //错误路径包含层级、组件及字段，调用方不会提交失败产物。
        internal ValueData Write(object value, string path)
        {
            if (value == null || value is Object obj && obj == null) return new ValueData { kind = "null" };
            Type type = value.GetType();
            if (value is Object reference)
                return objects.TryGetValue(reference, out string id)
                    ? new ValueData { kind = "object", text = id }
                    : new ValueData { kind = "asset", text = resources.Add(reference, path) };
            if (value is string text) return new ValueData { kind = "string", text = text };
            if (value is bool flag) return new ValueData { kind = "bool", number = flag ? 1 : 0 };
            if (type.IsEnum || value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
                return new ValueData { kind = "integer", text = type.IsEnum ? Convert.ToString(Convert.ChangeType(value, Enum.GetUnderlyingType(type)), CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) };
            if (value is float || value is double)
            {
                double number = Convert.ToDouble(value);
                if (double.IsNaN(number)) throw new NotSupportedException(path + "：数值不能为 NaN。");
                return double.IsInfinity(number)
                    ? new ValueData { kind = "specialNumber", text = number.ToString(CultureInfo.InvariantCulture) }
                    : new ValueData { kind = "number", number = number };
            }
            float[] vector = Vector(value);
            if (vector != null)
            {
                if (vector.Any(component => float.IsNaN(component) || float.IsInfinity(component)))
                    throw new NotSupportedException(path + "：向量或颜色包含非有限数值。");
                return new ValueData { kind = "vector", vector = vector };
            }
            if (value is UnityEventBase ev) return EventWriter.Write(ev, this, objects, path);
            ValueData math = MathValueWriter.Write(value, this, path);
            if (math != null) return math;
            if (value is IDictionary) throw new NotSupportedException(path + "：Unity 不序列化 Dictionary。");
            if (!type.IsValueType && !visiting.Add(value)) throw new NotSupportedException(path + "：不支持循环数据对象。");
            try
            {
                if (value is IList list)
                {
                    var result = new ValueData { kind = "array", items = new List<ValueData>() };
                    for (int i = 0; i < list.Count; i++) result.items.Add(Write(list[i], path + "[" + i + "]"));
                    return result;
                }
                if (value is ISerializationCallbackReceiver) throw new NotSupportedException(path + "：序列化回调尚未适配。");
                if (!type.IsSerializable) throw new NotSupportedException(path + "：类型不可序列化：" + type.FullName);
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                    throw new NotSupportedException(path + "：尚未适配的 Unity 值类型：" + type.FullName);
                return new ValueData { kind = "data", fields = Fields(value, path) };
            }
            finally { if (!type.IsValueType) visiting.Remove(value); }
        }

        //继承字段按声明层级枚举，不导出 MonoBehaviour 和引擎私有状态。
        internal List<MemberData> Fields(object value, string path)
        {
            var members = new List<MemberData>();
            var names = new HashSet<string>();
            for (Type type = value.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(object); type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized) continue;
                    if (field.IsDefined(typeof(SerializeReference), true)) throw new NotSupportedException(path + "." + field.Name + "：SerializeReference 尚未支持。");
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true)) continue;
                    if (!names.Add(field.Name)) throw new NotSupportedException(path + "：隐藏的同名字段 " + field.Name);
                    members.Add(new MemberData { name = field.Name, value = Write(field.GetValue(value), path + "." + field.Name) });
                }
            return members;
        }

        //按公开成员名导出内置组件，避免依赖 Unity 内部 YAML 字段布局。
        internal MemberData Property(object source, string name, string path)
        {
            PropertyInfo property = source.GetType().GetProperty(name);
            FieldInfo field = property == null ? source.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance) : null;
            if (property == null && field == null) throw new MissingMemberException(source.GetType().FullName, name);
            return new MemberData { name = name, property = true, value = Write(property != null ? property.GetValue(source) : field.GetValue(source), path + "." + name) };
        }

        //数学类型按固定分量顺序编码，矩阵沿用 Unity 的列优先索引。
        private static float[] Vector(object value)
        {
            if (value is Vector2 v2) return new[] { v2.x, v2.y };
            if (value is Vector3 v3) return new[] { v3.x, v3.y, v3.z };
            if (value is Vector4 v4) return new[] { v4.x, v4.y, v4.z, v4.w };
            if (value is Quaternion q) return new[] { q.x, q.y, q.z, q.w };
            if (value is Color c) return new[] { c.r, c.g, c.b, c.a };
            if (value is Color32 c32) return new float[] { c32.r, c32.g, c32.b, c32.a };
            if (value is Rect r) return new[] { r.x, r.y, r.width, r.height };
            if (value is Vector2Int i2) return new float[] { i2.x, i2.y };
            if (value is Vector3Int i3) return new float[] { i3.x, i3.y, i3.z };
            if (value is Bounds b) return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z };
            if (value is Matrix4x4 m) return Enumerable.Range(0, 16).Select(i => m[i]).ToArray();
            return null;
        }
    }

    //循环检测使用引用身份，避免业务 Equals 改变结果。
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
