# QA PerfKit

Frame-drop context for QA/staging builds of **any** game. No game references, no dependencies.

- **Dormant until switched on.** The kit does nothing until `<persistentDataPath>/perfkit.on` exists (the Testing Tools
  Box creates it at session start and removes it at Stop; or `adb shell touch`; or `Tools/PerfKit/Flag File On` in the
  Editor), or game code sets `PerfKit.Enabled = true`. While off: no per-frame work (`Update` returns on a `bool`);
  one `File.Exists` every 2 s.
- **Output** goes to its own logcat tag, `adb logcat -s PerfKit`. Lines `on ...` / `off` mark the switch. In the
  Editor the same text goes to the Unity log with a `[PerfKit]` prefix; the Android path never uses the Unity log.
- **While on**, every dropped frame (`ft > target * 1.5`) is logged with CPU / render / GPU times, RAM, GC, scene and
  context, plus a heartbeat every 5 s. A normal frame allocates nothing. See [CHANGELOG](CHANGELOG.md).

## Game-side API (namespace `QA.PerfKit`)

```csharp
PerfKit.Set("screen", "Shop");   // string / int / float; at most 8 keys, values up to 32 chars
PerfKit.Set("wave", 5);          // `;` `=` and whitespace are stripped (they would break the line)
PerfKit.Clear("wave");
PerfKit.Mark("boss spawned");    // one `MARK t=<ms> name=boss_spawned` line; no-op while off
PerfKit.Enabled = true;          // force on without the flag; false = follow the flag file
bool on = PerfKit.IsOn;
```

Call `Set` when a value changes, not every frame (`int` / `float` build a string). Values set while the kit is off are
kept, so the first line after switch-on already carries the right context. Main thread only.

## Line contract (Testing Tools Box parses this)

One line per event under tag `PerfKit`, whitespace-separated `key=value`, numbers with one decimal (invariant), `-1` =
not available, `t` = device wall-clock Unix ms:

```
DROP t=1730264954285 ft=98.0 tgt=33.3 efps=10 cpu=61.2 rend=20.1 gpu=88.4 ram=512 gc=1 scene=Level_12 ctx=screen:Shop;wave:5 prev=33.4/30.1,34.0/31.2,52.3/60.8
HB t=1730264955000 fps=30 cpu=12.1 rend=8.0 gpu=14.3 ram=510 scene=Level_12 ctx=screen:Shop;wave:5
DROPBURST t=1730264956000 n=37 worst=210.4
MARK t=1730264957000 name=BossSpawn
```

| Key | Meaning |
|---|---|
| `ft` / `tgt` | frame time (`Time.unscaledDeltaTime`) / target frame time, ms. Drop = `ft > tgt * 1.5` |
| `efps` | `(int)(1000 / ft)` |
| `cpu` `rend` `gpu` | `FrameTimingManager` main thread, render thread, GPU ms (a frame 1-2 frames old). All `-1` when Player Settings *Frame Timing Stats* is off; `gpu=-1` after 3 zero / unchanged GPU timings |
| `ram` | `Profiler.GetTotalAllocatedMemoryLong()` MB |
| `gc` | 1 if a gen-0 GC ran since the previous frame |
| `scene` | active scene, whitespace / `;` / `=` replaced by `_`, `-` when empty |
| `ctx` | `PerfKit.Set` pairs `k:v;k:v`, may be empty |
| `prev` | the three previous frames' `cpu/gpu`, oldest first, `-` when none |
| `fps` (HB) | frames / seconds over the 5 s window; cpu / rend / gpu are window averages |
| `n` / `worst` | DROPBURST: drops refused by the 10-per-second limit, and their worst `ft` |

Target: `vSyncCount` on desktop / Editor, else `Application.targetFrameRate`, else 30 on Android / iOS (Unity's
platform default) or the display rate; re-read once a second. Turn on *Frame Timing Stats* in the game, otherwise
only `ft` is real.

## Test spike (development builds and the Editor)

```
adb shell am broadcast -a com.qa.perfkit.SPIKE -p <package> --ei ms 80
```

While the kit is on, the next frame sleeps the main thread for `ms` (default 80, max 2000), giving one real drop for an
end-to-end check. In the Editor: `Tools/PerfKit/Spike 80 ms` (Play mode). The broadcast is read by
`Runtime/Plugins/Android/PerfKitSpikeReceiver.java`, registered the first time the kit is on in a development build and
polled once a second. The Java class is in every Android build, but nothing registers it outside development builds.
If the build is minified (R8), keep `com.qa.perfkit.PerfKitSpikeReceiver` or the hook logs `spike receiver
unavailable` and stays off.

## Which builds contain it

| Build | Compiled in |
|---|---|
| Editor, Development, staging (no `PRODUCTION`) | yes |
| Defines `PRODUCTION` or `QAKIT_DISABLED` | **no** (asmdef define constraint) |

The assembly is not auto-referenced: game code that calls the API sits in its own asmdef that references
`PerfKit.Runtime` and carries the same define constraints.

## Compile check (QA Kit repo)

```powershell
# Editor view (default defines UNITY_2022_3_OR_NEWER;UNITY_EDITOR)
powershell -File tools/build-check.ps1 -Project <game> -Package com.qa.perfkit -Assemblies Runtime,Editor,Tests
# Android development player: the UNITY_ANDROID && !UNITY_EDITOR branches (JNI output, spike receiver)
powershell -File tools/build-check.ps1 -Project <2022.3 game> -Package com.qa.perfkit -Assemblies Runtime `
    -Defines "UNITY_2022_3_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_ANDROID;DEVELOPMENT_BUILD"
# Same on a Unity 6 game: add UNITY_6000_0_OR_NEWER (AndroidJNI API differences)
powershell -File tools/build-check.ps1 -Project <Unity 6 game> -Package com.qa.perfkit -Assemblies Runtime `
    -Defines "UNITY_6000_0_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_ANDROID;DEVELOPMENT_BUILD"
```

`-Defines` replaces the default list (`QA_AUTOMATION` and the per-assembly defines are always added). The Android passes
compile only `Runtime`: without `UNITY_EDITOR` the Editor and Tests assemblies do not apply.

## Install

```powershell
powershell -File tools/sync-kit.ps1 -Project <game> -Package com.qa.perfkit -Destination Assets/QAKit/PerfKit
```

Never edit the synced copy inside a game; change the kit in the QA Kit repo and sync again.

Without the repo: `tools/pack-kit.ps1` writes `dist/com.qa.perfkit-<version>.tgz` (Package Manager > **Add package from
tarball**, or `"com.qa.perfkit": "file:../path/com.qa.perfkit-<version>.tgz"` in `Packages/manifest.json`) and
`dist/com.qa.perfkit-<version>.unitypackage` (Assets > **Import Package > Custom Package**, lands in
`Assets/QAKit/PerfKit`). Same files and GUIDs as sync-kit.

## Tests

`Tests/Editor` (EditMode: line builder, context) and `Tests/Runtime` (PlayMode: on/off, MARK, a real spike frame → DROP
with scene and ctx, HB after 5 s). Both assemblies compile only when `com.unity.test-framework` is in the project
(`versionDefines` → `PERFKIT_HAS_TEST_FRAMEWORK`), so a game without it gets no NUnit errors. Run from Window > General
> Test Runner, or in batchmode: `Unity.exe -batchmode -projectPath <proj> -runTests -testPlatform PlayMode -testResults
r.xml` with `"testables": ["com.qa.perfkit"]` in `manifest.json`.
