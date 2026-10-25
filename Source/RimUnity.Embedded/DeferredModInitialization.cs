using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimUnity.Embedded;

//仅构造延迟载入程序集内的 Mod，避免再次执行其他模组失败后的构造与补丁。
internal static class DeferredModInitialization
{
    //沿用原版实例表，使设置、GetMod 和内容包归属保持一致。
    internal static void Initialize(Assembly assembly, ModContentPack owner)
    {
        var field = AccessTools.Field(typeof(LoadedModManager), "runningModClasses")
            ?? throw new MissingFieldException(typeof(LoadedModManager).FullName, "runningModClasses");
        var instances = (Dictionary<Type, Mod>)field.GetValue(null);
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(Mod).IsAssignableFrom(type) || instances.ContainsKey(type)) continue;
            try { instances.Add(type, (Mod)Activator.CreateInstance(type, owner)); }
            catch (Exception error)
            {
                throw new InvalidOperationException($"[RimUnity] 延迟 Mod 初始化失败：{type.FullName}；所属模组：{owner.PackageId}。", error);
            }
        }
    }
}
