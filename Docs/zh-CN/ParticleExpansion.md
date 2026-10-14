# 粒子模块与使用限制

[简体中文](ParticleExpansion.md) | [English](../en/ParticleExpansion.md)

桥接层提供自有模拟与简化物理实现，不等同完整 Unity／PhysX。

## 支持模块

- 3D 粒子碰撞：平面、球、盒、胶囊、静态非凸网格、动态凸网格；扫掠半径、反弹、阻尼、寿命损失、速度阈值、碰撞消息、碰撞子发射器、对刚体施加冲量。
- 2D 粒子碰撞：圆、盒、胶囊、多边形与开放折线；3D/2D 查询相互隔离。Trigger 支持 Enter/Exit/Inside/Outside、Kill/Callback、查询碰撞对象和回写粒子。
- 简单刚体：质量、重力、速度、阻力、力和冲量、约束、静态/运动学对象、基础接触摩擦与恢复系数。不支持完整 PhysX/Box2D 行为。
- 粒子 Lights：将 Point 模板的颜色、强度、范围、比例和数量接到地图 GlowGrid；影响地图格子照明。刷新频率 10 Hz，最大范围 40 格。
- External Forces：ForceField、WindZone、显式引用/Layer 筛选和可读三维浮点向量纹理；Noise Remap 与质量设置。随机场与噪声算法不保证逐粒子等同 Unity。
- Shape：纹理颜色/Alpha/裁切、网格生成模式、MeshRenderer/SpriteRenderer/SkinnedMeshRenderer；骨骼发射位置按当前骨骼姿态采样。
- 生命周期：Lifetime By Emitter Speed、Flip Rotation、Stop Callback、Manual 子发射器和 Ring Buffer。自然到期保留不会吞掉 Collision/Trigger 的 Kill。
- 渲染：多网格粒子及随机权重、MeshIndex 图集行、UV3/UV4/VertexID/ParticleIndex/MeshIndex 顶点流，跨纹理 Sprite 帧按透明排序连续拆批。
- 拖尾：Ribbon、独立带数量、按子发射器分带。
- Built-in Standard Unlit 粒子：常见混合模式、颜色混合、图集帧混合、自发光和 Camera Fading；减法混合通过桥接材质管线适配。
- Built-in Standard Surface 粒子：支持本次水流资源使用的表面受光、法线、金属度、自发光与透明混合；不支持该材质的 Distortion、Soft Particles、Camera Fading、Flipbook 与其他颜色模式。

资源仍按 Prefab/材质/依赖 JSON 与 GLSL 导出。内容作者不需要逐个手写 ShaderDef；内置映射的 ShaderDef 随桥接模组提供，自定义 Shader 随清单注册。

## 使用限制

- 粒子 Lights 模块当前是地图格子照明，不是三维逐像素点光源；Spot/Directional/Cookie/阴影模板明确拒绝。预制体独立方向光与表面材质的支持范围见 [Shader 导出](CustomShaderConversion.md)。
- 碰撞只针对导入或脚本创建的桥接碰撞体。不会自动把 RimWorld 的墙、地形格子、角色变成 Collider，也不会推动原版 Pawn/Thing。
- 刚体为离散小步和单接触点求解；没有关节、CCD、插值或完整稳定堆叠。Medium/Low 粒子碰撞复用高精度查询，没有 Unity 体素缓存优化。
- Unity 全局 Layer 碰撞矩阵未导入；粒子模块 collidesWith 已筛选。2D 自动质量、Effector、Composite 和 Edge 虚拟相邻端点不支持；带孔多边形之间的刚体接触受限。
- TerrainCollider 使用实际高度/孔洞和固定三角划分，可能与 Unity 原生地形三角划分存在接触差异；不作为封闭 Trigger 体积。
- 不支持完整 Unity 光照与阴影管线。Surface、ShadowCaster 和其他自定义程序的要求见 [Shader 导出](CustomShaderConversion.md)。
- 跨纹理 Sprite 的帧间混合、已打包旋转 SpriteAtlas 仍有限制。支持 TrailRenderer 与 Legacy Animation；不支持完整拖尾独立顶点流、Renderer 参数及 NativeArray/Job/PlaybackState API。
- 目前仍有 TEXCOORD0～2 共 12 个浮点数的自定义流限制；不支持 GPU Instancing 与原生粒子 Jobs。

## 使用

在内容模组中加载和实例化粒子，调用方式见 [粒子快速开始](ParticleQuickStart.md)。
