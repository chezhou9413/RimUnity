using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //GLSL 和管线描述作为普通依赖写入当前事务，不依赖模组全局 Shaders 目录。
    internal sealed partial class ResourceWriter
    {
        private readonly System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JObject> shaderArrayProperties = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JObject>();
        internal string AddShader(Material material, bool ui, string path, int pass = -1, bool includeDefaults = false, bool cameraUi = false)
        {
            var exported = CustomShaderWriter.Write(material, directory, ui, path, pass, cameraUi);
            ShaderData shader = ShaderStateWriter.Write(material, exported.Manifest, ui, path);
            if (includeDefaults) shader.defaults = ShaderDefaultsWriter.Read(material);
            foreach (string approximation in shader.approximations) Debug.LogWarning("[RimUnity] " + path + "：" + approximation);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(shader)))).Replace("-", "").ToLowerInvariant();
            string id = exported.Id + "_" + hash;
            if (dependencies.Count > 0) dependencies.Peek().Add(id);
            shaderArrayProperties[id] = exported.Manifest["arrays"] as Newtonsoft.Json.Linq.JObject;
            if (records.ContainsKey(id)) return id;
            shader.key = id;
            string folder = Path.GetFileName(exported.Directory);
            shader.vertexFile = relative + "/" + folder + "/" + (string)exported.Manifest["vertexFile"];
            shader.fragmentFile = relative + "/" + folder + "/" + (string)exported.Manifest["fragmentFile"];
            shader.descriptorFile = relative + "/" + folder + "/shader.json";
            //程序身份和材质状态一起决定文件名，避免不同程序覆盖同一描述。
            string fileHash;
            using (var sha = SHA256.Create()) fileHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").ToLowerInvariant();
            string file = "s_" + CustomShaderWriter.CompactHash(fileHash) + ".json";
            ExportJson.Write(Path.Combine(directory, file), shader);
            records.Add(id, new AssetRecord { id = id, sourcePath = UnityEditor.AssetDatabase.GetAssetPath(material.shader), name = material.shader.name, description = ui ? "UI GLSL" : "世界 GLSL", path = "__assets/" + id, file = relative + "/" + file, type = "Shader" });
            return id;
        }
    }
}


