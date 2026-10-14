namespace RimUnity.UI;

//为文字保留阴影或描边配置。
public class Shadow : MonoBehaviour
{
    public Color effectColor { get; set; } = new(0, 0, 0, 0.5f);
    public Vector2 effectDistance { get; set; } = new(1, -1);
    public bool useGraphicAlpha { get; set; } = true;
}
//在四个方向绘制轮廓。
public class Outline : Shadow { }
