using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Waylith.Rendering;

namespace RimUnity.Diagnostics;

//测试场补丁按会话安装，地图释放补丁只在本次清理期间存在。
internal static class LabHooks
{
    private const string Id = "Chezhou.RimUnity.PerformanceLab";
    private static readonly Harmony patcher = new(Id);
    private static MethodInfo capture, save;
    private static Map removingMap;

    //地图快照计数和临时地图存档保护共用会话生命周期。
    internal static void Install()
    {
        capture = AccessTools.Method(AccessTools.TypeByName("RimWorldWaylith.Rendering.MapRenderFeature"), "CaptureFrame")
            ?? throw new MissingMethodException("宿主缺少地图快照入口。");
        save = AccessTools.Method(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame));
        patcher.Patch(save, prefix: new HarmonyMethod(typeof(LabHooks), nameof(BeforeSave)));
        patcher.Patch(capture, postfix: new HarmonyMethod(typeof(LabHooks), nameof(Captured)));
    }

    //采样停止后只保留存档保护，计数入口立即返回。
    private static void Captured(object __result)
    {
        var sampler = PerformanceLab.Current?.Sampler;
        if (sampler?.Sampling == true && __result is MeshDrawFrame frame)
            sampler.SetSubmittedItems(frame.Draws.Count + frame.PersistentDraws.Count);
    }

    //避免调试小地图进入正式存档；退出测试场即可正常保存。
    private static bool BeforeSave()
    {
        if (PerformanceLab.Current == null) return true;
        Messages.Message("请先退出特效性能测试场，再保存游戏。", MessageTypeDefOf.RejectInput, false);
        return false;
    }

    //原版卸载仍负责地图组件和登记；仅保护生成中断时尚未建立的绘制资源。
    internal static void RemoveMap(Game game, Map map)
    {
        MethodInfo dispose = AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.Dispose));
        removingMap = map;
        try
        {
            patcher.Patch(dispose, prefix: new HarmonyMethod(typeof(LabHooks), nameof(DisposeDrawer)));
            game.DeinitAndRemoveMap(map, notifyPlayer: false);
        }
        finally
        {
            patcher.Unpatch(dispose, HarmonyPatchType.All, Id);
            removingMap = null;
        }
    }

    //只接管本次测试地图，完整生成、尚未生成及部分生成的分区都按实际资源释放。
    private static bool DisposeDrawer(Map ___map, ref Section[,] ___sections, List<MapDrawLayer> ___global)
    {
        if (___map != removingMap) return true;
        if (___sections != null)
            foreach (Section section in ___sections) section?.Dispose();
        ___sections = null;
        if (___global != null)
            foreach (MapDrawLayer layer in ___global) layer?.Dispose();
        return false;
    }

    //只移除当前工具自己的补丁，不影响其他诊断或模组。
    internal static void Remove()
    {
        if (capture != null) patcher.Unpatch(capture, HarmonyPatchType.All, Id);
        if (save != null) patcher.Unpatch(save, HarmonyPatchType.All, Id);
        capture = save = null;
    }
}
