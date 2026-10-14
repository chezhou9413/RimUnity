using System;
using System.Collections.Generic;

namespace RimUnity.Serialization
{
    //一个内容模组拥有独立清单，资源引用只保存本清单中的稳定 ID。
    [Serializable]
    public sealed class Manifest
    {
        public int formatVersion = 1;
        public string owner;
        public List<AssetRecord> assets = new List<AssetRecord>();
        public Dictionary<string, string> sharedFiles = new Dictionary<string, string>();
    }

    //记录逻辑地址与实际文件，描述随资源一起发布。
    [Serializable]
    public sealed class AssetRecord
    {
        public string id, name, description, path, file, type, sourcePath;
        public List<string> dependencies = new List<string>();
    }
}
