# 0.1.9 Preview · Validation scope

2026-10-16, using RimWorld 1.7.4980 rev60 and .NET 10. PR #4 first merged keeptpa's particle seed and conditional LoadFolders fixes plus publishing improvements, preserving the existing build-path fixes.

This release adds an embedded container with Core, runtime, Spine, built-in Defs/GLSL, bilingual settings text and licenses. C# content uses targets and deferred assemblies; References are compilation-only. Standalone takes priority; identical copies run once; the newest compatible payload is selected and same-version conflicts are reported. Inspector-style Rendering, Runtime and About pages retain Bloom APIs/settings keys. About includes GitHub, Workshop, contributors, participation invitations and full license entries. No separate Unity engine, online update feature or new Unity export option is included.

## Completed checks

Core, runtime, Spine, Embedded, ShaderTool and Unity exporter source compiled successfully. Existing PublishingTests and CatalogPolicyTests passed. Three existing Spine CA2022 warnings and five deprecated particle-enum warnings remain.

`Tools/Verify-Embedded.ps1` uses real CLR assemblies, the game's Mod factory and the Harmony XML hook. It covers single/identical copies, reversed order, standalone first/middle/last, MonoBehaviour and cross-assembly inheritance, Mod ownership, repeated entry, incompatible standalone, newer compatible candidates and same-version conflicts. Native mounting, graphics initialization and the full game type collection are isolated in this offline harness; these checks alone are not in-game verification.

Following the user's additional authorization, `Tools/Test-EmbeddedInGame.ps1` **launched the actual game twice**, each with three embedded test mods: A and B contain cross-assembly scripts, C is resource-only.

| Check | Standalone + A/B/C | A/B/C only |
| --- | --- | --- |
| Active source | Standalone RimUnity | One embedded runtime supplied by A |
| Embedded copies detected | 3 | 3 |
| Runtime Mod and each of the three API assemblies | One each | One each |
| Embedded cache | Not created | Hash-keyed cache |
| Built-in shader and particle ShaderDef | One each | One each |
| Content Def ownership | test.a, test.b, test.c | test.a, test.b, test.c |
| Runtime host, GameObject creation and derived-script attachment | Passed | Passed |

Both reports returned `passed: true`, with no RimUnity loading errors. Logs also contained unrelated system Epic Vulkan Overlay manifest warnings. Tests used separate savedatafolder directories, exited automatically, removed temporary mods and restored the original installed 0.1.8 runtime. The player's regular mod configuration and saves were not changed. The Unity Editor was not launched.

As requested, test saves, caches, temporary fixtures, probes and test build outputs were removed, and pre-test game logs restored. Reproducible verification source and this record remain in the repository; test mods are not distributed.

## Large lists and limits

New folder scans only visit mods already registered as carrying embedded entry assemblies; unrelated and disabled mods are not traversed. Identical payload bytes have one retained copy. Standalone mode does not read/extract embedded payloads. Unnecessary Mod creation passes are skipped when standalone initialization is already complete or there are no deferred scripts.

An offline sample took approximately 121 ms for the full bootstrap with two embedded mods, and 144 ms after adding 5,000 unrelated mod registry entries. This includes JIT, file reads, cache extraction and isolated Mod creation; it is neither a directory-scan-only measurement nor total game startup time. No large-list game launch was performed. Real assembly type counts, disk performance and actual embedded-copy counts affect costs, especially game type-cache refreshes.

Long-running map rendering, every asset combination and visual checks at every UI scale/language remain unverified. Settings were statically checked for measured text height and restored GUI state. This is a preview, not a complete in-game regression certification.
