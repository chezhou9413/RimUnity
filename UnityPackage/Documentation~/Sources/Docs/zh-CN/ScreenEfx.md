# Clip 屏幕后处理（1.7）

`RimUnity.ScreenEfx` 在地图与 Bloom 合成后、游戏界面前执行全屏材质。内容模组继承 `EfxClip`，提交一次后修改自己的属性；播放器负责更新、排序、参数快照、串联和资源回收。世界地图、菜单和离屏 Pawn 预览不执行这条效果链。

## 最小示例

```csharp
using RimUnity;
using RimUnity.ScreenEfx;

public sealed class InterferenceClip : EfxClip
{
    private readonly Material material;
    private readonly MaterialPropertyBlock properties = new();
    public float Strength { get; set; }

    public InterferenceClip(Material template) => material = new Material(template);

    public override void BuildFrame(EfxFrameBuilder frame)
    {
        if (Strength <= 0) return;
        properties.SetFloat("_Strength", Mathf.Clamp01(Strength));
        frame.AddPass(material, pass: 0, properties: properties);
    }

    protected override void OnRelease() => RimUnity.Object.Destroy(material);
}

//当前游戏已初始化，在主线程提交。
var clip = new InterferenceClip(template);
var handle = EfxPlayer.Play(clip);
clip.Strength = 0.6f; //下一次捕获生效，无需重新提交。
clip.Enabled = false; //暂时隐藏，仍执行 Update。
clip.Enabled = true;
handle.Stop(); //结束播放；也可 Dispose。
```

`AddPass` 另有 `UnityEngine.Material` 与 `UnityEngine.MaterialPropertyBlock` 重载，供现有模组通过 Lumi 等加载器取得的宿主材质使用。Pass 索引必须有效；一个 Clip 可以多次调用 `AddPass`，按调用顺序串联。参数覆盖支持标量、颜色、向量、矩阵与纹理；数组放在材质中，首版不支持参数块中的数组或存储缓冲覆盖。

## 更新、顺序与所有权

- 所有公共控制操作及 Clip 回调仅在游戏主线程执行。`Update(EfxUpdateContext)` 每次宿主更新执行一次；`DeltaTime`／`Time` 随游戏暂停和速度变化，`UnscaledDeltaTime`／`UnscaledTime` 使用真实帧时间。加载长事件期间不推进。
- `Enabled=false` 时仍更新，但不调用 `BuildFrame`。`Order` 升序执行，同序按提交顺序；每次捕获重新读取顺序。
- 稳定且暂时不需要观察状态的 Clip 可以设置 `UpdateEnabled=false`，跳过更新回调；业务事件必须显式设回 `true` 唤醒。默认值为 `true`，隐藏不自动休眠；需要逐帧观察地图切换的效果应继续更新。
- `Play` 要求已有游戏，绑定提交时的会话。提交后播放器拥有 Clip；同一实例不能重复提交，也不能在停止后重新播放。`Stop`／`Dispose` 幂等，返回菜单或更换游戏自动停止旧会话。
- 播放器不提供隐式淡入淡出，Clip 自行插值业务参数。地图条件由内容模组在 `Update` 中判断并更新 `Enabled`。
- `OnRelease` 只调用一次，等待相关帧所在 GPU 槽位安全退役后回到主线程执行。私有材质和专属纹理在这里销毁，不要在 `Stop` 后立即自行销毁。共享模板继续由资源加载器管理。
- `OnRelease` 可能延迟数帧；玩法事件订阅仍应验证所属会话，不应因旧 Clip 尚待释放而修改新游戏。已释放句柄不再持有 Game 或 Clip。
- Clip、句柄和材质快照不参与存档。内容模组从自己的已保存状态重建 Clip；重复初始化时保存并复用句柄。

可选覆盖 `Warmup(EfxFrameBuilder)`，在加载完成的主线程上提交将使用的材质 Pass，每个实例只捕获一次，禁用或休眠不影响此回调。框架将冻结的快照送入现有加载预热阶段，不显示滤镜、不推进业务更新；材质创建、共享三角形上传和 GPU 管线编译可在此完成。加载完成后才提交的 Clip 仍按需创建。不要在此回调修改剧情或推进渐变。

全屏三角形提供 Unity 向上的 UV，与导出器采样时的 Y 翻转配套，各 Pass 及多 Pass 串联均保持画面方向；无需在内容 Shader 再次翻转。

