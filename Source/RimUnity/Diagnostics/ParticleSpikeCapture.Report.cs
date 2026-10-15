using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RimWorld;
using Verse;

namespace RimUnity.Diagnostics;

internal static partial class ParticleSpikeCapture
{
    private static readonly JsonSerializerOptions ReportJson = new() { IncludeFields = true, WriteIndented = true };
    private static string N(double n) => n.ToString("F3", CultureInfo.InvariantCulture);
    private static string Redact(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return text;
        foreach (string path in new[] { home, home.Replace('\\', '/'), JsonSerializer.Serialize(home).Trim('"'), JsonSerializer.Serialize(home.Replace('\\', '/')).Trim('"') }.Distinct())
            text = text.Replace(path, "<USER>", StringComparison.OrdinalIgnoreCase);
        return text;
    }
    private static void Export()
    {
        try
        {
            var snapshot = last.Read();
            string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimUnityReports"); Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, "particle-spike-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            var encoding = new UTF8Encoding(false);
            File.WriteAllText(prefix + ".txt", Redact(BuildReport(snapshot)), encoding);
            File.WriteAllText(prefix + ".json", Redact(JsonSerializer.Serialize(new
            {
                Schema = 1, StartedUtc = startedUtc, StopReason = reason, StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                Before = before, After = after, Coverage = coverage.ToArray(), Data = snapshot,
                Caveats = "CPU wall time, not GPU timestamp queries. Nested inclusive times overlap. First means first observed in this capture. Frame is last main-thread bridge frame observed, not a render submission ID. Allocation counts include instrumentation."
            }, ReportJson)), encoding);
            using (var csv = new StreamWriter(prefix + ".events.csv", false, encoding))
            {
                csv.WriteLine("id,parent,thread,bridge_frame,start_ms,duration_ms,self_ms,allocated_bytes,stage,detail,outcome,notes");
                foreach (var e in snapshot.Events)
                    csv.WriteLine(string.Join(",", e.Id, e.Parent, e.Thread, e.Frame, N(e.StartMs), N(e.DurationMs), N(e.SelfMs), e.AllocatedBytes,
                        Csv(e.Stage), Csv(Redact(e.Detail)), Csv(Redact(e.Outcome)), Csv(Redact(e.Notes))));
            }
            using (var csv = new StreamWriter(prefix + ".frames.csv", false, encoding))
            {
                csv.WriteLine("bridge_frame,at_ms,interval_ms,process_allocated_bytes,gen0,gen1,gen2,active_systems,particles_first_512_systems,long_event");
                foreach (var f in snapshot.Frames)
                    csv.WriteLine(string.Join(",", f.Number, N(f.AtMs), N(f.IntervalMs), f.AllocatedBytes, f.Gen0, f.Gen1, f.Gen2, f.Systems, f.Particles, f.LongEvent));
            }
            reportPath = prefix + ".txt";
            string message = "卡顿诊断已保存：" + reportPath + "（同时生成 JSON、事件 CSV、帧 CSV）。";
            try { UnityEngine.GUIUtility.systemCopyBuffer = reportPath; message += "报告路径已复制。"; }
            catch { message += "剪贴板不可用，请从日志取得路径。"; }
            Log.Message("[RimUnity] " + message);
            Messages.Message(message, MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception error) { Log.Error("[RimUnity] 卡顿报告导出失败，采集数据仍在内存，可再次导出：" + error); }
    }
    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static string BuildReport(SpikeCaptureData.Snapshot s)
    {
        var b = new StringBuilder(65536);
        b.AppendLine("RimUnity 首次特效卡顿诊断 / schema 1");
        b.AppendLine("Start UTC=" + startedUtc + "; stop=" + reason);
        b.AppendLine("\n读法与测量边界");
        b.AppendLine("1. First/Second/Third 是本次采集的前几次调用，是否冷加载请看 Asset.Get cache=miss、采集前资源缓存与 compilerCacheHit。");
        b.AppendLine("2. duration 是 CPU 墙钟耗时；self 扣除同线程、同会话内已测量子调用，仍包含未布点工作和诊断开销。不可相加嵌套 duration，也不可跨线程求和推算帧时长。");
        b.AppendLine("3. Host.RecordFrame、GPU.* 均是 CPU 端记录/创建/上传调用耗时，不是 GPU 执行时间；没有 GPU timestamp 或呈现延迟测量。");
        b.AppendLine("4. Frame interval 是相邻主循环 OnUpdate 回调间隔，包含游戏更新、等待、限帧、调试菜单等。事件 frame 是最近观察到的桥接帧号；渲染线程请按绝对相对时间关联。");
        b.AppendLine("5. 分配量是当前线程的 inclusive 托管分配，包含探针/嵌套记录成本；帧分配量是进程累计分配增量。原生/GPU 分配未计入，GC 次数是进程级，不能单凭同时发生认定因果。");
        b.AppendLine("6. 探针安装和状态快照在计时之外，但 Harmony 安装/JIT 本身会影响冷启动条件；不预加载资源、不主动生成或重播特效、不清空缓存、不强制 GC、不等待设备空闲。");
        b.AppendLine("7. 每个 stage+detail 统计全部已完成调用，保留最早 3 次、>=2ms 的慢调用和异常；其余普通调用只有聚合记录。子事件可能未保留，parent ID 不保证在事件表中。");
        b.AppendLine("8. Capture 前/后资源与活动系统配置是状态证据；冷加载是否真正触发还要看事件。打开菜单/切换窗口的帧也可能变慢，触发前可空等几秒取得基线。");
        b.AppendLine("9. Host.MainFrame/Host.RenderFrame 使用宿主事件、是 detached 外层时间段，其 self 等于 inclusive，不扣除其中独立记录的子阶段。停止时的 partial main frame 不能作为完整帧比较。");
        b.AppendLine("\n采集完整性");
        b.AppendLine("groups=" + s.Groups.Length + "/" + SpikeCaptureData.MaxGroups + "; events=" + s.Events.Length + "/" + SpikeCaptureData.MaxEvents
            + "; frames=" + s.Frames.Length + "/" + SpikeCaptureData.MaxFrames + "; closed=" + s.Closed);
        b.AppendLine("omittedEvents=" + s.OmittedEvents + "; omittedGroupCalls=" + s.OmittedGroups + "; omittedFrames=" + s.OmittedFrames
            + "; inFlightAtExport=" + s.InFlight + "; probeErrors=" + s.ProbeErrors);
        if (s.OmittedEvents + s.OmittedGroups + s.OmittedFrames + s.InFlight + s.ProbeErrors > 0)
            b.AppendLine("WARNING: 有截断、未完成调用或探针错误；不能把缺失事件解释为无开销。停止时仍在运行的调用不纳入统计。");
        if (s.Groups.Length == 0) b.AppendLine("NO OBSERVED CALLS：采集期间未观察到布点入口，不能据此判定特效正常或没有卡顿。");
        b.AppendLine("\n主循环帧间隔");
        double[] intervals = s.Frames.Select(f => f.IntervalMs).OrderBy(x => x).ToArray();
        if (intervals.Length > 0)
            b.AppendLine("count=" + intervals.Length + "; p50=" + N(Percentile(intervals, .5)) + "ms; p95=" + N(Percentile(intervals, .95))
                + "ms; p99=" + N(Percentile(intervals, .99)) + "ms; max=" + N(intervals[^1]) + "ms; >33ms=" + intervals.Count(x => x > 33.333) + "; >100ms=" + intervals.Count(x => x > 100));
        else b.AppendLine("NO FRAME SAMPLES");
        b.AppendLine("\n最慢 20 个帧间隔及重叠事件（每帧最多列 12 项，按耗时排序）");
        foreach (var frame in s.Frames.OrderByDescending(f => f.IntervalMs).Take(20))
        {
            b.AppendLine("FRAME " + frame.Number + " at=" + N(frame.AtMs) + "ms interval=" + N(frame.IntervalMs) + "ms allocated=" + frame.AllocatedBytes
                + " GC=" + frame.Gen0 + "/" + frame.Gen1 + "/" + frame.Gen2 + " systems=" + frame.Systems + " particles(<=512)=" + frame.Particles + " longEvent=" + frame.LongEvent);
            foreach (var e in s.Events.Where(e => e.Stage != "MARK" && e.StartMs <= frame.AtMs && e.StartMs + e.DurationMs >= frame.AtMs - frame.IntervalMs)
                         .OrderByDescending(e => e.DurationMs).Take(12)) b.AppendLine("  " + EventLine(e));
        }
        b.AppendLine("\n阶段汇总（同一 stage 下的 duration 可能嵌套；按 self 总时间排序）");
        foreach (var stage in s.Groups.GroupBy(g => g.Stage).OrderByDescending(g => g.Sum(x => x.SelfMs)))
            b.AppendLine(stage.Key + " calls=" + stage.Sum(g => g.Calls) + "; inclusive=" + N(stage.Sum(g => g.TotalMs))
                + "ms; self=" + N(stage.Sum(g => g.SelfMs)) + "ms; max=" + N(stage.Max(g => g.MaxMs)) + "ms; errors=" + stage.Sum(g => g.Errors));
        b.AppendLine("\n逐资源/实例：首次与后续调用（按单次峰值排序）");
        foreach (var g in s.Groups.OrderByDescending(g => g.MaxMs))
        {
            b.AppendLine(g.Stage + " | " + g.Detail + " | calls=" + g.Calls + " total=" + N(g.TotalMs) + "ms self=" + N(g.SelfMs)
                + "ms max=" + N(g.MaxMs) + "ms allocated=" + g.AllocatedBytes + " errors=" + g.Errors);
            foreach (var e in g.First) b.AppendLine("  " + EventLine(e));
        }
        b.AppendLine("\n用户标记与脚本异常");
        foreach (var e in s.Events.Where(e => e.Stage == "MARK")) b.AppendLine(EventLine(e));
        b.AppendLine("\n探针覆盖（OK 仅表示安装成功，不保证本次调用经过该入口；MISSING/FAILED 必须保留）");
        foreach (string line in coverage) b.AppendLine(line);
        b.AppendLine("\n采集前状态\n" + before + "\n采集后状态\n" + after);
        b.AppendLine("\n完整保留事件见 .events.csv / .json；所有帧间隔见 .frames.csv。文本报告不收集存档内容、Shader 源码或贴图像素。");
        return b.ToString();
    }
    private static double Percentile(double[] sorted, double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
    private static string EventLine(SpikeCaptureData.Event e) => "#" + e.Id + " parent=" + e.Parent + " thread=" + e.Thread + " frame=" + e.Frame
        + " at=" + N(e.StartMs) + "ms duration=" + N(e.DurationMs) + "ms self=" + N(e.SelfMs) + "ms bytes=" + e.AllocatedBytes
        + " | " + e.Stage + " | " + e.Detail + " | " + e.Outcome + " | " + e.Notes;
}
