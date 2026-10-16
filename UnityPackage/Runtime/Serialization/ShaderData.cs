using System;

namespace RimUnity.Serialization
{
    //渲染状态已由导出器求值，运行时无需解析 ShaderLab 或访问 Unity 工程。
    [Serializable]
    public sealed class ShaderData
    {
        public int formatVersion = 1;
        public string key, sourceName, profile, vertexFile, fragmentFile, descriptorFile;
        public string grabTexture;
        public string geometry;
        public string depthCompare = "LessOrEqual";
        public string colorSpace = "Gamma";
        public string[] keywords = Array.Empty<string>();
        public string[] approximations = Array.Empty<string>();
        public MemberData[] defaults = Array.Empty<MemberData>();
        public bool blend, depthTest = true, depthWrite = true, colorWrite = true, stencil;
        public string srcColor = "One", dstColor = "Zero", srcAlpha = "One", dstAlpha = "Zero";
        public string colorOp = "Add", alphaOp = "Add", cull = "Back";
        public string stencilCompare = "Always", stencilPass = "Keep", stencilFail = "Keep", stencilZFail = "Keep";
        public int stencilRef, stencilReadMask = 255, stencilWriteMask = 255, renderQueue = 2000;
        public float depthBiasConstant, depthBiasSlope;
    }
}
