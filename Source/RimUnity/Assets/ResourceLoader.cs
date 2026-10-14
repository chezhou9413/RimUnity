using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Serialization;

namespace RimUnity.Assets;

//从明文描述恢复共享网格、贴图、精灵、字体和材质。
internal static class ResourceLoader
{
    //资源描述与像素、字体文件分离，清单中不需要本机路径。
    internal static RimUnity.Object Load(AssetCatalog catalog, AssetRecord record)
    {
        if (record.type == "Shader") return Rendering.CustomShaderLoader.Load(catalog, record);
        ResourceData data = catalog.Read<ResourceData>(record.file);
        if (data.formatVersion != 1 || data.type != record.type) throw new InvalidDataException($"资源格式不匹配：{record.path}");
        var reader = new ValueReader(catalog, new Dictionary<string, RimUnity.Object>());
        var fields = data.members.ToDictionary(m => m.name, m => m.value);
        T Get<T>(string key) => (T)reader.Read(fields[key], typeof(T));
        RimUnity.Object result;
        switch (data.type)
        {
            case "TextAsset":
                result = new TextAsset(File.ReadAllBytes(catalog.Resolve(Get<string>("file"))));
                break;
            case "RenderTexture":
                result = new RenderTexture(Get<int>("width"), Get<int>("height"), Get<int>("depth"), Get<int>("graphicsFormat"), Get<int>("antiAliasing"))
                { wrapMode = Get<TextureWrapMode>("wrapMode"), filterMode = Get<FilterMode>("filterMode") };
                break;
            case "Light":
                var lightObject = GameObject.CreateAssetToken(data.name + " 灯光模板");
                var light = lightObject.AddComponent<Light>();
                reader.Apply(light, data.members); result = light; break;
            case "TMP_FontAsset":
                var tmpFont = new UI.TMP_FontAsset(); reader.Apply(tmpFont, data.members); result = tmpFont; break;
            case "AnimationClip":
                var clip = new AnimationClip(); reader.Apply(clip, data.members); result = clip; break;
            case "RuntimeAnimatorController":
                var controller = new RuntimeAnimatorController(); reader.Apply(controller, data.members); result = controller; break;
            case "Cubemap":
                var cube = Cubemap.Load(data.name, Get<int>("width"), Get<int>("mipCount"), File.ReadAllBytes(catalog.Resolve(Get<string>("file"))), Get<bool>("linear"));
                cube.wrapMode = Get<TextureWrapMode>("wrapMode"); cube.filterMode = Get<FilterMode>("filterMode"); cube.anisoLevel = Get<int>("anisoLevel"); cube.mipMapBias = Get<float>("mipMapBias");
                result = cube; break;
            case "Texture3D":
                var volume = new Texture3D(Get<int>("width"), Get<int>("height"), Get<int>("depth"), TextureFormat.RGBAFloat, false, true);
                volume.SetPixels(Get<Color[]>("pixels")); volume.Apply(false, false);
                volume.wrapMode = Get<TextureWrapMode>("wrapMode"); volume.filterMode = Get<FilterMode>("filterMode");
                result = volume; break;
            case "Texture2D":
                if (fields.ContainsKey("numericFormat"))
                {
                    if (Get<string>("numericFormat") != "RGBAFloatLE") throw new InvalidDataException("Unknown numeric texture encoding: " + record.path);
                    int w = Get<int>("width"), h = Get<int>("height");
                    if (w <= 0 || h <= 0) throw new InvalidDataException("Invalid numeric texture dimensions: " + record.path);
                    byte[] payload = File.ReadAllBytes(catalog.Resolve(Get<string>("file")));
                    if (!BitConverter.IsLittleEndian || payload.Length != checked(w * h * 16)) throw new InvalidDataException("Invalid numeric texture payload: " + record.path);
                    var numeric = new Texture2D(w, h, TextureFormat.RGBAFloat, false, Get<bool>("linear"));
                    numeric.CoreTexture.ReplaceCpuData(w, h, Waylith.Rendering.LoadedTextureFormat.RgbaFloat, new[] { payload }, 1, Get<bool>("linear"));
                    numeric.wrapMode = Get<TextureWrapMode>("wrapMode"); numeric.filterMode = Get<FilterMode>("filterMode");
                    result = numeric; break;
                }
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false, Get<bool>("linear"));
                if (!texture.LoadImage(File.ReadAllBytes(catalog.Resolve(Get<string>("file"))))) throw new InvalidDataException($"无法读取图片：{record.path}");
                //宿主图片解码会重置为 sRGB，解码后恢复导出时的数值纹理标记。
                var pixels = texture.CoreTexture.CpuData;
                if (pixels.Linear != Get<bool>("linear"))
                    texture.CoreTexture.ReplaceCpuData(texture.width, texture.height, pixels.Format, pixels.Levels, pixels.MipCount, Get<bool>("linear"));
                texture.wrapMode = Get<TextureWrapMode>("wrapMode");
                texture.filterMode = Get<FilterMode>("filterMode");
                result = texture;
                break;
            case "Sprite":
                result = Sprite.Create(Get<Texture2D>("texture"), Get<Rect>("rect"), Get<Vector2>("pivot"),
                    Get<float>("pixelsPerUnit"), 0, SpriteMeshType.FullRect, Get<Vector4>("border"));
                if (fields.ContainsKey("geometryPositions")) UI.SpriteGeometry.Register((Sprite)result, Get<Vector2[]>("geometryPositions"), Get<Vector2[]>("geometryUV"), Get<int[]>("geometryTriangles"));
                break;
            case "Font":
                if (fields.TryGetValue("builtin", out var builtin) && builtin.number != 0)
                    result = new Font { GameFontIndex = 1 };
                else
                {
                    int index = UI.Runtime.NativeGuiHost.RegisterFont(data.name, File.ReadAllBytes(catalog.Resolve(Get<string>("file"))), 32);
                    if (index < 0) throw new InvalidDataException($"Waylith 无法注册字体：{data.name}");
                    result = new Font { GameFontIndex = index };
                }
                break;
            case "Mesh":
                Mesh mesh = new() { indexFormat = IndexFormat.UInt32 };
                reader.Apply(mesh, data.members.Where(m => m.name != "submeshes" && m.name != "uvChannels" && !m.name.StartsWith("skin", StringComparison.Ordinal)));
                if (fields.ContainsKey("uvChannels"))
                {
                    Vector4[][] channels = Get<Vector4[][]>("uvChannels");
                    if (channels.Length > 8) throw new InvalidDataException("Mesh has more than eight UV channels: " + record.path);
                    for (int channel = 0; channel < channels.Length; channel++)
                    {
                        if (channels[channel] == null || channels[channel].Length != 0 && channels[channel].Length != mesh.vertexCount)
                            throw new InvalidDataException("Invalid UV channel length: " + record.path);
                        mesh.SetUVs(channel, channels[channel]);
                    }
                }
                if (fields.ContainsKey("skinCounts")) Rendering.SkinWeights.Register(mesh, Get<int[]>("skinCounts"), Get<int[]>("skinIndices"), Get<float[]>("skinWeights"));
                int[][] triangles = Get<int[][]>("submeshes");
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                result = mesh;
                break;
            case "Material":
                Shader unityShader;
                if (fields["shader"].kind == "asset") unityShader = Get<Shader>("shader");
                else
                {
                    string shaderName = Get<string>("shader");
                    if (!Waylith.Rendering.ShaderRegistry.TryResolve(shaderName, out var shader))
                        throw new InvalidDataException($"Shader 未注册：{shaderName}；请检查随包导出的 ShaderDef。");
                    unityShader = Rendering.StandardParticleShader.Resolve(shader, data.colorSpace == "Linear");
                }
                Material material = new(unityShader);
                if (fields.ContainsKey("shadowCaster")) material.shadowCaster = Get<Shader>("shadowCaster");
                if (fields.ContainsKey("shaderPasses")) material.SetPasses(Get<Shader[]>("shaderPasses"));
                foreach (var entry in fields.Where(p => p.Key != "shader" && p.Key != "shaderPasses" && p.Key != "shadowCaster"))
                {
                    if (entry.Value.kind == "asset" || entry.Value.kind == "null") material.SetTexture(entry.Key,
                        entry.Key == "_MainTex" && entry.Value.kind == "null" ? Texture2D.whiteTexture : (Texture)reader.Read(entry.Value, typeof(Texture)));
                    else if (entry.Value.kind == "array")
                    {
                        if (entry.Value.type == "float") material.SetFloatArray(entry.Key, (float[])reader.Read(entry.Value, typeof(float[])));
                        else if (entry.Value.type == "float4") material.SetVectorArray(entry.Key, (Vector4[])reader.Read(entry.Value, typeof(Vector4[])));
                        else throw new NotSupportedException("材质数组类型尚未支持：" + entry.Value.type + " " + entry.Key);
                    }
                    else if (entry.Value.kind == "vector") SetMaterialVector(material, entry.Key, (Vector4)reader.Read(entry.Value, typeof(Vector4)));
                    else if (entry.Value.kind == "integer") material.SetInt(entry.Key, (int)reader.Read(entry.Value, typeof(int)));
                    else material.SetFloat(entry.Key, (float)reader.Read(entry.Value, typeof(float)));
                }
                result = material;
                break;
            default: throw new NotSupportedException($"资源类型尚未实现：{data.type} ({record.path})");
        }
        result.name = data.name;
        return result;
    }

    //材质统一处理向量和纹理变换的设备布局。
    internal static void SetMaterialVector(Material material, string name, Vector4 value)
    {
        material.SetVector(name, value);
    }
}

