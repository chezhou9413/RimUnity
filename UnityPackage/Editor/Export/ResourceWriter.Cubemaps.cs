using System;
using System.Collections.Generic;
using System.IO;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    internal sealed partial class ResourceWriter
    {
        internal void Cube(Cubemap cube, ResourceData data, ValueWriter writer, string path, string id)
        {
            if (!cube.isReadable) throw new InvalidOperationException(path + "：Cubemap 导出需要开启 Read/Write，以保留原六面与 mip 像素。");
            data.type = "Cubemap";
            string file = id + ".rgba32f";
            using (var stream = new BinaryWriter(File.Create(Path.Combine(directory, file))))
                for (int mip = 0; mip < cube.mipmapCount; mip++)
                    for (int face = 0; face < 6; face++)
                        foreach (Color color in cube.GetPixels((CubemapFace)face, mip))
                        { stream.Write(color.r); stream.Write(color.g); stream.Write(color.b); stream.Write(color.a); }
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, path + "." + name) });
            Put("file", relative + "/" + file); Put("width", cube.width); Put("mipCount", cube.mipmapCount);
            Put("linear", !cube.isDataSRGB); Put("wrapMode", cube.wrapMode); Put("filterMode", cube.filterMode);
            Put("anisoLevel", cube.anisoLevel); Put("mipMapBias", cube.mipMapBias);
        }

        internal string DefaultBumpTexture()
        {
            const string id = "RimUnity_DefaultBumpTexture";
            if (dependencies.Count > 0) dependencies.Peek().Add(id);
            if (records.ContainsKey(id)) return id;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try { texture.SetPixels32(new[] { new Color32(128, 128, 255, 255) }); texture.Apply(); File.WriteAllBytes(Path.Combine(directory, id + ".png"), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            var data = new ResourceData { type = "Texture2D", name = "Unity default normal map" };
            var writer = new ValueWriter(new Dictionary<UnityEngine.Object, string>(), this);
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, id + "." + name) });
            Put("file", relative + "/" + id + ".png"); Put("linear", true); Put("wrapMode", TextureWrapMode.Repeat); Put("filterMode", FilterMode.Bilinear);
            ExportJson.Write(Path.Combine(directory, id + ".json"), data);
            records.Add(id, new AssetRecord { id = id, type = "Texture2D", name = data.name, path = "__assets/" + id, file = relative + "/" + id + ".json" });
            return id;
        }
        //未赋值的 Cube 属性使用六面灰色纹理。
        internal string DefaultGrayCube()
        {
            const string id = "RimUnity_DefaultGrayCube";
            if (dependencies.Count > 0) dependencies.Peek().Add(id);
            if (records.ContainsKey(id)) return id;
            var data = new ResourceData { type = "Cubemap", name = "Unity default gray cubemap" };
            var writer = new ValueWriter(new Dictionary<UnityEngine.Object, string>(), this);
            string file = id + ".rgba32f";
            using (var stream = new BinaryWriter(File.Create(Path.Combine(directory, file))))
                for (int face = 0; face < 6; face++) { stream.Write(0.5f); stream.Write(0.5f); stream.Write(0.5f); stream.Write(1f); }
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, value = writer.Write(value, id + "." + name) });
            Put("file", relative + "/" + file); Put("width", 1); Put("mipCount", 1); Put("linear", false);
            Put("wrapMode", TextureWrapMode.Clamp); Put("filterMode", FilterMode.Bilinear); Put("anisoLevel", 1); Put("mipMapBias", 0f);
            ExportJson.Write(Path.Combine(directory, id + ".json"), data);
            records.Add(id, new AssetRecord { id = id, type = "Cubemap", name = data.name,
                path = "__assets/" + id, file = relative + "/" + id + ".json" });
            return id;
        }
    }
}

