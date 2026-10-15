using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace RimUnity;

//首次绑定时生成访问器，逐帧写曲线不调用字段反射。
internal sealed class AnimatedFloat
{
    internal readonly Func<float> read;
    internal readonly Action<float> write;
    internal readonly float initial;
    internal readonly string path;
    internal readonly bool scale;
    internal AnimatedFloat(object target, string propertyPath, string path)
    {
        this.path = path;
        scale = propertyPath.StartsWith("m_LocalScale.", StringComparison.Ordinal);
        if (target is GameObject go && propertyPath == "m_IsActive")
        { read = () => go.activeSelf ? 1 : 0; write = value => go.SetActive(value > .5f); initial = read(); return; }
        if (target is ParticleSystem particles && AnimatedParticleFloat.TryBind(particles, propertyPath, out var particleRead, out var particleWrite))
        { read = particleRead; write = particleWrite; initial = read(); return; }
        //材质浮点参数不是 CLR 字段，绑定当前渲染器的实例材质，避免改动共享资源。
        if (propertyPath.StartsWith("material.", StringComparison.Ordinal))
        {
            string property = propertyPath[9..];
            if (property.Length == 0 || property.Contains('.'))
                throw new NotSupportedException("当前只支持完整的材质浮点属性，不支持颜色或向量分量：" + propertyPath);
            Material material = target switch
            {
                LineRenderer line => line.material,
                TrailRenderer trail => trail.material,
                MeshRenderer mesh => mesh.material,
                SpriteRenderer sprite => sprite.material,
                ParticleSystemRenderer particlesRenderer => particlesRenderer.material,
                _ => throw new NotSupportedException("此动画目标尚未接通实例材质属性：" + target.GetType().FullName)
            };
            if (material == null) throw new InvalidOperationException("动画目标缺少材质：" + path);
            int id = Shader.PropertyToID(property);
            if (!material.HasProperty(id)) throw new MissingMemberException("动画材质不存在属性：" + path + "/" + property);
            read = () => material.GetFloat(id); write = value => material.SetFloat(id, value);
            initial = read(); return;
        }
        string[] parts = propertyPath.Split('.');
        Expression root = Expression.Constant(target, target.GetType());
        var members = new List<MemberInfo>();
        Type type = target.GetType();
        foreach (string raw in parts)
        {
            string name = Alias(raw);
            MemberInfo member = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) as MemberInfo ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (member == null) throw new MissingMemberException(type.FullName, name);
            members.Add(member); type = MemberType(member);
        }
        Expression get = root;
        foreach (var member in members) get = Expression.MakeMemberAccess(get, member);
        if (type != typeof(float) && type != typeof(double) && type != typeof(int) && type != typeof(bool)) throw new NotSupportedException("曲线目标不是数值：" + propertyPath);
        read = Expression.Lambda<Func<float>>(type == typeof(bool) ? Expression.Condition(get, Expression.Constant(1f), Expression.Constant(0f)) : Expression.Convert(get, typeof(float))).Compile();
        var input = Expression.Parameter(typeof(float), "value");
        var variables = new List<ParameterExpression>(); var body = new List<Expression>();
        Expression owner = root;
        for (int i = 0; i < members.Count - 1; i++)
        {
            var temp = Expression.Variable(MemberType(members[i]), "part" + i); variables.Add(temp);
            body.Add(Expression.Assign(temp, Expression.MakeMemberAccess(owner, members[i]))); owner = temp;
        }
        body.Add(Expression.Assign(Expression.MakeMemberAccess(owner, members[^1]), type == typeof(bool) ? Expression.GreaterThan(input, Expression.Constant(.5f)) : Expression.Convert(input, type)));
        for (int i = variables.Count - 1; i >= 0; i--)
            if (variables[i].Type.IsValueType) body.Add(Expression.Assign(Expression.MakeMemberAccess(i == 0 ? root : variables[i - 1], members[i]), variables[i]));
        body.Add(Expression.Empty());
        write = Expression.Lambda<Action<float>>(Expression.Block(variables, body), input).Compile();
        initial = read();
    }
    private static Type MemberType(MemberInfo member) => member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
    //Unity 的序列化字段名转换为桥接的公开属性。
    internal static string Alias(string name) => name switch
    {
        "m_LocalPosition" => "localPosition", "m_LocalRotation" => "localRotation", "m_LocalScale" => "localScale",
        "localEulerAnglesRaw" => "localEulerAngles", "localEulerAnglesBaked" => "localEulerAngles",
        "m_AnchoredPosition" => "anchoredPosition", "m_SizeDelta" => "sizeDelta", "m_AnchorMin" => "anchorMin", "m_AnchorMax" => "anchorMax", "m_Pivot" => "pivot",
        "m_Color" => "color", "m_Alpha" => "alpha", "m_FillAmount" => "fillAmount", "m_Enabled" => "enabled", "m_IsActive" => "activeSelf",
        _ => name
    };
}
