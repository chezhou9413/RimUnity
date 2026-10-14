namespace RimUnity;

//护盾等实例通过属性块传递颜色和交互点，不污染共享材质。
public sealed partial class ParticleSystemRenderer
{
    private MaterialPropertyBlock propertyBlock;

    //复制调用者状态，后续修改同一个输入块不会改变已设置的渲染器。
    public void SetPropertyBlock(MaterialPropertyBlock properties)
        => propertyBlock = properties == null ? null : new MaterialPropertyBlock(properties);
}
