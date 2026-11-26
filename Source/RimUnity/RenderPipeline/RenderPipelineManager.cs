using System;
using System.Collections.Generic;
using Brutal.VulkanApi;
using RimWorldWaylith.Rendering;
using Verse;
using Waylith;
using Waylith.Graphics;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

namespace RimUnity.RenderPipeline;

//管理模组注册的 ScriptableRendererFeature，并按 RenderPassEvent 桥接到 Waylith FrameRenderer。
public static class RenderPipelineManager
{
    private static readonly List<FeatureRegistration> s_registrations = new();
    private static readonly HashSet<ScriptableRendererFeature> s_retiringFeatures = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Action> s_pendingAfterSubmit = new();
    private static readonly object s_submitLock = new();
    private static readonly Action<Action> s_enqueueAfterSubmit = _EnqueueAfterSubmit;
    private static EngineApplication s_application;
    private static bool s_frameEndedSubscribed;

    private sealed class FeatureRegistration
    {
        internal readonly ScriptableRendererFeature Feature;
        internal readonly List<ScriptableRenderPass> Passes = new();
        internal readonly List<FeatureBridge> Bridges = new();
        internal bool Created;
        internal bool FeatureDisposed;
        internal int PendingBridgeDisposals;
        internal int LastEvaluatedFrame = -1;
        internal bool LastShouldExecute;

        internal FeatureRegistration(ScriptableRendererFeature feature)
        {
            Feature = feature;
        }

        //同一 Feature 跨多个 RenderPassEvent 时，每帧只调用一次 ShouldExecute 并复用判断结果。
        internal bool EvaluateShouldExecute(in RenderFrameInfo frameInfo)
        {
            if (LastEvaluatedFrame != frameInfo.FrameNumber)
            {
                LastEvaluatedFrame = frameInfo.FrameNumber;
                LastShouldExecute = Feature.ShouldExecute(in frameInfo);
            }
            return LastShouldExecute;
        }

        //保证 Feature.Dispose 最多执行一次，避免延迟卸载与关闭流程重复释放。
        internal void DisposeFeatureOnce()
        {
            if (FeatureDisposed)
            {
                return;
            }
            FeatureDisposed = true;
            try
            {
                Feature.Dispose();
            }
            catch (Exception exception)
            {
                Log.Error($"[RimUnity.RenderPipeline] 释放 Feature 异常：{exception}");
            }
        }
    }

    private sealed class FeatureBridge : IRenderFeature, IDisposable
    {
        private readonly FeatureRegistration _owner;
        private readonly ScriptableRendererFeature _feature;
        private readonly RenderPassEvent _passEvent;
        private readonly List<ScriptableRenderPass> _passes;
        private readonly IRenderPass[] _nativePasses;
        private bool _activeThisFrame;
        private bool _disposed;

        internal RenderPassEvent Event => _passEvent;
        public bool IsActive => _activeThisFrame;
        public IReadOnlyList<IRenderPass> Passes => _nativePasses;

        internal FeatureBridge(FeatureRegistration owner, RenderPassEvent passEvent, List<ScriptableRenderPass> passes)
        {
            _owner = owner;
            _feature = owner.Feature;
            _passEvent = passEvent;
            _passes = passes;
            _nativePasses = new IRenderPass[passes.Count];
            for (int index = 0; index < passes.Count; index++)
            {
                _nativePasses[index] = passEvent == RenderPassEvent.AfterRendering
                    ? new PostBackbufferPassBridge(passes[index], index, passes.Count, passEvent)
                    : new StandardPassBridge(passes[index], index, passes.Count, passEvent);
            }
        }

        public object CaptureFrame(in FrameTiming timing)
        {
            _activeThisFrame = false;
            RenderFrameInfo frameInfo = _BuildFrameInfo(in timing);
            try
            {
                if (!_owner.EvaluateShouldExecute(in frameInfo))
                {
                    return null;
                }
                //单 Pass 场景直接透传载荷避免每帧分配数组；多 Pass 每次分配独立快照数组，避免与在途渲染线程帧竞态。
                if (_passes.Count == 1)
                {
                    object singlePayload = _passes[0].PrepareFrame(in frameInfo);
                    _activeThisFrame = true;
                    return singlePayload;
                }
                object[] payloads = new object[_passes.Count];
                for (int index = 0; index < _passes.Count; index++)
                {
                    payloads[index] = _passes[index].PrepareFrame(in frameInfo);
                }
                _activeThisFrame = true;
                return payloads;
            }
            catch (Exception exception)
            {
                Log.Error($"[RimUnity.RenderPipeline] {_feature.GetType().Name}.PrepareFrame 异常：{exception}");
                _activeThisFrame = false;
                return null;
            }
        }

