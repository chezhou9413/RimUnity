using System;
using System.IO;
using System.IO.Compression;

namespace RimUnity.Serialization
{
    //编辑器与运行时使用同一套文件编码和路径边界。
    public static class ContentFiles
    {
        //压缩内容直接流式解码，不生成临时解压文件。
        public static Stream OpenJson(string path)
        {
            var input = File.OpenRead(path);
            return path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(input, CompressionMode.Decompress) : (Stream)input;
        }

        //禁止绝对路径、目录穿越及链接指向内容目录外部。
        public static string Inside(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("无效资源路径：" + relative);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("资源路径越界：" + relative);
            for (string path = full; path != null && path.Length >= root.Length; path = Path.GetDirectoryName(path))
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("资源路径不可包含链接：" + path);
            return full;
        }

        //只有内容寻址载荷允许跨模组共享，资源描述迁移时也需先转换为内容寻址文件。
        public static void RequireBlob(string relative)
        {
            string name = Path.GetFileNameWithoutExtension(relative.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) ? relative.Substring(0, relative.Length - 3) : relative);
            if (!relative.StartsWith("blobs/", StringComparison.Ordinal) || relative.IndexOf('/', 6) >= 0
                || name.Length != 64
                || !IsHex(name))
                throw new InvalidDataException("共享资源必须使用 SHA256 文件名：" + relative);
        }

        //哈希文件名只允许小写十六进制字符。
        private static bool IsHex(string value)
        {
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
