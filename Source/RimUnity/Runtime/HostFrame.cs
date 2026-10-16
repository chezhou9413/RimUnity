using System;
using RimUnity.Runtime;
using Verse;
using Waylith;

namespace RimUnity;

//直接订阅 Waylith 主循环，在宿主更新之后、冻结绘制队列之前推进自有运行时。
internal static class HostFrame
{
    private static EngineApplication application;
    private static Game lastGame;
    private static bool framePending;
    private static float effectsTime;
    //帧调度安装后，内容模组才可以创建运行时对象。
    internal static bool IsInstalled => application != null;
    //调试场景仅在启用期间订阅，生成测试实例发生在本帧模拟之前。
    internal static event Action BeforeSimulation;

    //加载完成后在游戏主线程安装一次帧回调。
    internal static void Install()
    {
        if (!UnityData.IsInMainThread) throw new InvalidOperationException("RimUnity 帧调度必须在游戏主线程安装。");
        if (application != null) return;
        NativeRuntime.InitializeOnCurrentThread();
        application = EngineApplication.Instance ?? throw new InvalidOperationException("Waylith 主循环尚未创建。");
        NativeRuntime.SceneOwnerProvider = () => Current.Game;
        NativeRuntime.ExceptionRaised += Report;
        NativeRuntime.HierarchyChanged += UI.Runtime.UguiDirty.Mark;
        application.OnUpdate += Update;
        application.FrameEnqueued += FinishFrame;
        application.BeforeGraphicsShutdown += Shutdown;
        Rendering.LoadingWarmup.Install(application);
        lastGame = Current.Game;
    }

    //退出或更换游戏时释放场景实例，模板和共享资源保留在资源缓存中。
    private static void Update()
    {
        //读档与地图生成会在后台重建世界数据，完成前不推进脚本或提交地图绘制。
        if (LongEventHandler.AnyEventNowOrWaiting) return;
        //最小化窗口时没有绘制快照，在下一次更新前结束未提交的帧。
        FinishFrame(application);
        if (!ReferenceEquals(lastGame, Current.Game))
        {
            ScreenEfx.EfxPlayer.SynchronizeSession();
            effectsTime = 0;
            NativeRuntime.ClearScene(lastGame);
            ParticleDrawBatch.Clear();
            Assets.AssetLoader.EvictTextures();
            lastGame = Current.Game;
        }
        BeforeSimulation?.Invoke();
        HostCamera.Synchronize();
        NativeRuntime.BeginFrame(Waylith.Time.DeltaTime);
        framePending = true;
        try
        {
            NativeRuntime.Update();
            Tweening.DOTween.Update();
            NativeRuntime.LateUpdate();
            float realDelta = Time.unscaledDeltaTime;
            float gameDelta = Current.Game == null || Find.TickManager.Paused ? 0 : realDelta * Find.TickManager.TickRateMultiplier;
            effectsTime += gameDelta;
            ScreenEfx.EfxPlayer.Update(new(gameDelta, realDelta, effectsTime, Time.unscaledTime));
            ParticleFrame.Draw();
        }
        catch
        {
            FinishFrame(application);
            throw;
        }
        if (application.Headless) FinishFrame(application);
    }

    //绘制数据冻结后再执行帧尾协程和释放资源。
    private static void FinishFrame(EngineApplication app)
    {
        if (!framePending) return;
        framePending = false;
        NativeRuntime.EndFrame();
    }

    //将脚本异常明确交给游戏日志，保留对象与堆栈信息。
    private static void Report(Exception error, Object context)
        => Log.Error($"[RimUnity] {context?.name ?? "运行时"}：{error}");

    //GPU 设备销毁前清理自有场景并解除订阅。
    private static void Shutdown(EngineApplication app)
    {
        FinishFrame(app);
        //宿主已等待渲染线程和 GPU 并释放管线，先丢弃取消的加载快照，再处理片段释放回调。
        Rendering.LoadingWarmup.Shutdown(app);
        ScreenEfx.EfxPlayer.Clear();
        ScreenEfx.EfxPlayer.Drain();
        ScreenEfx.EfxFrameBuilder.Shutdown();
        NativeRuntime.ClearScene();
        ParticleDrawBatch.Clear();
        Tweening.DOTween.Clear();
        app.OnUpdate -= Update;
        app.FrameEnqueued -= FinishFrame;
        app.BeforeGraphicsShutdown -= Shutdown;
        NativeRuntime.ExceptionRaised -= Report;
        NativeRuntime.HierarchyChanged -= UI.Runtime.UguiDirty.Mark;
        NativeRuntime.SceneOwnerProvider = null;
        application = null;
    }
}