        public void OnRenderTargetRebuilt(IRenderTarget renderTarget)
        {
            VkExtent2D extent = renderTarget != null ? renderTarget.GetExtent() : default;
            try
            {
                _feature.OnRenderTargetRebuilt(extent.Width, extent.Height);
                for (int index = 0; index < _passes.Count; index++)
                {
                    _passes[index].OnRenderTargetRebuilt(extent.Width, extent.Height);
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[RimUnity.RenderPipeline] {_feature.GetType().Name}.OnRenderTargetRebuilt 异常：{exception}");
            }
        }

        //由 FrameRenderer.DisposeRetiredFeatures 或 FrameRenderer.Dispose 调用，确保每个 Pass 仅释放一次。
        public void Dispose()
        {
            DisposePassesOnly();
            if (_owner.PendingBridgeDisposals > 0 && --_owner.PendingBridgeDisposals == 0)
            {
                _owner.Passes.Clear();
                _owner.DisposeFeatureOnce();
                s_retiringFeatures.Remove(_owner.Feature);
            }
        }

        internal void DisposePassesOnly()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            for (int index = 0; index < _passes.Count; index++)
            {
                try
                {
                    _passes[index]?.Dispose();
                }
                catch (Exception exception)
                {
                    Log.Error($"[RimUnity.RenderPipeline] 释放 Pass 异常：{exception}");
                }
            }
        }
    }

    private class StandardPassBridge : IRenderPass
    {
        private readonly ScriptableRenderPass _pass;
        private readonly int _index;
        private readonly int _passCount;
        private readonly RenderPassEvent _passEvent;

        public byte PassId => 0;
        public PassAttachment Attachment => null;
        public bool PreBackbuffer => _passEvent is RenderPassEvent.BeforeRendering or RenderPassEvent.BeforePostProcessing;

        internal StandardPassBridge(ScriptableRenderPass pass, int index, int passCount, RenderPassEvent passEvent)
        {
            _pass = pass;
            _index = index;
            _passCount = passCount;
            _passEvent = passEvent;
        }

        public void Record(in FrameContext frameContext, object featurePayload)
        {
            object payload = _passCount == 1
                ? featurePayload
                : featurePayload is object[] payloadArray && (uint)_index < (uint)payloadArray.Length
                    ? payloadArray[_index]
                    : null;
            RenderPassContext passContext = new(in frameContext, _passEvent, _CanCopySwapchainColor(), s_enqueueAfterSubmit);
            try
            {
                _pass.Execute(in passContext, payload);
            }
            catch (Exception exception)
            {
                Log.Error($"[RimUnity.RenderPipeline] {_pass.GetType().Name}.Execute 异常：{exception}");
            }
        }

        public void OnRenderTargetRebuilt(IRenderTarget target) { }
        public void Dispose() { }
    }

    private sealed class PostBackbufferPassBridge : StandardPassBridge, IPostBackbufferPass
    {
        internal PostBackbufferPassBridge(ScriptableRenderPass pass, int index, int passCount, RenderPassEvent passEvent)
            : base(pass, index, passCount, passEvent) { }
    }

    public static bool IsReady => s_application != null && s_application.FrameRenderer != null;

    //注册自定义渲染特性；可在 HostFrame 安装前或安装后调用，重复注册或仍在等待宿主释放的实例返回 false。
    public static bool RegisterFeature(ScriptableRendererFeature feature)
    {
        if (feature == null)
        {
            throw new ArgumentNullException(nameof(feature));
        }
        for (int index = 0; index < s_registrations.Count; index++)
        {
            if (ReferenceEquals(s_registrations[index].Feature, feature))
            {
                return false;
            }
        }
        if (s_retiringFeatures.Contains(feature))
        {
            return false;
        }

        FeatureRegistration registration = new(feature);
        FrameRenderer renderer = s_application?.FrameRenderer;
        if (renderer != null)
        {
            _EnsureFrameEndedSubscribed(renderer);
            //先完成 Create、AddRenderPasses、阶段校验和宿主挂载再入表；失败时清理本次状态，避免阻塞后续扩展注册。
            _AttachRegistration(registration, renderer);
        }
        s_registrations.Add(registration);
        return true;
    }

    //从渲染管线注销特性；默认由宿主在在途帧完成后延迟释放，避免渲染线程并发访问已销毁资源。
    public static bool UnregisterFeature(ScriptableRendererFeature feature, bool dispose = true)
    {
        if (feature == null)
        {
            return false;
        }
        int matchedIndex = -1;
        for (int index = 0; index < s_registrations.Count; index++)
        {
            if (ReferenceEquals(s_registrations[index].Feature, feature))
            {
                matchedIndex = index;
                break;
            }
        }
        if (matchedIndex < 0)
        {
            return false;
        }

        FeatureRegistration registration = s_registrations[matchedIndex];
        s_registrations.RemoveAt(matchedIndex);
        bool deferredDispose = _DetachRegistration(registration, dispose);
        if (dispose && !deferredDispose)
        {
            _DisposeRegistration(registration);
        }
        return true;
    }

