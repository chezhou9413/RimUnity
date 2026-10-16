using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Waylith;

namespace RimUnity.Diagnostics;

//测试地图只存在于本次调试会话；正常游戏不订阅更新或采样回调。
internal sealed partial class PerformanceLab
{
    internal static PerformanceLab Current { get; private set; }
    private static bool opening;
    private readonly Game game;
    private readonly Map returnMap;
    private readonly TimeSpeed previousSpeed;
    private readonly EngineApplication application;
    private PocketMapParent parent;
    private bool closing;
    internal Map Map { get; private set; }
    internal List<LabEffect> Effects { get; private set; }
    internal LabSampler Sampler { get; private set; }
    internal string Status = "选择特效后开始播放；测试期间使用真实时间，殖民地保持暂停。";
    internal bool IsCurrent => !closing && Prefs.DevMode && Verse.Current.Game == game
        && Find.CurrentMap == Map && Map != null && WorldRendererUtility.DrawingMap;

    //记录进入前的游戏与地图，退出时只清理本会话拥有的资源。
    private PerformanceLab()
    {
        game = Verse.Current.Game; returnMap = Find.CurrentMap;
        previousSpeed = Find.TickManager.CurTimeSpeed;
        application = EngineApplication.Instance;
    }

    [DebugAction("RimUnity", "特效性能测试场", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void Open()
    {
        if (!Prefs.DevMode || opening) return;
        if (Current != null) return;
        opening = true;
        var session = new PerformanceLab();
        LongEventHandler.QueueLongEvent(() =>
        {
            try
            {
                session.Effects = LabEffect.Discover();
                if (session.Effects.Count == 0) throw new InvalidOperationException("已启用模组没有可测试的粒子预制体。请先导入含 ParticleSystem 的特效。");
                Find.TickManager.Pause();
                session.parent = (PocketMapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PocketMap);
                session.parent.sourceMap = session.returnMap;
                session.parent.mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("RimUnity_PerformanceLab");
                session.Map = MapGenerator.GenerateMap(new IntVec3(96, 1, 96), session.parent,
                    session.parent.mapGenerator, isPocketMap: true);
                Find.World.pocketMaps.Add(session.parent);
                //FinalizeInit 把网格构建排在长事件之后，切图与面板必须等它完成。
                LongEventHandler.ExecuteWhenFinished(session.Activate);
            }
            catch (Exception ex)
            {
                session.FailOpen(ex);
            }
        }, "GeneratingMap", false, null);
    }

    //地图生成及绘制初始化结束后，才允许相机、窗口和采样访问测试地图。
    private void Activate()
    {
        try
        {
            if (Verse.Current.Game != game || !Prefs.DevMode) { Close(); return; }
            CameraJumper.TryJump(Map.Center, Map, CameraJumper.MovementMode.Cut);
            Current = this;
            Sampler = new LabSampler(this);
            LabHooks.Install();
            HostFrame.BeforeSimulation += Update;
            application.PrePollEvents += CheckContext;
            application.BeforeGraphicsShutdown += Shutdown;
            Find.WindowStack.Add(new LabWindow(this));
        }
        catch (Exception ex) { FailOpen(ex); }
        finally { opening = false; }
    }

    //先记录原始异常，再等待已排队的地图初始化完成，避免清理错误盖住创建失败。
    private void FailOpen(Exception exception)
    {
        Log.Error("[RimUnity] 无法建立特效测试场：" + exception);
        Messages.Message("特效测试场创建失败：" + exception.Message, MessageTypeDefOf.RejectInput, false);
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try { Close(); }
            catch (Exception cleanupError) { Log.Error("[RimUnity] 创建失败后的测试地图清理异常：" + cleanupError); }
            finally { opening = false; }
        });
    }

    //地图切换、读档、退出开发者模式时，在下一轮模拟之前解除采样。
    private void CheckContext(EngineApplication app)
    {
        if (!IsCurrent) Close();
    }

    //关闭程序时仅解除回调与实例，地图交给宿主退出流程。
    private void Shutdown(EngineApplication app) => Close(removeMap: false);

    //面板关闭也代表退出测试，防止隐藏窗口留下后台压力负载。
    internal void Close(bool removeMap = true)
    {
        if (closing) return;
        closing = true;
        HostFrame.BeforeSimulation -= Update;
        application.PrePollEvents -= CheckContext;
        application.BeforeGraphicsShutdown -= Shutdown;
        try
        {
            Sampler?.Stop("退出测试场", export: true);
            ParticleSpikeCapture.EndLabCapture(detailedCapture);
            StopPlayback();
        }
        finally
        {
            try
            {
                if (removeMap && Verse.Current.Game == game)
                {
                    Map testMap = Map ?? parent?.Map;
                    if (Find.CurrentMap == testMap && returnMap != null && game.Maps.Contains(returnMap)) game.CurrentMap = returnMap;
                    if (parent != null) Find.World.pocketMaps.Remove(parent);
                    if (testMap != null && game.Maps.Contains(testMap)) LabHooks.RemoveMap(game, testMap);
                }
            }
            finally
            {
                LabHooks.Remove();
                Current = null;
                if (removeMap && Verse.Current.Game == game) Find.TickManager.CurTimeSpeed = previousSpeed;
                Sampler?.Dispose();
                Map = null; parent = null;
            }
        }
    }
}
