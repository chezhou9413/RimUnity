using System;
using System.Collections.Generic;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //模块使用公开 API 读取，只对已实现的设置提交产物。
    internal static class ParticleWriter
    {
        internal static void Write(ParticleSystem system, ComponentData data, ValueWriter writer, string path)
        {
            void Module(string name, object value, params string[] members) => data.members.Add(new MemberData { name = name, value = MathValueWriter.Properties(value, writer, path + "." + name, members) });
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, property = true, value = writer.Write(value, path + "." + name) });
            Validate(system, path);
            Put("randomSeed", system.randomSeed); Put("useAutoRandomSeed", system.useAutoRandomSeed);
            Module("main", system.main, "duration", "loop", "prewarm", "playOnAwake", "useUnscaledTime", "simulationSpeed", "maxParticles", "startDelay", "startLifetime", "startSpeed", "startColor", "startSize3D", "startSize", "startSizeX", "startSizeY", "startSizeZ", "startRotation3D", "startRotation", "startRotationX", "startRotationY", "startRotationZ", "gravityModifier", "simulationSpace", "customSimulationSpace", "scalingMode", "stopAction", "emitterVelocityMode", "emitterVelocity");
            var emission = MathValueWriter.Properties(system.emission, writer, path + ".emission", "enabled", "rateOverTime", "rateOverDistance");
            data.members[data.members.Count - 1].value.fields.Add(new MemberData { name = "flipRotation", value = writer.Write(system.main.flipRotation, path + ".main.flipRotation") });
            data.members[data.members.Count - 1].value.fields.Add(new MemberData { name = "ringBufferMode", value = writer.Write(system.main.ringBufferMode, path + ".main.ringBufferMode") });
            data.members[data.members.Count - 1].value.fields.Add(new MemberData { name = "ringBufferLoopRange", value = writer.Write(system.main.ringBufferLoopRange, path + ".main.ringBufferLoopRange") });
            Module("lifetimeByEmitterSpeed", system.lifetimeByEmitterSpeed, "enabled", "curve", "range");
            var bursts = new ParticleSystem.Burst[system.emission.burstCount]; system.emission.GetBursts(bursts);
            emission.fields.Add(new MemberData { name = "bursts", value = writer.Write(bursts, path + ".bursts") });
            data.members.Add(new MemberData { name = "emission", value = emission });
            WriteShape(system.shape, data, writer, path);
            Module("velocityOverLifetime", system.velocityOverLifetime, "enabled", "x", "y", "z", "speedModifier", "space", "orbitalX", "orbitalY", "orbitalZ", "orbitalOffsetX", "orbitalOffsetY", "orbitalOffsetZ", "radial");
            Module("forceOverLifetime", system.forceOverLifetime, "enabled", "x", "y", "z", "space", "randomized");
            Module("inheritVelocity", system.inheritVelocity, "enabled", "mode", "curve");
            WriteExternalForces(system, data, writer, path);
            Module("colorOverLifetime", system.colorOverLifetime, "enabled", "color");
            Module("sizeOverLifetime", system.sizeOverLifetime, "enabled", "separateAxes", "size", "x", "y", "z");
            Module("rotationOverLifetime", system.rotationOverLifetime, "enabled", "separateAxes", "x", "y", "z");
            Module("colorBySpeed", system.colorBySpeed, "enabled", "color", "range");
            Module("sizeBySpeed", system.sizeBySpeed, "enabled", "separateAxes", "size", "x", "y", "z", "range");
            Module("rotationBySpeed", system.rotationBySpeed, "enabled", "separateAxes", "x", "y", "z", "range");
            Module("limitVelocityOverLifetime", system.limitVelocityOverLifetime, "enabled", "separateAxes", "space", "limit", "limitX", "limitY", "limitZ", "dampen", "drag", "multiplyDragByParticleSize", "multiplyDragByParticleVelocity");
            Module("noise", system.noise, "enabled", "separateAxes", "strength", "strengthX", "strengthY", "strengthZ", "frequency", "damping", "octaveCount", "octaveMultiplier", "octaveScale", "scrollSpeed", "positionAmount", "rotationAmount", "sizeAmount", "remapEnabled", "remap", "remapX", "remapY", "remapZ", "quality");
            Module("textureSheetAnimation", system.textureSheetAnimation, "enabled", "mode", "timeMode", "fps", "speedRange", "numTilesX", "numTilesY", "cycleCount", "rowIndex", "animation", "rowMode", "frameOverTime", "startFrame");
            if (system.textureSheetAnimation.enabled && system.textureSheetAnimation.mode == ParticleSystemAnimationMode.Sprites)
            {
                var sprites = new Sprite[system.textureSheetAnimation.spriteCount];
                for (int i = 0; i < sprites.Length; i++) sprites[i] = system.textureSheetAnimation.GetSprite(i);
                data.members[data.members.Count - 1].value.fields.Add(new MemberData { name = "sprites", value = writer.Write(sprites, path + ".textureSheetAnimation.sprites") });
            }
            Module("trails", system.trails, "enabled", "ratio", "lifetime", "minVertexDistance", "worldSpace", "dieWithParticles", "sizeAffectsWidth", "sizeAffectsLifetime", "inheritParticleColor", "widthOverTrail", "colorOverLifetime", "colorOverTrail", "textureMode", "mode", "ribbonCount", "splitSubEmitterRibbons");
            WriteSubEmitters(system, data, writer, path);
            WriteCustom(system, data, writer, path);
            ParticlePhysicsWriter.Write(system, data, writer, path);
            Module("lights", system.lights, "enabled", "ratio", "useRandomDistribution", "light", "useParticleColor", "sizeAffectsRange", "alphaAffectsIntensity", "range", "intensity", "maxLights");
        }

        //网格保存资源引用，精灵同时保存原始几何数据。
        private static void WriteShape(ParticleSystem.ShapeModule shape, ComponentData data, ValueWriter writer, string path)
        {
            var value = MathValueWriter.Properties(shape, writer, path + ".shape", "enabled", "shapeType", "radius", "radiusThickness", "angle", "length", "arc", "arcSpread", "radiusSpread", "donutRadius", "boxThickness", "position", "rotation", "scale", "randomDirectionAmount", "sphericalDirectionAmount", "randomPositionAmount", "alignToDirection", "arcMode", "arcSpeed", "radiusMode", "radiusSpeed");
            void Field(string name, object content) => value.fields.Add(new MemberData { name = name, value = writer.Write(content, path + ".shape." + name) });
            if (shape.enabled && shape.texture != null)
            {
                Field("texture", shape.texture); Field("textureColorAffectsParticles", shape.textureColorAffectsParticles);
                Field("textureAlphaAffectsParticles", shape.textureAlphaAffectsParticles); Field("textureBilinearFiltering", shape.textureBilinearFiltering);
                Field("textureClipChannel", (int)shape.textureClipChannel); Field("textureClipThreshold", shape.textureClipThreshold); Field("textureUVChannel", shape.textureUVChannel);
            }
            if (shape.enabled && (shape.shapeType == ParticleSystemShapeType.Mesh || shape.shapeType == ParticleSystemShapeType.Sprite
                || shape.shapeType == ParticleSystemShapeType.MeshRenderer || shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer || shape.shapeType == ParticleSystemShapeType.SpriteRenderer))
            {
                Field("meshShapeType", shape.meshShapeType); Field("normalOffset", shape.normalOffset); Field("useMeshColors", shape.useMeshColors);
                Field("meshSpawnMode", shape.meshSpawnMode); Field("meshSpawnSpeed", shape.meshSpawnSpeed); Field("meshSpawnSpread", shape.meshSpawnSpread);
                if (shape.shapeType == ParticleSystemShapeType.Mesh)
                {
                    Field("mesh", shape.mesh); Field("useMeshMaterialIndex", shape.useMeshMaterialIndex); Field("meshMaterialIndex", shape.meshMaterialIndex);
                }
                else if (shape.shapeType == ParticleSystemShapeType.Sprite)
                {
                    Field("sprite", shape.sprite);
                    Vector2[] source = shape.sprite.vertices; ushort[] sourceTriangles = shape.sprite.triangles;
                    var positions = new Vector3[source.Length]; var triangles = new int[sourceTriangles.Length];
                    for (int i = 0; i < positions.Length; i++) positions[i] = new Vector3(source[i].x, source[i].y, 0);
                    for (int i = 0; i < triangles.Length; i++) triangles[i] = sourceTriangles[i];
                    Field("spriteVertices", positions); Field("spriteTriangles", triangles);
                }
                else
                {
                    Field("meshRenderer", shape.meshRenderer); Field("skinnedMeshRenderer", shape.skinnedMeshRenderer); Field("spriteRenderer", shape.spriteRenderer);
                    Field("useMeshMaterialIndex", shape.useMeshMaterialIndex); Field("meshMaterialIndex", shape.meshMaterialIndex);
                }
            }
            data.members.Add(new MemberData { name = "shape", value = value });
        }
        //启用模块的空槽保留原参数与位置，并明确说明它不会触发发射。
        private static void WriteSubEmitters(ParticleSystem system, ComponentData data, ValueWriter writer, string path)
        {
            var m = system.subEmitters;
            var entries = new ValueData { kind = "array", items = new List<ValueData>() };
            for (int i = 0; m.enabled && i < m.subEmittersCount; i++)
            {
                var type = m.GetSubEmitterType(i);
                ParticleSystem child = m.GetSubEmitterSystem(i);
                var e = new ValueData { kind = "data", fields = new List<MemberData>() };
                void Field(string n, object v) => e.fields.Add(new MemberData { name = n, value = writer.Write(v, path + ".subEmitters." + n) });
                if (child == null) Debug.LogWarning(path + ".subEmitters[" + i + "]：子发射器目标为空；保留槽位及参数，运行时不触发发射。", system);
                Field("system", child); Field("type", (int)type);
                Field("properties", (int)m.GetSubEmitterProperties(i)); Field("probability", m.GetSubEmitterEmitProbability(i)); entries.items.Add(e);
            }
            data.members.Add(new MemberData { name = "subEmitters", value = new ValueData { kind = "data", fields = new List<MemberData> {
                new MemberData { name = "enabled", value = writer.Write(m.enabled, path) }, new MemberData { name = "entries", value = entries } } } });
        }

        //力场引用随预制体一起绑定，未包含在预制体中的场景对象由引用检查明确报错。
        private static void WriteExternalForces(ParticleSystem system, ComponentData data, ValueWriter writer, string path)
        {
            var module = system.externalForces;
            var value = MathValueWriter.Properties(module, writer, path + ".externalForces", "enabled", "multiplierCurve");
            var influences = new ParticleSystemForceField[module.influenceCount];
            for (int i = 0; i < influences.Length; i++) influences[i] = module.GetInfluence(i);
            value.fields.Add(new MemberData { name = "influences", value = writer.Write(influences, path + ".externalForces.influences") });
            value.fields.Add(new MemberData { name = "influenceFilter", value = writer.Write((int)module.influenceFilter, path + ".externalForces.influenceFilter") });
            value.fields.Add(new MemberData { name = "influenceMask", value = writer.Write(module.influenceMask.value, path + ".externalForces.influenceMask") });
            data.members.Add(new MemberData { name = "externalForces", value = value });
        }
        private static void WriteCustom(ParticleSystem system, ComponentData data, ValueWriter writer, string path)
        {
            var m = system.customData; var streams = new ValueData { kind = "array", items = new List<ValueData>() };
            for (int i = 0; i < 2; i++)
            {
                var stream = (ParticleSystemCustomData)i; var mode = m.GetMode(stream);
                var item = new ValueData { kind = "data", fields = new List<MemberData> { new MemberData { name = "mode", value = writer.Write(mode, path) } } };
                if (mode == ParticleSystemCustomDataMode.Color) item.fields.Add(new MemberData { name = "color", value = writer.Write(m.GetColor(stream), path) });
                if (mode == ParticleSystemCustomDataMode.Vector)
                {
                    int n = m.GetVectorComponentCount(stream); var curves = new ParticleSystem.MinMaxCurve[4];
                    for (int j = 0; j < n; j++) curves[j] = m.GetVector(stream, j);
                    item.fields.Add(new MemberData { name = "components", value = writer.Write(n, path) });
                    item.fields.Add(new MemberData { name = "curves", value = writer.Write(curves, path) });
                }
                streams.items.Add(item);
            }
            data.members.Add(new MemberData { name = "customData", value = new ValueData { kind = "data", fields = new List<MemberData> {
                new MemberData { name = "enabled", value = writer.Write(m.enabled, path) }, new MemberData { name = "streams", value = streams } } } });
        }

        //尚未执行的设置必须明确报错，近似实现向作者给出可见提示。
        private static void Validate(ParticleSystem s, string path)
        {
            void Reject(bool condition, string feature) { if (condition) throw new NotSupportedException(path + "：尚未接通 " + feature); }
            if (s.collision.enabled && s.collision.quality != ParticleSystemCollisionQuality.High)
                Debug.LogWarning(path + "：Medium／Low 碰撞使用同一高精度几何查询，未实现 Unity 的体素缓存优化。", s);
            if (s.lights.enabled && s.lights.light != null)
            {
                Reject(s.lights.light.type != LightType.Point || s.lights.light.shadows != LightShadows.None || s.lights.light.cookie != null, "聚光／定向光／实时阴影／Cookie");
                Debug.LogWarning(path + "：粒子 Lights 使用 RimWorld 格子照明；范围上限 40 格，十赫兹更新，不提供三维逐像素受光。", s);
            }
            Reject(s.main.customSimulationSpace == null && s.main.simulationSpace == ParticleSystemSimulationSpace.Custom, "缺失 CustomSimulationSpace 引用");
            var shape = s.shape.shapeType;
            Reject(s.shape.enabled && shape != ParticleSystemShapeType.Sphere && shape != ParticleSystemShapeType.Hemisphere && shape != ParticleSystemShapeType.Box && shape != ParticleSystemShapeType.Circle && shape != ParticleSystemShapeType.Cone && shape != ParticleSystemShapeType.ConeVolume
                && shape != ParticleSystemShapeType.Rectangle && shape != ParticleSystemShapeType.SingleSidedEdge && shape != ParticleSystemShapeType.BoxShell && shape != ParticleSystemShapeType.BoxEdge && shape != ParticleSystemShapeType.Donut
                && shape != ParticleSystemShapeType.Mesh && shape != ParticleSystemShapeType.Sprite
                && shape != ParticleSystemShapeType.MeshRenderer && shape != ParticleSystemShapeType.SkinnedMeshRenderer && shape != ParticleSystemShapeType.SpriteRenderer
                && shape != ParticleSystemShapeType.SphereShell && shape != ParticleSystemShapeType.HemisphereShell && shape != ParticleSystemShapeType.ConeShell && shape != ParticleSystemShapeType.ConeVolumeShell && shape != ParticleSystemShapeType.CircleEdge, "粒子形状 " + shape);
            if (s.shape.enabled && (shape == ParticleSystemShapeType.Mesh || shape == ParticleSystemShapeType.Sprite))
            {
                if (shape == ParticleSystemShapeType.Mesh && s.shape.mesh == null) throw new NotSupportedException(path + "：Mesh 形状缺少网格引用。");
                if (shape == ParticleSystemShapeType.Sprite && s.shape.sprite == null) throw new NotSupportedException(path + "：Sprite 形状缺少精灵引用。");
            }
            if (s.textureSheetAnimation.enabled && s.textureSheetAnimation.mode == ParticleSystemAnimationMode.Sprites)
            {
                var sheet = s.textureSheetAnimation;
                Reject(sheet.spriteCount == 0, "空 Sprite 图集");
                Texture texture = sheet.GetSprite(0)?.texture;
                for (int i = 0; i < sheet.spriteCount; i++)
                {
                    Sprite sprite = sheet.GetSprite(i);
                    Reject(sprite == null, "Sprite 动画帧不能为空");
                    if (sprite != null && sprite.texture != texture)
                    {
                        var renderer = s.GetComponent<ParticleSystemRenderer>();
                        Reject(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_FlipbookMode") && renderer.sharedMaterial.GetFloat("_FlipbookMode") != 0,
                            "跨纹理 Sprite 帧之间的 Flipbook 混合");
                    }
                    Reject(sprite.packed && sprite.packingRotation != SpritePackingRotation.None, "旋转打包的 Sprite 图集");
                }
            }
        }
    }
}
