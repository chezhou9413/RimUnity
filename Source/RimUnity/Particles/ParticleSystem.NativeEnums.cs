namespace RimUnity;

//粒子枚举值与制作端导出的数值一致。
public enum ParticleSystemAnimationMode { Grid, Sprites }
public enum ParticleSystemAnimationRowMode { Custom, Random, MeshIndex }
public enum ParticleSystemAnimationTimeMode { Lifetime, Speed, FPS }
public enum ParticleSystemAnimationType { WholeSheet, SingleRow }
public enum ParticleSystemCollisionMode { Collision3D, Collision2D }
public enum ParticleSystemCollisionType { Planes, World }
public enum ParticleSystemCurveMode { Constant, Curve, TwoCurves, TwoConstants }
public enum ParticleSystemGradientMode { Color, Gradient, TwoColors, TwoGradients, RandomColor }
public enum ParticleSystemEmitterVelocityMode { Transform, Rigidbody, Custom }
public enum ParticleSystemRenderMode { Billboard, Stretch, HorizontalBillboard, VerticalBillboard, Mesh, None }
public enum ParticleSystemRenderSpace { View, World, Local, Facing, Velocity }
public enum ParticleSystemSimulationSpace { Local, World, Custom }
public enum ParticleSystemRingBufferMode { Disabled, PauseUntilReplaced, LoopUntilReplaced }
public enum ParticleSystemScalingMode { Hierarchy, Local, Shape }
public enum ParticleSystemSortMode { None, Distance, OldestInFront, YoungestInFront, Depth }
public enum ParticleSystemTrailMode { PerParticle, Ribbon }
public enum ParticleSystemTrailTextureMode { Stretch, Tile, DistributePerSegment, RepeatPerSegment, Static }
public enum ParticleSystemMeshShapeType { Vertex, Edge, Triangle }
public enum ParticleSystemShapeType
{
    Sphere, SphereShell, Hemisphere, HemisphereShell, Cone, Box, Mesh, ConeShell, ConeVolume, ConeVolumeShell,
    Circle, CircleEdge, SingleSidedEdge, MeshRenderer, SkinnedMeshRenderer, BoxShell, BoxEdge, Donut, Rectangle, Sprite, SpriteRenderer
}
public enum ParticleSystemStopAction { None, Disable, Destroy, Callback }
public enum ParticleSystemStopBehavior { StopEmittingAndClear, StopEmitting }
public enum ParticleSystemNoiseQuality { Low, Medium, High }

//顶点流值用于读写制作端导出的排列；支持情况由 Renderer 的 StreamWidth 明确校验。
public enum ParticleSystemVertexStream
{
    Position, Normal, Tangent, Color, UV, UV2, UV3, UV4, AnimBlend, AnimFrame, Center, VertexID,
    SizeX, SizeXY, SizeXYZ, Rotation, Rotation3D, RotationSpeed, RotationSpeed3D, Velocity, Speed, AgePercent, InvStartLifetime,
    StableRandomX, StableRandomXY, StableRandomXYZ, StableRandomXYZW, VaryingRandomX, VaryingRandomXY, VaryingRandomXYZ, VaryingRandomXYZW,
    Custom1X, Custom1XY, Custom1XYZ, Custom1XYZW, Custom2X, Custom2XY, Custom2XYZ, Custom2XYZW,
    NoiseSumX, NoiseSumXY, NoiseSumXYZ, NoiseImpulseX, NoiseImpulseXY, NoiseImpulseXYZ, MeshIndex, ParticleIndex,
    ColorPackedAsTwoFloats, MeshAxisOfRotation, NextTrailCenter, PreviousTrailCenter, PercentageAlongTrail, TrailWidth
}
