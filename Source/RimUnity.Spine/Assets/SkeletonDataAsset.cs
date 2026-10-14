using System;
using System.IO;
using System.Linq;
using RimUnity;

namespace Spine.Unity;

//骨骼数据和动画混合表按资源共享，实例只持有自己的姿态。
public sealed class SkeletonDataAsset : ScriptableObject
{
    public AtlasAssetBase[] atlasAssets;
    public TextAsset skeletonJSON;
    public bool isBinary;
    public float scale = .01f, defaultMix;
    public string[] fromAnimation = Array.Empty<string>(), toAnimation = Array.Empty<string>();
    public float[] duration = Array.Empty<float>();
    private SkeletonData data;
    private AnimationStateData state;
    public bool IsLoaded => data != null;
    public SkeletonData GetSkeletonData(bool quiet = false)
    {
        if (data != null) return data;
        Atlas[] atlases = atlasAssets.Select(a => a.GetAtlas()).ToArray();
        if (isBinary)
        {
            using var stream = new MemoryStream(skeletonJSON.bytes, false);
            data = new SkeletonBinary(atlases) { Scale = scale }.ReadSkeletonData(stream);
        }
        else data = new SkeletonJson(atlases) { Scale = scale }.ReadSkeletonData(new StringReader(skeletonJSON.text));
        data.Name = name;
        return data;
    }
    public AnimationStateData GetAnimationStateData()
    {
        if (state != null) return state;
        state = new AnimationStateData(GetSkeletonData()) { DefaultMix = defaultMix };
        if (fromAnimation.Length != toAnimation.Length || fromAnimation.Length != duration.Length)
            throw new InvalidDataException("Spine 动画混合表长度不一致：" + name);
        for (int i = 0; i < duration.Length; i++) state.SetMix(fromAnimation[i], toAnimation[i], duration[i]);
        return state;
    }
}
