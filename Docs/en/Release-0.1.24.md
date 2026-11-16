# 0.1.24 Release Notes

- Fix screen-effect resource cleanup crashes after the RimWorld 1.7.5011 post-processing API change, and check API compatibility during startup.
- Keep warmed mesh renderers when Bloom and map HDR rebuild render targets. Attachments are released only after the GPU is idle, then reallocated for the new size.
- Adapt custom UI material submission to the updated UI draw records.
- Refine shader conversion diagnostics: handled integer-power compatibility and screen UI color blending notices become informational logs; shadow passes no longer report color blending differences.
- Add build, documentation sync and packaging entry points requiring only .NET 10 SDK, with Steam/Harmony discovery and local path configuration.

Content mods using the embedded runtime should update their embedded package. Build from source with `build.cmd` on Windows or `sh build.sh` on Linux/macOS; PowerShell is no longer required.
