using System;
using System.Collections.Generic;

namespace RimUnity.RenderPipeline;

//可动态注册到 RimUnity 渲染管线的扩展特性，由业务模组自行组合和调度 Pass。
public abstract class ScriptableRendererFeature : IDisposable
{
    public bool isActive { get; set; } = true;

    //注册到管线时初始化特性持有的渲染步骤。
    public virtual void Create() { }

    //收集本特性需要注入管线的自定义步骤。
    public abstract void AddRenderPasses(IList<ScriptableRenderPass> passes);

    //每帧在主线程判断是否需要执行本特性。
    public virtual bool ShouldExecute(in RenderFrameInfo frameInfo) => isActive;

    //渲染目标重建时通知。
    public virtual void OnRenderTargetRebuilt(int width, int height) { }

    public virtual void Dispose() { }
}
