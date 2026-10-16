using System;
using Verse;
using URect = UnityEngine.Rect;

namespace RimUnity.Diagnostics;

//参数先在面板中编辑，应用时开启独立测试，避免一次报告混入不同负载。
internal sealed partial class LabWindow
{
    private float concurrency = 20, rate = 5, lifetime = 5, radius = 8, scale = 1, rotationX, seconds = 60;

    //并发数、频率与寿命共同决定实际密度，过载限流会在报告中明确记录。
    private void DrawControls(float width, float row, ref float y)
    {
        Label("滑动相机查看场地。勾选一个特效会循环播放；勾选多个会轮流播放。修改参数后点击应用，已有实例会清空并开始新的采样。", width, ref y);
        Slider("最大同时存在实例", ref concurrency, 1, 500, 1, width, row, ref y);
        Slider("每秒播放次数", ref rate, .5f, 200, .5f, width, row, ref y);
        Slider("实例最长寿命（秒）", ref lifetime, .5f, 60, .5f, width, row, ref y);
        Slider("分布半径（格）", ref radius, 1, 40, 1, width, row, ref y);
        Slider("整体尺寸倍率", ref scale, .1f, 5, .1f, width, row, ref y);
        Slider("X 轴旋转（度）", ref rotationX, -180, 180, 15, width, row, ref y);
        Label("参数应用后开始常规采样；采样结束会自动导出，特效继续播放。频率上限还受每帧创建预算约束。", width, ref y);
        if (Widgets.ButtonText(new URect(0, y, width, row), "应用参数并播放 / 重新开始"))
        {
            lab.Sampler.Stop("应用新参数", export: true);
            lab.Concurrency = (int)concurrency; lab.Rate = rate; lab.Lifetime = lifetime;
            lab.Radius = radius; lab.Scale = scale; lab.RotationX = rotationX; lab.SampleSeconds = (int)seconds;
            lab.StartPlayback();
        }
        y += row + 8;
        if (Widgets.ButtonText(new URect(0, y, width, row), "停止播放并清空实例"))
        { lab.Sampler.Stop("停止播放", export: true); lab.StopPlayback(); }
        y += row + 10;
        Label(lab.Status, width, ref y);
        Label(lab.Sampler.Display, width, ref y);
    }

    //滑条文字单独占一行，控件本体不与中文标签重叠。
    private static void Slider(string label, ref float value, float min, float max, float step, float width, float row, ref float y)
    {
        Label(label + "：" + value.ToString("0.##"), width, ref y);
        value = Widgets.HorizontalSlider(new URect(0, y, width, row), value, min, max, middleAlignment: true, roundTo: step);
        y += row + 6;
    }

    //常规报告与详细探针独立，详细探针的额外测量成本保留在逐帧标记中。
    private void DrawReports(float width, float row, ref float y)
    {
        Label(lab.Sampler.Display, width, ref y);
        Slider("采样时长（秒）", ref seconds, 10, 300, 10, width, row, ref y);
        if (Widgets.ButtonText(new URect(0, y, width, row), lab.Sampler.Sampling ? "停止采样并导出" : "开始常规采样"))
        {
            if (lab.Sampler.Sampling) lab.Sampler.Stop("手动停止", export: true);
            else lab.Sampler.Start((int)seconds);
        }
        y += row + 8;
        if (Widgets.ButtonText(new URect(0, y, width, row), "导出性能报告（TXT / JSON / CSV）")) lab.Sampler.Export();
        y += row + 8;
        if (Widgets.ButtonText(new URect(0, y, width, row), "开启详细耗时探针（60 秒）")) lab.StartDetailedCapture();
        y += row + 8;
        if (!string.IsNullOrEmpty(lab.Sampler.LastReport))
        {
            if (Widgets.ButtonText(new URect(0, y, width, row), "复制性能报告路径"))
            {
                try { UnityEngine.GUIUtility.systemCopyBuffer = lab.Sampler.LastReport; }
                catch (Exception ex) { lab.Status = "无法访问剪贴板：" + ex.Message; }
            }
            y += row + 8;
        }
        Label(lab.Status, width, ref y);
        Label("常规采样最多保留 36000 帧，达到上限也会停止并导出。退出场景自动停止采样、移除临时探针并清理实例。测试地图不允许保存。", width, ref y);
        Label("帧时间包含整个游戏和测试面板的开销；内存与分配是进程级指标。提交项不等于 GPU DrawCall，报告不包含 GPU 时间或显存数值。详细探针报告也保存在 RimUnityReports 目录。", width, ref y);
    }
}
