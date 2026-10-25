# 颜色、透明混合与 Bloom

[简体中文](ColorAndBloom.md) | [English](../en/ColorAndBloom.md)

## 颜色与材质

导出器读取 Unity 工程的 Gamma／Linear 设置，并保留材质颜色、Alpha、纹理、混合模式和绘制状态。制作时将噪声、遮罩等数值贴图设为 Linear，将颜色贴图按实际用途设置为 sRGB。

脚本使用 `Material.SetTexture` 更换纹理时会同步颜色编码。通过 `MaterialPropertyBlock` 换用颜色编码不同的纹理时，需要同时设置 `_RimUnitySrgb<纹理属性名>`：sRGB 为 1，数值纹理为 0。

向颜色属性调用 `SetVector` 时仍应提供导出色值；该调用不用于绕过颜色空间转换。

“渲染设置 → 启用地图 HDR 接管”默认关闭，旧设置文件缺少该项时也保持关闭。关闭时地图沿用原版绘制，选中图层仍可通过独立浮点光缓冲产生 Bloom。启用后，Linear 工程导出的地图材质使用线性 HDR 混合，保留高于 1 的亮度直到最终合成。开关从下一次地图帧捕获生效，不需要重启或重新导出资源；地图 HDR 与 Bloom 分别控制，显式启用的离屏预览不受此地图开关影响。

地图绘制仍遵守原有材质队列、深度与模板测试。与游戏原生 Gamma 材质交错时，运行时在连续绘制段之间转换颜色空间；该过程会增加浮点缓冲显存和全屏绘制开销，实际开销取决于分辨率及绘制顺序。

## 启用 Bloom

在“选项 → 模组设置 → RimUnity”中启用 Bloom，勾选参与的 Layer（0～31），再调整阈值、强度、柔和度与扩散。

默认关闭且不选层，阈值为 0.8，强度为 0.6。未选层的物体不会成为泛光源，光晕仍会扩散到周围像素。过高强度会使画面发白。普通地表通常使用 0 层，不建议勾选。材质颜色与透明度不受 Bloom 开关控制。

## 设置 Layer

在 Unity 中为特效对象及需要泛光的子节点设置专用 Layer，再正常导出。运行时可设置 `gameObject.layer`；修改根对象不会自动改变子节点。

Layer 使用编号，不按名称匹配。不同 Unity 工程和内容模组应协调编号。

## 接入内容模组设置

在地图初始化和设置变化时，从游戏主线程调用：

```csharp
RimUnity.RimUnityMod.Bloom.SetLayerEnabled(29, myBloomEnabled);
```

29 仅为示例。该调用使内容模组的开关控制对应层，优先于 RimUnity 的手动选择。关闭时，该层也不会被手动勾选重新开启。

结束游戏生命周期时撤销控制：

```csharp
RimUnity.RimUnityMod.Bloom.ReleaseLayer(29);
```

同一层应由一个内容模组负责。阈值、强度等参数共用 RimUnity 设置。层控制状态不写入 RimUnity 配置，内容模组应在读档或新游戏时同步自己的保存值。

## 宿主网格与离屏预览

使用标准导出的 Shader 绘制游戏网格时，可通过 `RimUnity.Assets.HostAssets.ToHost` 获得共享底层资源的宿主 Shader 或 Texture2D；宿主材质构造时会保留该 Shader 的导出默认参数。普通 `UnityEngine.Graphics.DrawMesh` 指定参与 Bloom 的层即可。宿主相机即时捕获使用 `HostAssets.DrawMeshNow`，该入口显式保留 Layer 和 MaterialPropertyBlock。

需要辉光的 Pawn 外观预览可持有 `RimUnity.Rendering.BloomPreview`。相机渲染前调用 `Begin(target, layerMask)`，完成后调用 `CheckError()`，在 `finally` 中调用 `End()`，窗口关闭时 `Dispose()`。只接管该次会话指定的 RenderTexture，复用地图的亮部提取、模糊和合成以及全局 Bloom 数值设置。

预览目标必须使用完整视口与不透明背景。Layer 掩码由预览调用方显式指定。普通头像和未开启会话的离屏相机沿用游戏原来的绘制流程，不自动添加 Bloom。

## 使用限制

- Bloom 适用于地图主通道中的粒子、拖尾和模型，以及显式启用的宿主 Pawn 离屏预览；UI 和世界地图不在该范围内。
- 不支持 Unity Camera／Volume 完整后处理、曝光、色调映射和镜头污渍。
- 线性 HDR 混合适用于带 RimUnity Linear 标记的地图 Shader；显式预览使用相同 HDR 绘制器，其余 UI、世界地图和离屏相机保持原有输出路径。
- 中途场景抓取仍受游戏场景贴图的精度和显示范围限制。最终屏幕也有亮度上限；未接入 Unity 的曝光和色调映射时，不保证与启用这些效果的 Unity 相机一致。
- 泛光沿用地图深度遮挡和材质深度测试。依赖中途 Stencil 写入或特殊多 Pass 顺序的材质可能有显示差异。
