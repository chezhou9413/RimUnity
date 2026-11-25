using System;
using System.IO;
using System.Reflection;
using Waylith.Graphics.Textures;
using Waylith.Rendering;

namespace RimUnity;

//文件纹理保留恢复来源，首次采样才上传 GPU，闲置后交给宿主管理驻留。
public sealed partial class Texture2D
{
    //只加载已存在的资源，不把损坏的文件替换成占位图片；保留三参重载以兼容已编译模组。
    public static Texture2D LoadFile(string path, bool linear = false, bool readable = false)
        => LoadFile(path, linear, readable, false);

    public static Texture2D LoadFile(string path, bool linear, bool readable, bool mipChain)
    {
        Runtime.NativeRuntime.AssertThread();
        string fullPath = Path.GetFullPath(path);
        var data = ReadFile(fullPath, mipChain, out bool cached);
        return new Texture2D(new FileTexture(fullPath, linear, readable, mipChain, data, cached));
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
    private static LoadedTextureData ReadFile(string path, bool mipChain, out bool cached)
    {
        bool isPayload = IO.GpuTexturePayload.IsFile(path);
        cached = ReadCache(path, out var data);
        if (cached)
        {
            bool needsMipUpgrade = mipChain && !isPayload && data.Format == LoadedTextureFormat.Rgba32
                && data.MipCount <= 1 && Math.Max(data.Width, data.Height) > 1;
            if (!needsMipUpgrade) return data;
            cached = false;
        }
        if (isPayload)
        {
            var payload = IO.GpuTexturePayload.Read(path);
            return new LoadedTextureData(payload.width, payload.height, payload.mipCount,
                (LoadedTextureFormat)payload.format, payload.levels, payload.opaque);
        }
        byte[] bytes = File.ReadAllBytes(path);
        byte[] rgba = PngDecoder.TryDecodeRgba32(bytes, out int width, out int height);
        if (rgba != null)
        {
            int mipCount = mipChain ? TexturePixelData.CalculateMipCount(width, height) : 1;
            byte[][] levels = mipCount > 1
                ? TexturePixelData.GenerateRgba32MipChain(rgba, width, height, mipCount, premultiplied: false)
                : new[] { rgba };
            return new LoadedTextureData(width, height, mipCount, LoadedTextureFormat.Rgba32, levels, TexturePixelData.IsOpaqueRgba32(rgba));
        }
        if (GpuTextureService.TryLoadTextureFromMemory(bytes, out var decoded))
        {
            if (mipChain && decoded.Format == LoadedTextureFormat.Rgba32 && decoded.MipCount <= 1
                && Math.Max(decoded.Width, decoded.Height) > 1 && decoded.Levels is { Length: > 0 })
            {
                int mipCount = TexturePixelData.CalculateMipCount(decoded.Width, decoded.Height);
                byte[][] levels = TexturePixelData.GenerateRgba32MipChain(decoded.Levels[0], decoded.Width, decoded.Height, mipCount, premultiplied: false);
                return new LoadedTextureData(decoded.Width, decoded.Height, mipCount, decoded.Format, levels, decoded.Opaque, decoded.SourceStamp, decoded.Swizzle);
            }
            return decoded;
        }
        throw new InvalidDataException("无法解码纹理：" + path);
    }

    //缺失磁盘缓存时从标准源文件精确恢复，避免宿主通用 PNG 路径改变预乘透明度。
    private sealed class FileTexture : Waylith.Rendering.Texture2D
    {
        private readonly string path;
        private readonly bool linear, keepReadable;
        private readonly bool _mipChain;
        private bool disposed, modified;

        internal FileTexture(string path, bool linear, bool readable, bool mipChain, LoadedTextureData data, bool cached)
            : base(data.Width, data.Height, data.Format, linear, data.MipCount, true)
        {
            this.path = path; this.linear = linear; keepReadable = readable; _mipChain = mipChain;
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
                var data = ReadFile(path, _mipChain, out _);
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
                var data = ReadFile(path, _mipChain, out _);
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
