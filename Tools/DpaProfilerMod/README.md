# RimUnity DPA Profiles

用于 RimWorld 1.7、Dubs Performance Analyzer 和内置 RimUnity。独立版 RimUnity 会通过根目录的 `Analyzer.xml` 接入 DPA。

将 `RIMWORLD_DIR` 指向包含游戏真实 `Assembly-CSharp.dll` 的 RimWorld 1.7 目录，在仓库根目录构建。

```powershell
dotnet build Tools/DpaProfilerMod -c Release
```

在游戏的 `Mods/RimUnity DPA Profiles` 下放入本目录的 `About/About.xml`，将生成的 `RimUnity.DpaProfiler.dll` 放入 `Assemblies`。启用该模组并重启后，DPA 的 **Modder（模组作者）** 页会显示 RimUnity 采样项。修改仓库根目录的 `Analyzer.xml` 后重新构建即可更新采样项。
