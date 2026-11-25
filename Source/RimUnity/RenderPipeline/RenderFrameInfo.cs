namespace RimUnity.RenderPipeline;

//主线程准备帧阶段提供给渲染特性的当前帧元信息。
public readonly struct RenderFrameInfo
{
    public int FrameNumber { get; }
    public float DeltaTime { get; }
    public double ElapsedTime { get; }
    public int Width { get; }
    public int Height { get; }
    public bool CanCopyColorTarget { get; }

    internal RenderFrameInfo(int frameNumber, float deltaTime, double elapsedTime, int width, int height, bool canCopyColorTarget)
    {
        FrameNumber = frameNumber;
        DeltaTime = deltaTime;
        ElapsedTime = elapsedTime;
        Width = width;
        Height = height;
        CanCopyColorTarget = canCopyColorTarget;
    }
}
