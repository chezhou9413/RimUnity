using System;
using Verse;
using URect = UnityEngine.Rect;

namespace RimUnity.Diagnostics;

//参数先在面板中编辑，应用时开启独立测试，避免一次报告混入不同负载。
internal sealed partial class LabWindow
{
    private float concurrency = 20, rate = 5, lifetime = 5, radius = 8, scale = 1, rotationX, seconds = 60;
    private float warmup = 10;
    private bool fixedInstances = true;

    //并发数、频率与寿命共同决定实际密度，过载限流会在报告中明确记录。
    private void DrawControls(float width, float row, ref float y)
    {
        if (DebugSettings.ShowDevGizmos)
        {
            const string preset = "一键建立 / 重建混合粒子压力场景";
            float buttonHeight = Math.Max(row, Text.CalcHeight(preset, width - 16) + 12);
            if (Widgets.ButtonText(new URect(0, y, width, buttonHeight), preset))
            {
                lab.StartMixedStress();
                concurrency = lab.Concurrency; fixedInstances = lab.FixedInstances; warmup = lab.WarmupSeconds;
                seconds = lab.SampleSeconds; radius = lab.Radius; scale = lab.Scale; rotationX = lab.RotationX;
            }
            y += buttonHeight + 8;
            const string bloomLabel = "混合场景包含 Bloom（仅测试场生效）";
            float bloomHeight = Math.Max(row, Text.CalcHeight(bloomLabel, width - 34) + 8);
            bool bloom = lab.StressBloom;
            Widgets.CheckboxLabeled(new URect(0, y, width, bloomHeight), bloomLabel, ref bloom);
            if (bloom != lab.StressBloom) { lab.Sampler.Stop("改变测试场 Bloom", export: true); lab.StressBloom = bloom; }
            y += bloomHeight + 8;

            Label("内置混合 v2：12 组 × 4 实例，固定种子和位置，预热 12 秒后采样 60 秒。覆盖常见绘制、模拟空间、拖尾、曲线、噪声、图集、复杂顶点流及出生子发射器。粒子有固定上限，数量仍随周期及帧间隔小幅变化。", width, ref y);
        }
        Label("勾选要测试的特效后应用参数。多项特效按列表顺序分配给实例；重新开始会清空旧实例。", width, ref y);
        float modeHeight = Math.Max(row, Text.CalcHeight("固定数量常驻循环（稳定负载）", width - 34) + 8);
        Widgets.CheckboxLabeled(new URect(0, y, width, modeHeight), "固定数量常驻循环（稳定负载）", ref fixedInstances);
        y += modeHeight + 8;
        Slider(fixedInstances ? "固定实例总数" : "最大同时存在实例", ref concurrency, 1, 500, 1, width, row, ref y);
        if (fixedInstances)
        {
            Slider("填满后的预热时间（秒）", ref warmup, 0, 60, 1, width, row, ref y);
            Label("一次建立固定实例，位置与粒子随机种子可重复。将克隆中的普通发射器设为循环；子发射器保持事件驱动。粒子数量仍随特效周期变化，脚本自行禁用或销毁实例会停止测试。", width, ref y);
        }
        else
        {
            Slider("每秒播放次数", ref rate, .5f, 200, .5f, width, row, ref y);
            Slider("实例最长寿命（秒）", ref lifetime, .5f, 60, .5f, width, row, ref y);
        }
        Slider("分布半径（格）", ref radius, 0, 40, 1, width, row, ref y);
        Slider("整体尺寸倍率", ref scale, .1f, 5, .1f, width, row, ref y);
        Slider("X 轴旋转（度）", ref rotationX, -180, 180, 15, width, row, ref y);
        Label(fixedInstances ? "固定模式在实例填满并预热后自动采样；采样结束后继续播放。采样时保持相机位置和缩放不变，关闭 DPA 详细探针。"
            : "创建/销毁模式应用后立即采样，包含实例创建成本；频率还受每帧创建预算约束。", width, ref y);
        if (Widgets.ButtonText(new URect(0, y, width, row), "应用参数并播放 / 重新开始"))
        {
            lab.Sampler.Stop("应用新参数", export: true);
            lab.Concurrency = (int)concurrency; lab.Rate = rate; lab.Lifetime = lifetime;
            lab.Radius = radius; lab.Scale = scale; lab.RotationX = rotationX; lab.SampleSeconds = (int)seconds;
            lab.FixedInstances = fixedInstances; lab.WarmupSeconds = warmup;
            lab.StartPlayback(mixedStress: lab.Scenario == LabStressScenario.Version);
        }
        y += row + 8;
        if (Widgets.ButtonText(new URect(0, y, width, row), "停止播放并清空实例"))
        { lab.Sampler.Stop("停止播放", export: true); lab.StopPlayback(); }
        y += row + 10;
        Label("当前场景：" + lab.Scenario, width, ref y);
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
        bool enabled = UnityEngine.GUI.enabled;
        string modeLabel = lab.CpuMode switch { LabCpuMode.Off => "关闭（无 Bridge 计时）", LabCpuMode.BridgeOnly => "仅 Bridge 边界", _ => "分阶段" };
        float modeHeight = Math.Max(row, Text.CalcHeight("采样模式：" + modeLabel + "（点击切换）", width - 16) + 12);
        try
        {
            UnityEngine.GUI.enabled = enabled && !lab.Sampler.Sampling;
            if (Widgets.ButtonText(new URect(0, y, width, modeHeight), "采样模式：" + modeLabel + "（点击切换）"))
                lab.CpuMode = (LabCpuMode)(((int)lab.CpuMode + 1) % 3);
        }
        finally { UnityEngine.GUI.enabled = enabled; }
        y += modeHeight + 8;
        Slider("采样时长（秒）", ref seconds, 10, 300, 10, width, row, ref y);
        if (Widgets.ButtonText(new URect(0, y, width, row), lab.Sampler.Sampling ? "停止采样并导出" : "开始常规采样"))
        {
            if (lab.Sampler.Sampling) lab.Sampler.Stop("手动停止", export: true);
            else lab.Sampler.Start((int)seconds);
        }
        y += row + 8;
        if (Widgets.ButtonText(new URect(0, y, width, row), "清空特效并采样空场景"))
        {
            lab.Sampler.Stop("切换空场景基线", export: true);
            lab.StopPlayback();
            lab.Sampler.Start((int)seconds);
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
        Label("CPU 阶段只在采样期间记录，默认开启；仅 Bridge 模式可用于对照内部阶段探针；关闭模式没有 Bridge 计时。报告分别记录进程分配、主线程分配和已覆盖阶段分配。阶段时间是 CPU 调用耗时，包含宿主 API 和脚本；不是 GPU 时间。详细探针报告也保存在 RimUnityReports 目录。", width, ref y);
    }
}
