namespace RimUnity.UI;

//矩形裁切区域在每次绘制前随变换重新计算。
public class RectMask2D : MonoBehaviour
{
    public Vector4 padding { get; set; }
    public Vector2 softness { get; set; }
    //兼容主动提交裁切的调用；布局与绘制本身也会重新取当前矩形。
    public void PerformClipping() => Runtime.UguiRuntime.Prepare();
}
