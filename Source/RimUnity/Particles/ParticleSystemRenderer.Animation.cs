using System;

namespace RimUnity;

//帧选择与面向规则同时供普通 Billboard、Mesh 图集和 UI 提交使用。
public sealed partial class ParticleSystemRenderer
{
    //每轮重新采样帧曲线，循环次数不能直接乘在非线性曲线的结果上。
    private Rect UV(ParticleSystem system, ParticleSystem.Particle particle)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Atlas);
#endif

        var module = system.textureSheetAnimation;
        if (!module.enabled) { streamFrame = streamBlend = 0; return streamCurrentUV = streamNextUV = new Rect(0, 0, 1, 1); }
        if (module.numTilesX <= 0 || module.numTilesY <= 0 || module.cycleCount < 0)
            throw new InvalidOperationException("粒子图集行列必须为正数，循环次数不能为负。");
        int nx = module.numTilesX, ny = module.numTilesY;
        bool sprites = module.mode == ParticleSystemAnimationMode.Sprites;
        int frames = sprites ? module.sprites.Length : module.animation == ParticleSystemAnimationType.SingleRow ? nx : checked(nx * ny);
        if (frames == 0) throw new InvalidOperationException("Sprite 图集没有配置精灵。");
        float phase;
        switch (module.timeMode)
        {
            case ParticleSystemAnimationTimeMode.Lifetime:
                float time = Mathf.Repeat(ParticleSystem.Age(particle) * module.cycleCount, 1);
                if (ParticleSystem.Age(particle) == 1 && module.cycleCount > 0) time = 1 - 1e-6f;
                phase = module.frameOverTime.Evaluate(time, ParticleSystem.Random(particle.randomSeed, 92));
                break;
            case ParticleSystemAnimationTimeMode.Speed:
                float speed = Mathf.InverseLerp(module.speedRange.x, module.speedRange.y, system.WorldVelocity(particle).magnitude);
                phase = Mathf.Repeat(speed * module.cycleCount, 1);
                if (speed == 1 && module.cycleCount > 0) phase = 1 - 1e-6f;
                break;
            case ParticleSystemAnimationTimeMode.FPS:
                phase = particle.elapsedAge * module.fps / frames;
                break;
            default: throw new NotSupportedException("粒子图集时间模式尚未支持：" + module.timeMode);
        }
        phase += module.startFrame.Evaluate(0, ParticleSystem.Random(particle.randomSeed, 91));
        if (!float.IsFinite(phase)) throw new InvalidOperationException("粒子图集选帧必须产生有限数值。");
        streamFrame = Mathf.Repeat(phase, 1) * frames;
        int frame = Math.Min(frames - 1, Mathf.FloorToInt(streamFrame));
        streamBlend = streamFrame - frame;
        if (sprites)
        {
            Rect SpriteUV(int index) { Sprite sprite = module.sprites[index]; Rect r = sprite.rect; return new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height, r.width / sprite.texture.width, r.height / sprite.texture.height); }
            streamNextUV = SpriteUV((frame + 1) % frames);
            return streamCurrentUV = SpriteUV(frame);
        }
        int row = module.animation == ParticleSystemAnimationType.SingleRow ? module.rowMode switch
        {
            ParticleSystemAnimationRowMode.Random => Mathf.FloorToInt(ParticleSystem.Random(particle.randomSeed, 93) * ny),
            ParticleSystemAnimationRowMode.MeshIndex => MeshIndex(particle) % ny,
            _ => Mathf.Clamp(module.rowIndex, 0, ny - 1)
        } : frame / nx;
        int next = (frame + 1) % frames;
        int nextRow = module.animation == ParticleSystemAnimationType.SingleRow ? row : next / nx;
        streamNextUV = new Rect((next % nx) / (float)nx, 1 - (nextRow + 1) / (float)ny, 1f / nx, 1f / ny);
        return streamCurrentUV = new Rect((frame % nx) / (float)nx, 1 - (row + 1) / (float)ny, 1f / nx, 1f / ny);
    }

    //Facing 逐粒子面向观察点，Velocity 沿真实世界速度确定朝向。
    private Quaternion BillboardAlignment(ParticleSystem system, in ParticleCameraState cameraState, Vector3 center, Vector3 worldVelocity)
    {
        if (alignment == ParticleSystemRenderSpace.Local) return system.transform.rotation;
        if (alignment == ParticleSystemRenderSpace.World) return Quaternion.identity;
        return alignment switch
        {
            ParticleSystemRenderSpace.Facing when cameraState.HasCamera => cameraState.Align(center - cameraState.Position),
            ParticleSystemRenderSpace.Velocity => cameraState.Align(worldVelocity),
            _ => cameraState.ViewAlignment
        };
    }
}
