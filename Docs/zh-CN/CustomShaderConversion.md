# 自定义 Shader 导出

[简体中文](CustomShaderConversion.md) | [English](../en/CustomShaderConversion.md)

## 在 Unity 中导出

1. 在 RimUnity 导出窗口中设置 RimWorld 安装目录与 `RimUnity.ShaderTool.exe` 路径。
2. 将材质用于 Prefab，或作为独立资源导出。
3. 地图材质使用默认配置；用于 IMGUI 的独立材质设置 `ExportRequest.ui = true`。
4. 随内容模组发布清单及全部 Shader、材质和纹理依赖。

导出器自动收集材质参数、纹理与绘制状态，无需逐个手写 ShaderDef。自定义 Shader 需要项目中可读取的源码。

## Shader 编写范围

使用显式顶点／片元程序，可以编写 UV 动画、遮罩、溶解、顶点色、时间动画和纹理采样。

- 属性：Float、Range、Int／Integer、Color、Vector、纹理。
- 状态：Blend、BlendOp、Cull、ZTest、ZWrite、ZClip、AlphaToMask、ColorMask、Offset、Stencil、Tags。
- 关键字：shader_feature／multi_compile 的普通、local 与 fragment 组，同组只选择一个关键字。
- 常用变换：UnityObjectToClipPos、TransformObjectToHClip、TransformObjectToWorld、TransformWorldToHClip、TRANSFORM_TEX。
- UI：UnityGet2DClipping。
- 采样：tex2D／lod／bias／proj／grad，以及 URP 的 TEXTURE2D、SAMPLER、SAMPLE_TEXTURE2D／LOD／BIAS。

可使用相对路径 include，以及 UnityCG、UnityShaderVariables、UnityUI 和部分 URP Core／SpaceTransforms 接口；不支持完整 Unity 渲染管线库。

世界材质可导出受支持的多个颜色 Pass。Surface 支持转换器接受的 Unlit、Standard 与三参数自定义 Lighting 写法；内置 `Standard`、`Particles/Standard Surface` 使用随导出器提供的表面程序。支持范围不等同完整 Unity 渲染管线，其他不支持的语法、宏或布局会在导出时报告具体位置。

UI 顶点使用屏幕坐标，不提供对象模型矩阵。世界 Shader 在片元阶段需要的对象或相机计算结果应通过顶点 varying 传递。

## 离屏预制体与阴影

- Prefab 中带 `targetTexture` 的 Camera 可导出到 RimUnity，由宿主离屏绘制服务生成 RenderTexture，再供预制体材质采样。目前要求二维颜色附件、SolidColor 清屏，不支持 Mip 与随机写入。
- 单采样纹理按原尺寸绘制；4× MSAA 资源以宽高各两倍的超采样附件绘制，再线性缩小到原尺寸。这不是硬件 MSAA，显存占用和边缘结果可能不同。
- 离屏预制体可使用同一根对象中的一个 Directional Light；地图表面材质使用地图日月光照。
- MeshRenderer 可保留投影与接收阴影设置。ShadowCaster 导出保留受支持的顶点与裁切程序，Surface 材质使用 RimUnity 的方向光深度图接收阴影。不支持双面投影；阴影不会自动投射到未接入该采样程序的原版地图材质上。
- 这些能力已用于渡鸦的水流、离屏魔方及四种剑特效导出。导出与静态引用校验不代表已经验证游戏内画面，也不保证与 Unity 原场景逐像素一致。

## 参数使用

脚本使用原材质的属性名，例如 `_MainTex`、`_Color`。纹理变换通过 `SetTextureScale`、`SetTextureOffset` 设置，或以 `(scale.x, scale.y, offset.x, offset.y)` 写入 `_MainTex_ST`。

全局 `float4` 数组可以与普通向量合并声明，例如 `float4 _Color, _Hits[4];`。每次绘制的数组参数使用 `MaterialPropertyBlock.SetVectorArray`；设置、复制及绘制提交都保留独立数据，复用属性块不会覆盖已排队实例的受击参数。

时间动画可使用 `_Time`、`_SinTime`、`_CosTime` 和 `unity_DeltaTime`。颜色设置见 [颜色与 Bloom](ColorAndBloom.md)。

## 命令行

需要单独转换 Shader 文件时：

```powershell
& .\Tools\bin\ShaderTool\RimUnity.ShaderTool.exe '<RimWorld目录>' shaderlab '<源文件.shader>' '<输出目录>' world 'KEYWORD_A,KEYWORD_B'
```

界面程序将 `world` 改为 `ui`。输出包含 `shader.json`、`shader.vert.glsl`、`shader.frag.glsl` 和用于定位错误的 `shader.hlsl`。游戏资源应通过 Unity 导出器生成完整清单。

## 导出错误

根据诊断检查 Shader 源码、include 路径、关键字组合、材质状态和目标游戏目录。转换失败不会发布该批次的成功清单。不要用缺失 Shader 的材质替代失败资源。