    internal static void Install(EngineApplication application)
    {
        if (application == null)
        {
            return;
        }
        if (!ReferenceEquals(s_application, application))
        {
            s_application = application;
            s_frameEndedSubscribed = false;
        }
        _AttachPendingRegistrations();
    }

    //BeforeGraphicsShutdown 阶段宿主已等待 GPU 空闲并先释放 FrameRenderer；补齐尚未释放的 Pass 与全部 Feature.Dispose。
    internal static void Shutdown()
    {
        FrameRenderer renderer = s_application?.FrameRenderer;
        if (renderer != null && s_frameEndedSubscribed)
        {
            renderer.RenderThreadFrameEnded -= _OnRenderThreadFrameEnded;
        }
        for (int index = s_registrations.Count - 1; index >= 0; index--)
        {
            FeatureRegistration registration = s_registrations[index];
            if (registration.Bridges.Count == 0)
            {
                _DisposeRegistration(registration);
                continue;
            }
            for (int bridgeIndex = 0; bridgeIndex < registration.Bridges.Count; bridgeIndex++)
            {
                FeatureBridge bridge = registration.Bridges[bridgeIndex];
                renderer?.RemoveFeature(bridge, dispose: false);
                bridge.DisposePassesOnly();
            }
            registration.Bridges.Clear();
            registration.Passes.Clear();
            registration.DisposeFeatureOnce();
        }
        s_registrations.Clear();
        s_retiringFeatures.Clear();
        s_frameEndedSubscribed = false;
        lock (s_submitLock)
        {
            s_pendingAfterSubmit.Clear();
        }
        s_application = null;
    }

    private static void _EnsureFrameEndedSubscribed(FrameRenderer renderer)
    {
        if (!s_frameEndedSubscribed)
        {
            renderer.RenderThreadFrameEnded += _OnRenderThreadFrameEnded;
            s_frameEndedSubscribed = true;
        }
    }

    //安装时逐个挂载预注册扩展；单个扩展失败只移除自身并记错，不阻塞其他正常扩展。
    private static void _AttachPendingRegistrations()
    {
        FrameRenderer renderer = s_application?.FrameRenderer;
        if (renderer == null)
        {
            return;
        }
        _EnsureFrameEndedSubscribed(renderer);
        FeatureRegistration[] pending = s_registrations.ToArray();
        for (int index = 0; index < pending.Length; index++)
        {
            FeatureRegistration registration = pending[index];
            if (!s_registrations.Contains(registration) || registration.Bridges.Count > 0)
            {
                continue;
            }
            try
            {
                _AttachRegistration(registration, renderer, checkStillRegistered: true);
            }
            catch (Exception exception)
            {
                s_registrations.Remove(registration);
                Log.Error($"[RimUnity.RenderPipeline] 挂载预注册 Feature 失败，已移除 {registration.Feature.GetType().FullName}：{exception}");
            }
        }
    }

    private static void _AttachRegistration(FeatureRegistration registration, FrameRenderer renderer, bool checkStillRegistered = false)
    {
        if (renderer == null || registration.Bridges.Count > 0)
        {
            return;
        }

        bool createdNow = false;
        try
        {
            if (!registration.Created)
            {
                createdNow = true;
                registration.Feature.Create();
                registration.Passes.Clear();
                registration.Feature.AddRenderPasses(registration.Passes);
                registration.Created = true;
            }
            if (checkStillRegistered && !s_registrations.Contains(registration))
            {
                _DisposeRegistration(registration);
                registration.Created = false;
                return;
            }

            Dictionary<RenderPassEvent, List<ScriptableRenderPass>> groupedPasses = new();
            for (int index = 0; index < registration.Passes.Count; index++)
            {
                ScriptableRenderPass pass = registration.Passes[index];
                if (pass == null)
                {
                    continue;
                }
                if (!Enum.IsDefined(typeof(RenderPassEvent), pass.renderPassEvent))
                {
                    throw new NotSupportedException($"[RimUnity.RenderPipeline] 不支持的渲染阶段：{pass.renderPassEvent}");
                }
                if (!groupedPasses.TryGetValue(pass.renderPassEvent, out List<ScriptableRenderPass> bucket))
                {
                    bucket = new List<ScriptableRenderPass>();
                    groupedPasses.Add(pass.renderPassEvent, bucket);
                }
                bucket.Add(pass);
            }

            foreach (KeyValuePair<RenderPassEvent, List<ScriptableRenderPass>> entry in groupedPasses)
            {
                FeatureBridge bridge = new(registration, entry.Key, entry.Value);
                int insertIndex = _FindInsertIndex(renderer, entry.Key);
                renderer.InsertFeature(insertIndex, bridge);
                registration.Bridges.Add(bridge);
            }
        }
        catch
        {
            for (int index = 0; index < registration.Bridges.Count; index++)
            {
                renderer.RemoveFeature(registration.Bridges[index], dispose: false);
            }
            registration.Bridges.Clear();
            if (createdNow || registration.Created)
            {
                _DisposeRegistration(registration);
                registration.Created = false;
            }
            throw;
        }
    }

