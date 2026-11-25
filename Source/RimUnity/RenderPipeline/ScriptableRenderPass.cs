using System;

namespace RimUnity.RenderPipeline;

//可注入宿主帧渲染管线的单次自定义渲染步骤。
public abstract class ScriptableRenderPass : IDisposable
{
    public RenderPassEvent renderPassEvent { get; set; } = RenderPassEvent.AfterRendering;

    //主线程在 CaptureFrame 阶段准备本帧渲染所需的状态与资源；返回值作为不可变载荷传入渲染线程 Execute。
    public virtual object PrepareFrame(in RenderFrameInfo frameInfo) => null;

    //渲染线程将当前步骤的渲染或传输命令录制到当前帧命令缓冲。
    public abstract void Execute(in RenderPassContext context, object payload);

    //交换链或渲染目标尺寸变化时通知。
    public virtual void OnRenderTargetRebuilt(int width, int height) { }

    public virtual void Dispose() { }
}
