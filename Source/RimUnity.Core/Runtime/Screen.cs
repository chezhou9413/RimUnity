namespace RimUnity;

//读取渲染窗口的尺寸与像素密度。
public static class Screen
{
    public static int width => Waylith.EngineWindow.Width;
    public static int height => Waylith.EngineWindow.Height;
    public static float dpi => Waylith.EngineWindow.Dpi;
    public static Rect safeArea => new(0, 0, width, height);
}
