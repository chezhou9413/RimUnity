using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    //按 GUID 和 local ID 收集资源，同一导出批次内依赖只写一次。
    internal sealed partial class ResourceWriter
    {
        private readonly string directory, relative;
        private readonly Dictionary<string, AssetRecord> records;
        private readonly HashSet<string> writing = new HashSet<string>();
        private readonly HashSet<string> uiSources;
        private readonly Dictionary<string, string> standaloneShaders = new Dictionary<string, string>();
        private readonly Stack<HashSet<string>> dependencies = new Stack<HashSet<string>>();
        internal readonly ExportOptions Options;
        internal readonly ExportProcessorPipeline Processors;
        internal ResourceWriter(string directory, string relative, Dictionary<string, AssetRecord> records, ExportOptions options = null)
        {
            this.directory = directory; this.relative = relative; this.records = records;
            Options = (options ?? new ExportOptions()).Snapshot();
            uiSources = new HashSet<string>(Options.uiResourcePaths, StringComparer.OrdinalIgnoreCase);
            Processors = new ExportProcessorPipeline(Options);
        }

        internal ExportContext Context(Object source, string path, string id, bool ui = false, bool cameraUi = false)
            => new ExportContext(source, AssetDatabase.GetAssetPath(source), path, id, directory, ui, cameraUi);

        //预制体和资源共用同一个依赖收集栈。
        internal void Begin() => dependencies.Push(new HashSet<string>());
        internal List<string> End() => dependencies.Pop().OrderBy(s => s, StringComparer.Ordinal).ToList();

        //不可保存的场景外引用会报错，不用名字猜测对应资产。
        internal string Add(Object asset, string path, bool ui = false, bool cameraUi = false)
        {
            if (ExportUiPolicy.IsMarked(asset, uiSources)) ui = true;
            if (asset is Material && !ui) cameraUi |= Options.cameraUi;
            if (asset is Shader shader)
            {
                string sourceKey = shader.GetInstanceID() + ":" + ui + ":" + cameraUi;
                if (standaloneShaders.TryGetValue(sourceKey, out string cachedShader))
                { if (dependencies.Count > 0) dependencies.Peek().Add(cachedShader); return cachedShader; }
                return Processors.Run(Context(asset, path, "", ui, cameraUi), context =>
                {
                //独立 Shader 采用其默认材质参数，保留显式程序，不按名称替换材质。
                var defaults = new Material((Shader)context.ExportAsset);
                try
                {
                    int[] colorPasses = Enumerable.Range(0, defaults.passCount).Where(p => !MaterialWriter.IsShadowPass(defaults, p)).ToArray();
                    if (colorPasses.Length != 1) throw new NotSupportedException(path + "：独立 Shader 需要一个颜色 Pass；多 Pass 请导出材质资源。");
                    string shaderId = AddShader(defaults, ui, path, colorPasses[0], true);
                    standaloneShaders.Add(sourceKey, shaderId);
                    context.ResourceId = shaderId;
                    context.OutputFile = Path.Combine(directory, Path.GetFileName(records[shaderId].file));
                    return shaderId;
                }
                finally { Object.DestroyImmediate(defaults); }
                });
            }
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local))
                throw new NotSupportedException(path + "：引用不是可导出的工程资产：" + asset.name);
            string id = guid + "_" + local.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (asset is Material && ui) id += "_ui";
            if (asset is Material && cameraUi) id += "_camera_ui";
            if (dependencies.Count > 0) dependencies.Peek().Add(id);
            if (records.ContainsKey(id)) return id;
            if (!writing.Add(id)) throw new NotSupportedException(path + "：资源形成循环引用。");
            try
            {
                return Processors.Run(Context(asset, path, id, ui, cameraUi), context =>
                {
                    string result = WriteResource(context.ExportAsset, path, id, context.SourcePath, ui, cameraUi);
                    context.OutputFile = Path.Combine(directory, id + ".json");
                    return result;
                });
            }
            finally { writing.Remove(id); }
        }

        private string WriteResource(Object asset, string path, string id, string sourcePath, bool ui, bool cameraUi)
        {
            //Copies have no AssetDatabase path; replacements may be persistent assets.
            string inputPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(inputPath)) inputPath = sourcePath;
            Begin();
            try
            {
                var writer = new ValueWriter(new Dictionary<Object, string>(), this);
                var data = new ResourceData { name = asset.name };
                void Put(string key, object value) => data.members.Add(new MemberData { name = key, property = true, value = writer.Write(value, path + "." + key) });
                if (asset is TextAsset text)
                {
                    data.type = "TextAsset";
                    string file = id + ".bytes";
                    File.WriteAllBytes(Path.Combine(directory, file), text.bytes);
                    Put("file", relative + "/" + file);
                }
                else if (asset is UnityEngine.Video.VideoClip video)
                {
                    data.type = "VideoClip";
                    string source = inputPath;
                    string file = id + Path.GetExtension(source).ToLowerInvariant();
                    File.Copy(Path.GetFullPath(source), Path.Combine(directory, file));
                    Put("file", relative + "/" + file);
                }
                else if (asset is AudioClip audio)
                {
                    data.type = "AudioClip";
                    string source = inputPath;
                    string file = id + Path.GetExtension(source).ToLowerInvariant();
                    if (!new[] { ".ogg", ".wav", ".mp3" }.Contains(Path.GetExtension(file)))
                        throw new NotSupportedException(path + "：音频源文件需要 OGG、WAV 或 MP3。");
                    File.Copy(Path.GetFullPath(source), Path.Combine(directory, file));
                    Put("file", relative + "/" + file);
                }
                else if (asset is Light light)
                {
                    if (light.type != LightType.Point || light.shadows != LightShadows.None || light.cookie != null)
                        throw new NotSupportedException(path + "：粒子地图照明仅支持无阴影、无 Cookie 的 Point 模板。");
                    data.type = "Light";
                    Put("type", light.type); Put("color", light.color); Put("intensity", light.intensity); Put("range", light.range);
                }
                else if (asset is Cubemap cube) Cube(cube, data, writer, path, id);
                else if (asset is RenderTexture renderTexture)
                {
                    data.type = "RenderTexture";
                    if (renderTexture.dimension != UnityEngine.Rendering.TextureDimension.Tex2D || (renderTexture.antiAliasing != 1 && renderTexture.antiAliasing != 4) || renderTexture.useMipMap || renderTexture.enableRandomWrite)
                        throw new NotSupportedException(path + "：RenderTexture 支持一或四采样二维颜色附件，无 Mip 或随机写入。");
                    if (renderTexture.antiAliasing == 4) Debug.LogWarning("[RimUnity] " + path + "：宿主离屏通道使用 2×2 超采样和线性降采样实现四采样抗锯齿，不使用硬件 MSAA。");
                    Put("width", renderTexture.width); Put("height", renderTexture.height); Put("depth", renderTexture.depth);
                    Put("antiAliasing", renderTexture.antiAliasing);
                    Put("graphicsFormat", (int)renderTexture.graphicsFormat);
                    Put("wrapMode", renderTexture.wrapMode); Put("filterMode", renderTexture.filterMode);
                }
                else if (asset is Texture3D volume)
                {
                    if (!volume.isReadable) throw new NotSupportedException(path + "：三维向量纹理需要启用 Read/Write 才能导出浮点体素。");
                    data.type = "Texture3D";
                    Put("width", volume.width); Put("height", volume.height); Put("depth", volume.depth);
                    Put("pixels", volume.GetPixels(0)); Put("wrapMode", volume.wrapMode); Put("filterMode", volume.filterMode);
                }
                else if (asset is Texture2D texture)
                {
                    data.type = "Texture2D";
                    bool numeric = IsFloatTexture(texture.format);
                    bool compressed = IsGpuCompressed(texture.format);
                    string file = id + (numeric ? ".bytes" : compressed ? ".rutex" : ".png");
                    var importer = AssetImporter.GetAtPath(inputPath) as TextureImporter;
                    bool linear = importer != null ? !importer.sRGBTexture : !texture.isDataSRGB;
                    byte[] payload = numeric ? FloatTextureBytes(texture) : compressed ? GpuTextureBytes(texture, linear) : TextureBytes(texture, linear);
                    byte[] packed = compressed ? RimUnity.IO.GpuTexturePayload.CompressIfUseful(payload) : null;
                    if (packed != null) { payload = packed; file += ".gz"; }
                    File.WriteAllBytes(Path.Combine(directory, file), payload);
                    Put("file", relative + "/" + file); Put("linear", linear);
                    Put("readable", texture.isReadable);
                    if (numeric) { Put("numericFormat", "RGBAFloatLE"); Put("width", texture.width); Put("height", texture.height); }
                    Put("wrapMode", texture.wrapMode); Put("filterMode", texture.filterMode);
                }
                else if (asset is UnityEngine.U2D.SpriteAtlas atlas)
                {
                    data.type = "SpriteAtlas";
                    Put("sprites", SpriteAtlasWriter.Read(atlas, path));
                }
                else if (asset is Sprite sprite)
                {
                    if (sprite.packed) throw new NotSupportedException(path + "：请从原始 Sprite 导出，暂不接收已打包 SpriteAtlas。");
                    data.type = "Sprite";
                    Put("texture", sprite.texture); Put("rect", sprite.rect); Put("pivot", sprite.pivot / sprite.rect.size);
                    Put("pixelsPerUnit", sprite.pixelsPerUnit); Put("border", sprite.border);
                    Put("geometryPositions", sprite.vertices.Select(v => new Vector2(v.x * sprite.pixelsPerUnit / sprite.rect.width, v.y * sprite.pixelsPerUnit / sprite.rect.height) + sprite.pivot / sprite.rect.size).ToArray());
                    Put("geometryUV", sprite.uv); Put("geometryTriangles", sprite.triangles.Select(i => (int)i).ToArray());
                }
                else if (asset is Mesh mesh)
                {
                    if (mesh.blendShapeCount > 0) throw new NotSupportedException(path + "：BlendShape 不在首阶段范围内。");
                    data.type = "Mesh";
                    Put("vertices", mesh.vertices); Put("normals", mesh.normals); Put("tangents", mesh.tangents);
                    var uvChannels = new Vector4[8][];
                    for (int channel = 0; channel < uvChannels.Length; channel++)
                    {
                        var values = new List<Vector4>(); mesh.GetUVs(channel, values);
                        uvChannels[channel] = values.ToArray();
                    }
                    Put("uvChannels", uvChannels); Put("colors", mesh.colors);
                    var submeshes = new int[mesh.subMeshCount][];
                    for (int i = 0; i < submeshes.Length; i++)
                    {
                        if (mesh.GetTopology(i) != MeshTopology.Triangles) throw new NotSupportedException(path + "：当前仅接通三角网格。");
                        submeshes[i] = mesh.GetTriangles(i);
                    }
                    Put("bindposes", mesh.bindposes); Put("boneWeights", mesh.boneWeights);
                    //保留全部影响权重，不能用旧版四权重数组截断 Unity 的骨骼数据。
                    var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                    Put("skinCounts", counts.Select(n => (int)n).ToArray());
                    Put("skinIndices", weights.Select(w => w.boneIndex).ToArray());
                    Put("skinWeights", weights.Select(w => w.weight).ToArray());
                    Put("submeshes", submeshes);
                }
                else if (asset is Font font)
                {
                    data.type = "Font";
                    string source = inputPath;
                    bool builtin = source == "Resources/unity_builtin_extra" || source == "Library/unity default resources";
                    Put("builtin", builtin);
                    if (!builtin)
                    {
                        string extension = Path.GetExtension(source).ToLowerInvariant();
                        if (extension != ".ttf" && extension != ".otf" && extension != ".ttc") throw new NotSupportedException(path + "：字体必须提供 TTF／OTF／TTC 源文件。");
                        string file = id + extension;
                        File.Copy(Path.GetFullPath(source), Path.Combine(directory, file));
                        Put("file", relative + "/" + file);
                    }
                }
                else if (asset is TMPro.TMP_FontAsset tmpFont) TmpWriter.Font(tmpFont, data, writer, path);
                else if (asset is AnimationClip clip) AnimationWriter.Clip(clip, data, writer, path);
                else if (asset is UnityEditor.Animations.AnimatorController controller) AnimationWriter.Controller(controller, data, writer, path);
                else if (asset is Material material)
                {
                    data.type = "Material";
                    MaterialWriter.Write(material, data, writer, path, this, ui, cameraUi);
                }
                else if (!SpineWriter.Resource(asset, data, writer, path)) throw new NotSupportedException(path + "：未实现的资源类型：" + asset.GetType().FullName);
                string json = id + ".json";
                ExportJson.Write(Path.Combine(directory, json), data);
                records.Add(id, new AssetRecord { id = id, sourcePath = sourcePath, name = asset.name, description = "", path = "__assets/" + id,
                    file = relative + "/" + json, type = data.type, dependencies = End() });
                return id;
            }
            catch { End(); throw; }
        }

        //通过编辑器渲染读取导入后的纹理，不修改用户贴图的 Read/Write 设置。
        private static byte[] TextureBytes(Texture2D texture, bool linear)
        {
            if (texture.isReadable && (texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.RGB24 || texture.format == TextureFormat.ARGB32)) return texture.EncodeToPNG();
            //非可读或压缩贴图需要真实图形设备；空设备读回的像素不能作为成功产物发布。
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException(UnityEditor.AssetDatabase.GetAssetPath(texture) +
                    "：此贴图需要图形设备读回，当前 Unity 使用 Null 设备。请移除 -nographics 后重新导出。");
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Texture2D readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, linear);
            try
            {
                //遮罩和数值纹理必须保留线性通道，不能在读回时做颜色编码。
                GL.sRGBWrite = !linear;
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                return readable.EncodeToPNG();
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; RenderTexture.ReleaseTemporary(temporary); Object.DestroyImmediate(readable); }
        }
    }
}