    //当 dispose=true 且渲染器存活时，交由 Waylith FrameRenderer 在全部在途帧结束后安全 Dispose。
    private static bool _DetachRegistration(FeatureRegistration registration, bool dispose)
    {
        FrameRenderer renderer = s_application?.FrameRenderer;
        int deferredCount = 0;
        if (renderer != null && registration.Bridges.Count > 0)
        {
            if (dispose)
            {
                registration.PendingBridgeDisposals = registration.Bridges.Count;
            }
            for (int index = 0; index < registration.Bridges.Count; index++)
            {
                if (renderer.RemoveFeature(registration.Bridges[index], dispose: dispose))
                {
                    if (dispose)
                    {
                        deferredCount++;
                    }
                }
                else if (dispose && registration.PendingBridgeDisposals > 0)
                {
                    registration.PendingBridgeDisposals--;
                }
            }
            if (dispose && deferredCount > 0)
            {
                s_retiringFeatures.Add(registration.Feature);
            }
        }
        registration.Bridges.Clear();
        return deferredCount > 0;
    }

    private static void _DisposeRegistration(FeatureRegistration registration)
    {
        for (int index = 0; index < registration.Passes.Count; index++)
        {
            try
            {
                registration.Passes[index]?.Dispose();
            }
            catch (Exception exception)
            {
                Log.Error($"[RimUnity.RenderPipeline] 释放 Pass 异常：{exception}");
            }
        }
        registration.Passes.Clear();
        registration.DisposeFeatureOnce();
    }

    //同一 RenderPassEvent 内按注册顺序依次追加，保持 FIFO 执行序。
    private static int _FindInsertIndex(FrameRenderer renderer, RenderPassEvent passEvent)
    {
        IReadOnlyList<IRenderFeature> features = renderer.Features;
        switch (passEvent)
        {
            case RenderPassEvent.BeforeRendering:
            {
                int fallbackIndex = 0;
                for (int index = 0; index < features.Count; index++)
                {
                    if (features[index] is WorldRenderFeature or MapRenderFeature)
                    {
                        return index;
                    }
                    if (features[index] is FeatureBridge existing && existing.Event == RenderPassEvent.BeforeRendering)
                    {
                        fallbackIndex = index + 1;
                    }
                }
                return fallbackIndex;
            }

            case RenderPassEvent.BeforePostProcessing:
                for (int index = 0; index < features.Count; index++)
                {
                    if (features[index] is PostFxFeature)
                    {
                        return index;
                    }
                }
                return features.Count;

            case RenderPassEvent.AfterRendering:
            default:
                for (int index = 0; index < features.Count; index++)
                {
                    if (features[index] is ScreenCaptureFeature)
                    {
                        return index;
                    }
                }
                return features.Count;
        }
    }

    private static RenderFrameInfo _BuildFrameInfo(in FrameTiming timing)
    {
        WindowRenderContext renderContext = s_application?.RenderContext;
        VkExtent2D extent = renderContext != null ? renderContext.Extent : default;
        bool canCopyColorTarget = renderContext != null && (renderContext.SwapchainImageUsage & VkImageUsageFlags.TransferSrcBit) != 0;
        return new RenderFrameInfo(timing.FrameNumber, timing.DeltaTime, timing.ElapsedTime, extent.Width, extent.Height, canCopyColorTarget);
    }

    private static bool _CanCopySwapchainColor()
    {
        WindowRenderContext renderContext = s_application?.RenderContext;
        return renderContext != null && (renderContext.SwapchainImageUsage & VkImageUsageFlags.TransferSrcBit) != 0;
    }

    private static void _EnqueueAfterSubmit(Action callback)
    {
        lock (s_submitLock)
        {
            s_pendingAfterSubmit.Add(callback);
        }
    }

    private static void _OnRenderThreadFrameEnded(RenderFrame _)
    {
        FrameRenderer renderer = s_application?.FrameRenderer;
        Action[] callbackBatch;
        lock (s_submitLock)
        {
            if (s_pendingAfterSubmit.Count == 0)
            {
                return;
            }
            callbackBatch = s_pendingAfterSubmit.ToArray();
            s_pendingAfterSubmit.Clear();
        }
        if (renderer == null)
        {
            return;
        }
        for (int index = 0; index < callbackBatch.Length; index++)
        {
            renderer.RenderToMainThread.Enqueue(callbackBatch[index]);
        }
    }
}
