using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RimWorld;
using Verse;

namespace RimUnity.Diagnostics;

//报告保留场景条件与逐帧数据，便于比较不同特效、密度及框架版本。
internal sealed partial class LabSampler
{
    private const string Caveats = "帧间隔包含游戏、其他模组、测试窗口、限帧和等待；不是 GPU 时间。第一条间隔从按钮启动时计时，可能不足完整一帧。托管分配与内存是进程级，包含采样自身开销；未测量显存。提交项不是驱动实际 DrawCall。实例数受并发上限、寿命、每帧 16 个与 8ms 发射预算约束；单次实例化不可中断，预算不是帧耗时保证。达到预算后不追赶积欠发射，实际发射数和限流帧另行记录。资源缓存继续共享；索引扫描不等于冷加载，首次 GPU 上传可能已发生。详细探针开启时会额外影响计时。";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    //分位数只在停止采样后计算，避免每帧排序干扰测量。
    private string Summary()
    {
        if (count == 0) return "没有帧样本：" + stopReason;
        var values = samples.Take(count).Select(s => s.IntervalMs).OrderBy(x => x).ToArray();
        double mean = values.Average();
        double Percentile(double p) => values[Math.Clamp((int)Math.Ceiling(count * p) - 1, 0, count - 1)];
        long bytes = 0, beforeBatch = 0, afterBatch = 0; int g0 = 0, g1 = 0, g2 = 0;
        for (int i = 0; i < count; i++)
        {
            bytes += samples[i].AllocatedBytes; g0 += samples[i].Gen0; g1 += samples[i].Gen1; g2 += samples[i].Gen2;
            beforeBatch += samples[i].ParticleDrawsBeforeBatch; afterBatch += samples[i].ParticleDrawsAfterBatch;
        }
        var last = samples[count - 1];
        return $"{stopReason} · {Seconds:F2} 秒 · {count} 帧\n" +
            $"平均 {1000 / Math.Max(.001, mean):F1} FPS / {mean:F2} ms\n" +
            $"P50 {Percentile(.5):F2} · P95 {Percentile(.95):F2} · P99 {Percentile(.99):F2} · 最大 {values[^1]:F2} ms\n" +
            $">33.3ms：{values.Count(v => v > 33.333)} 帧；>100ms：{values.Count(v => v > 100)} 帧\n" +
            $"托管分配 {bytes / 1048576d:F2} MiB（{bytes / Math.Max(.001, Seconds) / 1048576d:F2} MiB/s）\n" +
            $"GC：{g0}/{g1}/{g2}；播放 {last.Spawned} 次（{last.Spawned / Math.Max(.001, Seconds):F1}/秒）；限流 {last.ThrottledFrames} 帧\n" +
            $"粒子绘制提交累计：{beforeBatch} → {afterBatch}，减少 {(beforeBatch - afterBatch) * 100d / Math.Max(1, beforeBatch):F1}%（非 GPU 实测 DrawCall）\n" +
            $"工作集起止：{samples[0].WorkingSetBytes / 1048576d:F1} → {last.WorkingSetBytes / 1048576d:F1} MiB";
    }

    //支持显式导出、到时自动导出和退出时导出；失败保留内存样本供再次导出。
    internal void Export()
    {
        if (Sampling) { Stop("手动停止并导出", export: true); return; }
        if (count == 0) { lab.Status = "没有可导出的样本，请先开始采样。"; return; }
        try
        {
            string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimUnityReports");
            Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, "effect-lab-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            var encoding = new UTF8Encoding(false);
            string config = JsonSerializer.Serialize(configuration, Json);
            File.WriteAllText(prefix + ".txt", "RimUnity 特效性能测试场 / schema 3\n开始 UTC：" + startedUtc +
                "\n\n" + Summary() + "\n\n测试条件\n" + config + "\n\n实例创建耗时（包含加载、对象构建及激活；不含销毁和随后帧中的粒子预热，二者仍计入整帧）\n" +
                string.Join("\n", spawnCosts.OrderByDescending(p => p.Value.MaxMs).Select(p =>
                    $"{p.Key} | 次数 {p.Value.Count} | 首次 {p.Value.FirstMs:F3}ms | 平均 {p.Value.TotalMs / p.Value.Count:F3}ms | 最大 {p.Value.MaxMs:F3}ms | 分配 {p.Value.AllocatedBytes}B")) +
                "\n\n测量边界\n" + Caveats, encoding);
            File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new
            {
                Schema = 3, StartedUtc = startedUtc, StopReason = stopReason, Configuration = configuration,
                Summary = Summary(), Caveats, SpawnCosts = spawnCosts, Frames = samples.Take(count).ToArray()
            }, Json), encoding);
            using (var csv = new StreamWriter(prefix + ".frames.csv", false, encoding))
            {
                csv.WriteLine("frame,elapsed_ms,interval_ms,allocated_bytes,gen0,gen1,gen2,instances,systems,particles,submitted_items,managed_bytes,working_set_bytes,private_bytes,spawned,throttled_frames,detailed_probes,particle_draws_before_batch,particle_draws_after_batch");
                for (int i = 0; i < count; i++)
                {
                    var s = samples[i];
                    csv.WriteLine(FormattableString.Invariant($"{s.Frame},{s.ElapsedMs:F3},{s.IntervalMs:F3},{s.AllocatedBytes},{s.Gen0},{s.Gen1},{s.Gen2},{s.Instances},{s.Systems},{s.Particles},{s.SubmittedItems},{s.ManagedBytes},{s.WorkingSetBytes},{s.PrivateBytes},{s.Spawned},{s.ThrottledFrames},{s.DetailedProbes},{s.ParticleDrawsBeforeBatch},{s.ParticleDrawsAfterBatch}"));
                }
            }
            lastReport = prefix + ".txt";
            lab.Status = "报告已导出：" + lastReport;
            Log.Message("[RimUnity] " + lab.Status);
            Messages.Message("特效测试报告已保存（TXT、JSON、逐帧 CSV）。", MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception ex)
        {
            lab.Status = "报告导出失败，可再次导出：" + ex.Message;
            Log.Error("[RimUnity] " + lab.Status + "\n" + ex);
        }
    }
}
