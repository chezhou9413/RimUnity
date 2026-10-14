
namespace RimUnity.UI.Runtime;

//旋转文字和世界文字先在局部矩形排版，再变换每个字形顶点。
internal static partial class UguiTextRenderer
{
    private static UguiRuntime.Entry localEntry;
    private static Rect localRect;
    private static bool localClip;
    internal static void DrawLocal(UguiRuntime.Entry entry, RimUnity.UI.Text text, Color tint)
    {
        Material previous = text.material;
        localEntry = entry; localRect = text is Text custom ? custom.ContentRect : entry.Rect.rect;
        localClip = text is not Text { clipText: false };
        text.material ??= UiMaskData.DefaultMaterial();
        try
        {
            UiTextStyle style = UguiRuntime.TextStyle(text);
            Draw(entry, text, style, new Rect(0, 0, localRect.width, localRect.height), tint, 1);
        }
        finally { localEntry = null; text.material = previous; }
    }
    private static Vector2 LocalPosition(Vector2 point)
        => localEntry == null ? point : RectTransformUtility.ToGui(localEntry.Canvas, localEntry.Rect.TransformPoint(new Vector2(localRect.xMin + point.x, localRect.yMax - point.y)));
}
