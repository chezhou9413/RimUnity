namespace RimUnity.UI;

//参数由 RimUnity.Canvas 读取并应用。
public class CanvasScaler : MonoBehaviour
{
    public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
    public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
    public enum Unit { Centimeters, Millimeters, Inches, Points, Picas }
    public ScaleMode uiScaleMode { get; set => SetProperty(ref field, value); }
    public ScreenMatchMode screenMatchMode { get; set => SetProperty(ref field, value); }
    public Unit physicalUnit { get; set => SetProperty(ref field, value); } = Unit.Points;
    public float referencePixelsPerUnit { get; set => SetProperty(ref field, value); } = 100f;
    public float scaleFactor { get; set => SetProperty(ref field, value); } = 1f;
    public Vector2 referenceResolution { get; set => SetProperty(ref field, value); } = new(800, 600);
    public float matchWidthOrHeight { get; set => SetProperty(ref field, value); }
    public float fallbackScreenDPI { get; set => SetProperty(ref field, value); } = 96f;
    public float defaultSpriteDPI { get; set => SetProperty(ref field, value); } = 96f;
    public float dynamicPixelsPerUnit { get; set => SetProperty(ref field, value); } = 1f;
}
