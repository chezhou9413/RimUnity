using System;
using System.Collections.Generic;
using System.Linq;
using RimUnity.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace RimUnity.Editor
{
    //逐项检查内置组件的导出支持范围。
    internal static class ComponentWriter
    {
        private static readonly Dictionary<string, string[]> Members = new Dictionary<string, string[]>
        {
            ["UnityEngine.Canvas"] = new[] { "renderMode", "sortingOrder", "worldCamera", "scaleFactor", "referencePixelsPerUnit", "overrideSorting", "planeDistance" },
            ["UnityEngine.CanvasGroup"] = new[] { "alpha", "interactable", "blocksRaycasts", "ignoreParentGroups" },
            ["UnityEngine.UI.CanvasScaler"] = new[] { "uiScaleMode", "referencePixelsPerUnit", "scaleFactor", "referenceResolution", "screenMatchMode", "matchWidthOrHeight", "physicalUnit", "fallbackScreenDPI", "defaultSpriteDPI", "dynamicPixelsPerUnit" },
            ["UnityEngine.UI.GraphicRaycaster"] = Array.Empty<string>(),
            ["UnityEngine.UI.Image"] = new[] { "color", "raycastTarget", "sprite", "overrideSprite", "type", "preserveAspect", "fillCenter", "fillMethod", "fillAmount", "fillClockwise", "fillOrigin", "pixelsPerUnitMultiplier", "useSpriteMesh" },
            ["UnityEngine.UI.RawImage"] = new[] { "color", "raycastTarget", "texture", "uvRect" },
            ["UnityEngine.UI.Text"] = new[] { "color", "raycastTarget", "text", "font", "fontSize", "fontStyle", "alignment", "supportRichText", "lineSpacing", "resizeTextForBestFit", "resizeTextMinSize", "resizeTextMaxSize" },
            ["UnityEngine.UI.Button"] = new[] { "interactable", "targetGraphic", "transition", "onClick" },
            ["UnityEngine.UI.Toggle"] = new[] { "interactable", "targetGraphic", "transition", "graphic", "group", "toggleTransition", "isOn", "onValueChanged" },
            ["UnityEngine.UI.ToggleGroup"] = new[] { "allowSwitchOff" },
            ["UnityEngine.UI.Slider"] = new[] { "interactable", "targetGraphic", "transition", "fillRect", "handleRect", "direction", "minValue", "maxValue", "wholeNumbers", "value", "onValueChanged" },
            ["UnityEngine.UI.Scrollbar"] = new[] { "interactable", "targetGraphic", "transition", "handleRect", "direction", "numberOfSteps", "size", "value", "onValueChanged" },
            ["UnityEngine.UI.RectMask2D"] = new[] { "padding", "softness" },
            ["UnityEngine.UI.Mask"] = new[] { "showMaskGraphic" },
            ["UnityEngine.UI.ContentSizeFitter"] = new[] { "horizontalFit", "verticalFit" },
            ["UnityEngine.UI.AspectRatioFitter"] = new[] { "aspectMode", "aspectRatio" },
            ["UnityEngine.UI.GridLayoutGroup"] = new[] { "padding", "childAlignment", "startCorner", "startAxis", "cellSize", "spacing", "constraint", "constraintCount" },
            ["UnityEngine.UI.LayoutElement"] = new[] { "minWidth", "minHeight", "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight", "ignoreLayout", "layoutPriority" },
            ["UnityEngine.UI.HorizontalLayoutGroup"] = new[] { "padding", "childAlignment", "spacing", "childForceExpandWidth", "childForceExpandHeight", "childControlWidth", "childControlHeight", "childScaleWidth", "childScaleHeight", "reverseArrangement" },
            ["UnityEngine.UI.VerticalLayoutGroup"] = new[] { "padding", "childAlignment", "spacing", "childForceExpandWidth", "childForceExpandHeight", "childControlWidth", "childControlHeight", "childScaleWidth", "childScaleHeight", "reverseArrangement" },
            ["UnityEngine.UI.ScrollRect"] = new[] { "content", "viewport", "horizontal", "vertical", "movementType", "elasticity", "inertia", "decelerationRate", "scrollSensitivity", "horizontalScrollbar", "verticalScrollbar", "horizontalScrollbarVisibility", "verticalScrollbarVisibility", "horizontalScrollbarSpacing", "verticalScrollbarSpacing", "onValueChanged" },
            ["UnityEngine.MeshFilter"] = new[] { "sharedMesh" },
            ["UnityEngine.SphereCollider"] = new[] { "enabled", "isTrigger", "center", "radius" },
            ["UnityEngine.BoxCollider"] = new[] { "enabled", "isTrigger", "center", "size" },
            ["UnityEngine.CapsuleCollider"] = new[] { "enabled", "isTrigger", "center", "radius", "height", "direction" },
            ["UnityEngine.MeshCollider"] = new[] { "enabled", "isTrigger", "sharedMesh", "convex" },
            ["UnityEngine.BoxCollider2D"] = new[] { "enabled", "isTrigger", "offset", "size", "edgeRadius" },
            ["UnityEngine.CircleCollider2D"] = new[] { "enabled", "isTrigger", "offset", "radius" },
            ["UnityEngine.CapsuleCollider2D"] = new[] { "enabled", "isTrigger", "offset", "size", "direction" },
            ["UnityEngine.PolygonCollider2D"] = new[] { "enabled", "isTrigger", "offset" },
            ["UnityEngine.EdgeCollider2D"] = new[] { "enabled", "isTrigger", "offset", "points", "edgeRadius" },
            ["UnityEngine.TerrainCollider"] = new[] { "enabled", "isTrigger" },
            ["UnityEngine.Rigidbody2D"] = new[] { "mass", "drag", "angularDrag", "bodyType", "simulated", "velocity", "angularVelocity", "gravityScale", "centerOfMass", "inertia", "constraints" },
            ["UnityEngine.Rigidbody"] = new[] { "mass", "drag", "angularDrag", "isKinematic", "useGravity", "detectCollisions", "velocity", "angularVelocity", "centerOfMass", "inertiaTensor", "inertiaTensorRotation", "constraints", "maxAngularVelocity" },
            ["UnityEngine.ParticleSystemForceField"] = new[] { "shape", "startRange", "endRange", "length", "directionX", "directionY", "directionZ", "gravity", "gravityFocus", "rotationSpeed", "rotationAttraction", "rotationRandomness", "drag", "multiplyDragByParticleSize", "multiplyDragByParticleVelocity", "vectorField", "vectorFieldSpeed", "vectorFieldAttraction" },
            ["UnityEngine.WindZone"] = new[] { "mode", "radius", "windMain", "windTurbulence", "windPulseMagnitude", "windPulseFrequency" },
            ["UnityEngine.MeshRenderer"] = new[] { "sharedMaterials", "enabled" },
            ["UnityEngine.SpriteRenderer"] = new[] { "sprite", "color", "flipX", "flipY", "sharedMaterial", "sortingOrder", "enabled" },
            ["UnityEngine.SkinnedMeshRenderer"] = new[] { "sharedMesh", "sharedMaterials", "bones", "rootBone", "localBounds", "enabled", "updateWhenOffscreen" },
            ["UnityEngine.Animator"] = new[] { "runtimeAnimatorController", "speed", "updateMode", "fireEvents" },
            ["UnityEngine.ParticleSystemRenderer"] = new[] { "sharedMaterial", "trailMaterial", "renderMode", "alignment", "sortMode", "lengthScale", "velocityScale", "pivot", "flip", "allowRoll", "enabled" },
            ["UnityEngine.EventSystems.EventSystem"] = new[] { "sendNavigationEvents", "firstSelectedGameObject", "pixelDragThreshold" },
            ["UnityEngine.EventSystems.EventTrigger"] = new[] { "triggers" }
        };

        //组件的稳定 ID 由所属节点和组件顺序决定。
        internal static ComponentData Write(Component component, string id, ValueWriter writer, string path, bool root)
        {
            string original = component.GetType().FullName;
            var data = new ComponentData { id = id, type = MapTypeName(original), enabled = !(component is Behaviour b) || b.enabled };
            if (component is TMPro.TextMeshProUGUI tmp) { TmpWriter.Component(tmp, data, writer, path); return data; }
            if (component is ParticleSystem particles) { ParticleWriter.Write(particles, data, writer, path); return data; }
            if (component is UnityEngine.Animation legacy) { LegacyEffectWriter.Animation(legacy, data, writer, path); return data; }
            if (component is UnityEngine.TrailRenderer trail) { LegacyEffectWriter.Trail(trail, data, writer, path); return data; }
            if (component is UnityEngine.LineRenderer line) { LegacyEffectWriter.Line(line, data, writer, path); return data; }
            Validate(component, path, root);
            if (component is TerrainCollider terrain) ParticlePhysicsWriter.Terrain(terrain, data, writer, path);
            if (component is Collider physicsCollider && physicsCollider.sharedMaterial != null)
                data.members.Add(new MemberData { name = "sharedMaterial", value = MathValueWriter.Properties(physicsCollider.sharedMaterial, writer, path + ".sharedMaterial",
                    "name", "dynamicFriction", "staticFriction", "bounciness", "frictionCombine", "bounceCombine") });
            if (component is Collider2D collider2D && collider2D.sharedMaterial != null)
                data.members.Add(new MemberData { name = "sharedMaterial", property = true, value = MathValueWriter.Properties(collider2D.sharedMaterial, writer, path + ".sharedMaterial", "name", "friction", "bounciness") });
            if (component is PolygonCollider2D polygon)
            {
                var paths = new Vector2[polygon.pathCount][];
                for (int i = 0; i < paths.Length; i++) paths[i] = polygon.GetPath(i);
                data.members.Add(new MemberData { name = "paths", value = writer.Write(paths, path + ".paths") });
            }
            if (component is ParticleSystemRenderer particleRenderer)
            {
                var streams = new List<ParticleSystemVertexStream>();
                particleRenderer.GetActiveVertexStreams(streams);
                int width = streams.Sum(s => ParticleVertexLayout.Width((int)s));
                if (width > 12) throw new NotSupportedException(path + "：粒子数值流超过 TEXCOORD0～2 的容量。");
                data.members.Add(new MemberData { name = "vertexStreams", property = true, value = writer.Write(streams.ToArray(), path + ".vertexStreams") });
                if (particleRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    var meshes = new Mesh[particleRenderer.meshCount]; particleRenderer.GetMeshes(meshes);
                    var weights = new float[meshes.Length]; particleRenderer.GetMeshWeightings(weights);
                    data.members.Add(new MemberData { name = "meshes", value = writer.Write(meshes, path + ".meshes") });
                    data.members.Add(new MemberData { name = "meshWeightings", value = writer.Write(weights, path + ".meshWeightings") });
                    data.members.Add(new MemberData { name = "meshDistribution", value = writer.Write((int)particleRenderer.meshDistribution, path + ".meshDistribution") });
                }
            }
            if (component is Graphic customGraphic && customGraphic.material != null && customGraphic.material != customGraphic.defaultMaterial)
            {
                Canvas canvas = customGraphic.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    throw new NotSupportedException(path + "：世界／相机画布的自定义 UI Shader 尚未接通。");
                if (customGraphic.GetComponentInParent<Mask>() != null)
                    throw new NotSupportedException(path + "：自定义 UI Shader 的 Mask 覆盖率入口尚未接通。");
                if (customGraphic.GetComponentInParent<RectMask2D>() != null)
                    throw new NotSupportedException(path + "：自定义 UI Shader 的 RectMask2D 裁切入口尚未接通。");
                data.members.Add(new MemberData { name = "material", property = true, value = writer.Material(customGraphic.material, path + ".material", true) });
            }
            if (Members.TryGetValue(original, out string[] names))
            {
                foreach (string name in names)
                {
                    if (name == "padding" && component is LayoutGroup)
                    {
                        RectOffset padding = (RectOffset)component.GetType().GetProperty(name).GetValue(component);
                        data.members.Add(new MemberData { name = name, property = true, value = new ValueData { kind = "data", fields =
                            new[] { "left", "right", "top", "bottom" }.Select(n => writer.Property(padding, n, path)).ToList() } });
                    }
                    else data.members.Add(writer.Property(component, name, path));
                }
                if (component is Selectable selectable)
                {
                    data.members.Add(writer.Property(selectable, "animationTriggers", path));
                    data.members.Add(new MemberData { name = "navigation", property = true, value = MathValueWriter.Properties(selectable.navigation, writer, path, "mode", "wrapAround", "selectOnLeft", "selectOnRight", "selectOnUp", "selectOnDown") });
                    data.members.Add(new MemberData { name = "spriteState", property = true, value = MathValueWriter.Properties(selectable.spriteState, writer, path, "highlightedSprite", "pressedSprite", "selectedSprite", "disabledSprite") });
                    var fields = new List<MemberData>();
                    foreach (string name in new[] { "normalColor", "highlightedColor", "pressedColor", "selectedColor", "disabledColor", "colorMultiplier", "fadeDuration" })
                        fields.Add(new MemberData { name = name, value = writer.Write(typeof(ColorBlock).GetProperty(name).GetValue(selectable.colors), path) });
                    data.members.Add(new MemberData { name = "colors", property = true, value = new ValueData { kind = "data", fields = fields } });
                }
                if (component is Text text)
                {
                    data.members.Add(new MemberData { name = "wordWrap", property = true, value = writer.Write(text.horizontalOverflow == HorizontalWrapMode.Wrap, path) });
                    data.members.Add(new MemberData { name = "clipText", property = true, value = writer.Write(text.verticalOverflow == VerticalWrapMode.Truncate, path) });
                }
            }
            else if (component is MonoBehaviour && !original.StartsWith("UnityEngine.", StringComparison.Ordinal) && !original.StartsWith("TMPro.", StringComparison.Ordinal))
                data.members = writer.Fields(component, path);
            else throw new NotSupportedException(path + "：组件尚未实现：" + original);
            return data;
        }

        //公开映射以便事件的 Unity 对象参数一并调整。
        internal static string MapTypeName(string type)
        {
            if (type == "TMPro.TextMeshProUGUI") return "RimUnity.UI.TextMeshProUGUI";
            if (type == "TMPro.TMP_FontAsset") return "RimUnity.UI.TMP_FontAsset";
            if (type.StartsWith("UnityEngine.", StringComparison.Ordinal)) return "RimUnity." + type.Substring("UnityEngine.".Length);
            return type;
        }

        //当前还未接通的非默认设置必须阻止导出，不能降级后假报成功。
        private static void Validate(Component component, string path, bool root)
        {
            if (component is Renderer shadowRenderer && shadowRenderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off
                && shadowRenderer.sharedMaterials.Any(m => m != null && Enumerable.Range(0, m.passCount).Any(i => MaterialWriter.IsShadowPass(m, i))))
                throw new NotSupportedException(path + "：此 Renderer 启用了阴影投射，宿主投影阶段尚未接通。");
            if (component is Rigidbody body && (body.collisionDetectionMode != CollisionDetectionMode.Discrete || body.interpolation != RigidbodyInterpolation.None))
                throw new NotSupportedException(path + "：刚体 CCD／插值尚未接通。");
            if (component is Rigidbody2D body2D && (body2D.collisionDetectionMode != CollisionDetectionMode2D.Discrete || body2D.interpolation != RigidbodyInterpolation2D.None || body2D.useAutoMass))
                throw new NotSupportedException(path + "：二维刚体 CCD／插值／自动质量尚未接通。");
            if (component is Collider2D attached2D && (attached2D.usedByEffector || attached2D.usedByComposite))
                throw new NotSupportedException(path + "：二维 Effector／Composite 几何尚未接通。");
            if (component is EdgeCollider2D edge && (edge.useAdjacentStartPoint || edge.useAdjacentEndPoint))
                throw new NotSupportedException(path + "：EdgeCollider2D 相邻虚拟端点法线尚未接通。");
            if (component is MeshCollider meshCollider && !meshCollider.convex && meshCollider.attachedRigidbody != null && !meshCollider.attachedRigidbody.isKinematic)
                throw new NotSupportedException(path + "：动态刚体的 MeshCollider 必须使用 Convex。");
            if (component is ParticleSystemRenderer particle && particle.GetComponentInParent<Canvas>() != null && particle.sharedMaterials.Any(m => m != null && m.passCount > 1
                && UnityEditor.AssetDatabase.GetAssetPath(m.shader).StartsWith("Assets/", StringComparison.Ordinal)))
                throw new NotSupportedException(path + "：UI 粒子的自定义多 Pass 材质尚未接通。");
            if (component is SpriteRenderer sprite && (sprite.drawMode != SpriteDrawMode.Simple || sprite.maskInteraction != SpriteMaskInteraction.None))
                throw new NotSupportedException(path + "：当前 SpriteRenderer 支持 Simple 几何，不支持九宫格／平铺／SpriteMask。");
            if (component is SpriteRenderer sorted && sorted.sortingLayerID != 0)
                throw new NotSupportedException(path + "：SpriteRenderer 当前只接通默认 Sorting Layer。");
            if (component is Mask stencil && !(stencil.graphic is RawImage) && !(stencil.graphic is Image maskImage && maskImage.type == Image.Type.Simple && !maskImage.preserveAspect && !maskImage.useSpriteMesh))
                throw new NotSupportedException(path + "：基础 Mask 需要普通 Simple Image 或 RawImage，复杂遮罩几何尚未接通。");
            if (component is Animator animator && (animator.applyRootMotion || animator.avatar != null && animator.avatar.isHuman)) throw new NotSupportedException(path + "：Root Motion／Humanoid 不在首阶段范围。");
            if (component is Text spaced && (spaced.lineSpacing < 0 || float.IsNaN(spaced.lineSpacing) || float.IsInfinity(spaced.lineSpacing))) throw new NotSupportedException(path + "：Text 行距必须为非负有限值。");
            if (!Members.ContainsKey(component.GetType().FullName) && component is ISerializationCallbackReceiver) throw new NotSupportedException(path + "：自定义序列化回调尚未适配。");
        }
    }
}
