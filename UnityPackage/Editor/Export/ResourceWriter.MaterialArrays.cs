using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    internal sealed partial class ResourceWriter
    {
        // Owned by this export transaction; no process-wide material cache.
        internal void WriteMaterialArrays(Material material, ResourceData data, ValueWriter writer, string path)
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var shader in data.members.Where(m => m.name == "shader" || m.name == "shaderPasses"))
            foreach (var value in shader.value.items ?? new List<ValueData> { shader.value })
            {
                if (value.kind != "asset" || !shaderArrayProperties.TryGetValue(value.text, out JObject arrays) || arrays == null) continue;
                foreach (var property in arrays.Properties())
                {
                    string type = (string)property.Value;
                    if (types.TryGetValue(property.Name, out string previous) && previous != type)
                        throw new InvalidOperationException(path + "：不同 Pass 的材质数组类型不一致：" + property.Name);
                    types[property.Name] = type;
                }
            }
            foreach (var array in types)
            {
                Array values = array.Value == "float" ? (Array)material.GetFloatArray(array.Key) : material.GetVectorArray(array.Key);
                // Leave unset arrays to runtime/global setters, as in Unity.
                if (values == null || values.Length == 0) continue;
                ValueData encoded = writer.Write(values, path + "." + array.Key);
                encoded.type = array.Value;
                data.members.Add(new MemberData { name = array.Key, value = encoded });
            }
        }
    }
}