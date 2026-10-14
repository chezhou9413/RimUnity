namespace RimUnity.UI;

//画布遍历时按层级收集裁切和模板遮罩。
public abstract class MaskableGraphic : Graphic
{
    public bool maskable { get; set; } = true;
}
