
namespace RimUnity;

//保持 Unity 线段端点颜色接口，修改时保留中间颜色与透明度键。
public sealed partial class LineRenderer
{
    public Color startColor { get => colorGradient.Evaluate(0); set => SetEndpoint(value, false); }
    public Color endColor { get => colorGradient.Evaluate(1); set => SetEndpoint(value, true); }

    //端点颜色和透明度分别写入渐变，两者不互相覆盖。
    private void SetEndpoint(Color color, bool end)
    {
        var colors = colorGradient.colorKeys;
        var alphas = colorGradient.alphaKeys;
        int c = end ? colors.Length - 1 : 0, a = end ? alphas.Length - 1 : 0;
        colors[c] = new GradientColorKey(color, end ? 1 : 0);
        alphas[a] = new GradientAlphaKey(color.a, end ? 1 : 0);
        colorGradient.SetKeys(colors, alphas);
    }
}