已预热且仍在播放的 Clip 隐藏时，安全退役全屏纹理后保留小型绘制器与管线缓存（普通双槽位缓冲约 0.93 MiB），避免首次显示才编译；最后一个 Clip 停止后释放。加载预热不分配全屏纹理，首次显示与尺寸变化仍可能产生纹理分配开销，不能保证所有驱动上首帧完全无卡顿。

## Shader 与执行约定

使用标准 **world** Shader 导出配置，不能标为 GUI。顶点入口采用 `vert_img` 或等价的裁剪空间全屏顶点处理。原来的 `OnRenderImage` 和 `Graphics.Blit` 不会自动接入播放器。

每个 Pass 的 `_MainTex` 是上一阶段画面；`_MainTex_TexelSize`、`_ScreenParams` 使用实际渲染尺寸。输入与输出采用显示颜色，框架移除导出器添加的最终 Linear 输出编码，并关闭 `_MainTex` 的额外 sRGB 解码，避免中性效果重复转换颜色。其他纹理保留正常材质语义。

默认 `_Time` 使用真实时间，暂停时仍变化。需要暂停动画时，在 `Update` 保存 `context.Time`，通过参数块显式覆盖 `_Time`；逐 Pass 的参数在 `AddPass` 时即捕获。完整材质、纹理绑定及参数块按帧冻结，之后修改不会污染已排队画面。

全屏处理器固定禁用深度、模板、剔除和混合，Shader 应采样 `_MainTex` 并输出完整画面。需要多步处理时显式提交多个 Pass；首版不包含历史帧、场景深度、Bloom 前阶段或界面后阶段。

没有效果时沿用原合成入口；可见效果使用每个在途帧两个可复用目标。停止后等待槽位退役再释放，缩放窗口走宿主安全重建。单 Clip 异常会停止该 Clip 并记录类型，不逐帧重试；其余效果和正常地图输出保留。

帧容器、处理指令和参数缓冲在最后一个 CPU／GPU 引用释放后复用，不按固定帧数猜测可用时间。帧池最多缓存 8 个容器，每个最多 64 条指令、256 KiB 参数字节；超限帧正常执行但不缓存。渲染端按在途槽位复用最多 64 条指令，槽位退役后丢弃超过 256 KiB 的参数缓存。无运行时尺寸参数的数据块直接借用冻结快照，其余使用槽位私有副本。

回收时清空 Clip、Game、网格和纹理绑定引用；更换游戏或退出时清空池并拒绝旧会话迟到的回收。预热后的设备无关测试中，空提交和复用输入绑定均无托管分配；实际绘制仍可能包含宿主材质捕获、绘制帧包装等分配，不能将此理解为整条后处理管线零 GC。

每个宿主在途槽位使用独立的单槽位绘制器，上传缓冲按本帧需求向上取整，容量不足时仅在该槽位的 GPU 工作结束后重建。普通少量 Pass 的缓冲预留为每槽位 476 KiB，双在途槽位共约 0.93 MiB；原宿主通用绘制器在双槽位下约预留 66.1 MiB。这里只统计缓冲分配请求，上传内存位于驱动选择的主机可见堆，不能等同于独占显存；描述符、驱动与 Shader 开销另计。两张 RGBA8 中间纹理仍占 `2 × 宽 × 高 × 4 × 在途槽位数` 字节，双槽位 1080p 约 31.6 MiB。未提交 Clip 时不创建这些绘制器与中间纹理；停止后等待最后一个槽位安全退役再释放。

容量调整只在创建本框架绘制器的同步线程作用域内生效，其他宿主绘制器保持原配置。宿主构造函数不兼容时停止效果并保留地图输出。诊断中的 `uploadBufferBytes` 是当前槽位缓冲预留合计，属于分配预算，不是 GPU 驻留显存测量。

## 验证

运行 `dotnet run --project Tools/ScreenEfxTests -c Release` 验证播放器与参数快照；测试使用本机 1.7 游戏引用，可通过 `RimWorldDir`／`HarmonyPath` 指定路径。无图形设备测试不能证明 GPU 画面正确。

实际验收应比较无 Clip、中性 Clip、两个 Clip 的顺序与帧耗时，并检查 Bloom 开关、暂停、地图切换、读档、窗口缩放和退出。现有渲染诊断报告中的 `ScreenEfx` 阶段提供 Pass 数、目标数、尺寸和帧槽位，便于核对资源是否回收。
