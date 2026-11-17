# 地图颜色契约

原生地图 Shader 输出显示颜色数值，原生 `PostFxComposite.frag.glsl` 对场景采样后直接做饱和度处理。RimUnity 在接管地图时必须保留这套数值语义。

不要用 `VkFormat` 猜测内容是显示颜色还是线性颜色。sRGB 附件的写入编码与后续采样解码是一对存储转换；浮点或 10 位格式本身也不赋予数据线性语义。

## Legacy（整幅场景接管）

- `HdrDrawTarget.Begin`：保留的原生场景按显示颜色进入；显式清零的 Bloom 光缓冲从线性零开始。
- 原生绘制段在显示颜色空间混合，声明为 Linear 的 Shader 使用独立线性程序；跨段时转换 RGB，保持 Alpha。
- `HdrDrawTarget.Result` 明确是线性 HDR。
- `WriteScene` 将线性 HDR 编码为原生场景预期的显示颜色数值，再交由附件完成自己的存储转换。
- 没有实际 Bloom 和屏幕特效时沿用原生最终合成。
- 有 Bloom 时在线性空间加光，最终显示编码一次；ScreenEfx 的输入语义由是否传入明确的线性 HDR 图像决定。

## Experiment（光缓冲）

- 原生场景始终留在宿主附件中，由宿主按原版路径绘制；Experiment **不发布** `LinearScene`。
- 仅真正的加法线性材质（`DstColor = One`）进入独立的 `R16G16B16A16SFloat` 光缓冲；Alpha 覆盖、乘法、不透明等回落给宿主，仍用显示空间原 Shader。
- 每一段 `RecordSlice`：宿主先画光之前的外壳 → 光合成 → 再画光之后的外壳（夜晚/迷雾等，只画一次）。避免光逃出压暗，也避免遮罩重复叠加导致极暗。
- 补画固定使用宿主 `LoadAttachment`，保留已有颜色和深度；缺少此附件时回落完整原生绘制。光合成后使宿主绑定缓存失效，包括没有后续外壳的切片。合成 framebuffer 按帧槽和颜色视图复用，直到目标安全重建或销毁才释放。
- 合成式为 `encode(decode(snapshot) * (1 - light.a) + light.rgb)`（加法项的 `a` 保持 0）；无光像素 `discard`，宿主原值保持不变。
- Bloom / ScreenEfx 在 Experiment 下走既有的非 HDR 路径（显示空间场景采样）。
- 不把 Alpha 覆盖收进光缓冲：重叠的软光晕若先在离屏缓冲里把 Alpha 叠满，再写回夜晚场景，会把整块区域抬亮（例如涅拉控制中心）。

### 已知近似

同一队列段内，以第一项加法光效为边界：先画此前的原生物体，再合成该段全部加法光效，最后画其余原生物体。因此加法光效与原生半透明物体的交错排序仍是近似；夜晚遮罩、战争迷雾、天气等若落在更高队列段，前后关系仍与原版一致。Alpha 覆盖特效（烟、软光晕）留在宿主。

## 通用

这些规则适用于所有遵守导出颜色空间声明的资源，不包含资源名白名单或特判。新 Shader 仍需正确声明颜色空间，且应避免输出无穷或 NaN。

设置入口：`BloomSettings.mapHdrMode`（`None` / `Legacy` / `Experiment`），默认 `None`。

验证入口：`Tools/SceneColorTests` 的隐藏窗口 Vulkan 像素回归，以及 `Tools/ScreenEfxTests` 的帧与生命周期回归。前者覆盖 8 位、sRGB、10 位及浮点附件，旧逻辑额外编码负例，以及 Experiment 加法光合成、空光像素稳定性，和 Alpha 覆盖回落宿主。

规范依据：[Vulkan 图像格式的 Texel Decode/Encode](https://docs.vulkan.org/spec/latest/chapters/images.html)。GPU 回归不替代地图透明排序、读档、窗口缩放和其他显卡驱动的实机验收。

`Tools/HdrLightLifecycleTests` 直接链接生产钩子和光缓冲代码，以记录式宿主边界验证清屏次数、夜晚遮罩只画一次、跨切片状态恢复及 framebuffer 生命周期；不依赖游戏或 GPU，也不替代像素回归。
