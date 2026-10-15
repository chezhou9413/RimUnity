using System;
using System.IO;
using System.Reflection;
using Waylith.Rendering;

namespace RimUnity;

//文件纹理保留恢复来源，首次采样才上传 GPU，闲置后交给宿主管理驻留。
public sealed partial class Texture2D
{
    //只加载已存在的资源，不把损坏的文件替换成占位图片。
    public static Texture2D LoadFile(string path, bool linear = false, bool readable = false)
    {
        Runtime.NativeRuntime.AssertThread();
        string fullPath = Path.GetFullPath(path);
        var data = ReadFile(fullPath, out bool cached);
        return new Texture2D(new FileTexture(fullPath, linear, readable, data, cached));
    }

    private delegate bool ReadCached(string path, out LoadedTextureData data);
    private static readonly ReadCached ReadCache = typeof(GpuTextureService)
        .GetMethod("TryLoadPersistedTexture", BindingFlags.Static | BindingFlags.NonPublic)
        .CreateDelegate<ReadCached>();
    //已存在的缓存只绑定来源，不能每次列表重载都再次写入宿主缓存队列。
    private static readonly Action<Waylith.Rendering.Texture2D, string> SetSourcePath = typeof(Waylith.Rendering.Texture2D)
        .GetProperty("SourcePath").GetSetMethod(true).CreateDelegate<Action<Waylith.Rendering.Texture2D, string>>();

    //像素被脚本改写后不能再从原始文件恢复旧内容。
    private void PrepareFileWrite(bool replace = false)
    {
        if (CoreTexture is FileTexture file) file.DetachSource(replace);
    }

    //优先读宿主像素缓存；PNG 路径保留透明 RGB，不能再经过透明像素扩散。
    private static LoadedTextureData ReadFile(string path, out bool cached)
    {
        cached = ReadCache(path, out var data);
        if (cached) return data;
        if (IO.GpuTexturePayload.IsFile(path))
        {
            var payload = IO.GpuTexturePayload.Read(path);
            return new LoadedTextureData(payload.width, payload.height, payload.mipCount,
                (LoadedTextureFormat)payload.format, payload.levels, payload.opaque);
        }
        byte[] bytes = File.ReadAllBytes(path);
        byte[] rgba = PngDecoder.TryDecodeRgba32(bytes, out int width, out int height);
        if (rgba != null)
            return new LoadedTextureData(width, height, 1, LoadedTextureFormat.Rgba32, new[] { rgba }, TexturePixelData.IsOpaqueRgba32(rgba));
        if (GpuTextureService.TryLoadTextureFromMemory(bytes, out var decoded)) return decoded;
        throw new InvalidDataException("无法解码纹理：" + path);
    }

    //缺失磁盘缓存时从标准源文件精确恢复，避免宿主通用 PNG 路径改变预乘透明度。
    private sealed class FileTexture : Waylith.Rendering.Texture2D
    {
        private readonly string path;
        private readonly bool linear, keepReadable;
        private bool disposed, modified;

        internal FileTexture(string path, bool linear, bool readable, LoadedTextureData data, bool cached)
            : base(data.Width, data.Height, data.Format, linear, data.MipCount, true)
        {
            this.path = path; this.linear = linear; keepReadable = readable;
            Name = path; Opaque = data.Opaque;
            ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
            CpuData.Swizzle = data.Swizzle;
            if (cached) { SetSourcePath(this, path); TrackerName = path; }
            else PersistToTextureCache(path);
            DropPixels();
        }

        public override bool CanRehydrate => !disposed && (!modified || CpuData.HasData);

        //改写资源先保存现有像素，再解除磁盘恢复关系，调用 Apply 时由调用者决定是否保留 CPU 副本。
        internal void DetachSource(bool replace)
        {
            if (modified) return;
            if (!replace && !CpuData.HasData)
            {
                var data = ReadFile(path, out _);
                ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
                CpuData.Swizzle = data.Swizzle;
            }
            modified = true; SetSourcePath(this, null);
        }

        //宿主完成纹理引用解析后再上传，所有场景和 UI 都使用这一入口。
        public override bool EnsureResident()
        {
            if (disposed) throw new ObjectDisposedException(path);
            if (modified) return base.EnsureResident();
            if (GpuResource != null) return true;
            if (!CpuData.HasData)
            {
                var data = ReadFile(path, out _);
                ReplaceCpuData(data.Width, data.Height, data.Format, data.Levels, data.MipCount, linear, false);
                CpuData.Swizzle = data.Swizzle;
            }
            bool ready = base.EnsureResident();
            if (ready) DropPixels();
            return ready;
        }

        //不可读资源不保留 CPU 像素副本，GPU 回收后可按路径恢复。
        private void DropPixels()
        {
            if (keepReadable || modified) return;
            CpuData.ReleaseTracked(); CpuData.Release();
        }

        //销毁后的资源不能被旧材质引用重新上传。
        public override void Dispose() { disposed = true; base.Dispose(); }
    }
}
