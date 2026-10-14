# 支持范围与限制

[简体中文](Status.md) | [English](../en/Status.md)

制作环境：Unity 2022.3 LTS，Built-in 或 URP 14。运行环境：Windows x64、RimWorld 1.7 Waylith、.NET 10。

## 支持范围

| 功能 | 用法与范围 |
|---|---|
| 资源 | 明文清单、Prefab 层级、共享依赖、模板实例化、脚本字段与事件引用 |
| 脚本 | RimUnity 对象树、Mono 生命周期、协程、Transform 和数学类型 |
| UI | 常用 UGUI 控件、布局、指针事件、传统 Text、TMP SDF 图集与基础裁切 |
| 界面动画与语音 | RimUnity.Tweening 顺序补间、循环和关闭取消；二维 AudioSource、本地 OGG/WAV/MP3 加载 |
| 粒子 | 发射、形状、运动模块、曲线与渐变、图集、子发射器、拖尾、自定义数据、力场 |
| 物理与照明 | 碰撞器、Trigger、简化刚体、地图格子粒子照明；见 [粒子模块](ParticleExpansion.md) |
| 模型与动画 | 静态与骨骼网格、基础 Animator 状态机、1D 混合、属性动画、动画事件、Animation、LineRenderer 与 TrailRenderer |
| 材质 | 内置材质映射、自定义 Shader 转换、Gamma／Linear 颜色处理、按 Layer 筛选的地图 Bloom |

## 使用限制

- 自定义脚本使用 RimUnity 类型并编译为游戏侧程序集；不支持第三方 Unity DLL 直接运行。
- 实例化入口为加载器返回的根模板；不支持运行实例或局部子树再次克隆、跨 Prefab 对象引用及资源热重载。
- 不支持 SerializeReference 多态对象图与任意序列化回调。
- TMP 不支持完整动态字形和复杂排版；基础 Mask 不支持任意 Stencil 运算。
- UI 导出支持 Simple / Sliced 图片遮罩、RectMask2D 羽化与自定义 UI Shader 遮罩入口；TMP 可保留普通和粗体字重、字距。Text 的 Shadow / Outline 可随预制体导出。
- AudioSource 目前限无 Mixer 路由的二维界面音频；不接受空间混合、音效滤镜或监听器绕过。补间提供当前已接通的锚点位置、X 缩放与图形透明度，不等同完整 DOTween。
- 粒子随机轨迹、缩放及部分渲染参数与 Unity 有差异；不支持完整 NativeArray／Jobs、GPU Instancing 和 PlaybackState API。
- 物理不等同 PhysX／Box2D，游戏建筑和角色不会自动成为碰撞体；地图 Lights 不提供三维实时光照或阴影。
- Animator 不支持 Humanoid、IK、完整嵌套状态机和复杂过渡中断。
- 不支持 Shader Graph、Live2D 和完整 Unity 后处理栈。Shader 的输入要求见 [自定义 Shader](CustomShaderConversion.md)，Bloom 的适用范围见 [颜色与 Bloom](ColorAndBloom.md)。
