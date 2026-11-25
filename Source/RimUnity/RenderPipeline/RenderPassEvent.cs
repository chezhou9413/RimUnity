namespace RimUnity.RenderPipeline;

//自定义渲染步骤注入 Waylith FrameRenderer 的时机。
public enum RenderPassEvent
{
    BeforeRendering = 0,
    BeforePostProcessing = 100,

    // Waylith FrameRenderer.RecordFrame 会把非 PreBackbuffer 且非 IPostBackbufferPass 的 Pass
    // 直接录制在主 Vulkan RenderPass 内部（BeginRenderPass 与 EndRenderPass 之间）。
    // Vulkan 规范禁止在活动 RenderPass 内执行 vkCmdBlitImage / vkCmdPipelineBarrier 传输命令或嵌套开启新 RenderPass，
    // 而当前 RenderPassContext 仅暴露传输类 Blit 接口，因此暂不开放 BeforeUI / AfterUI，待后续补齐主 Pass 内绘制接口后再启用。
    // BeforeUI = 200,
    // AfterUI = 300,

    AfterRendering = 400
}
