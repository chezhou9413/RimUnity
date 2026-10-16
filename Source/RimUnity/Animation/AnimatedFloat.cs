using System;

namespace RimUnity;

//实例只保留目标和初值，属性访问器按类型与路径共享。
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
        var accessor = AnimatedFloatAccessor.For(target.GetType(), propertyPath);
        read = () => accessor.Read(target);
        write = value => accessor.Write(target, value);
        initial = read();
    }
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
