# Changelog

## [0.3.0] - 2026-10-30
- PlayMode tests (`Tests/Runtime`, assembly `PerfKit.PlayTests`): host created at startup and hidden, `on`/`off` via
  `PerfKit.Enabled`, `MARK`, a real 80 ms spike frame produces `DROP ... scene= ctx=`, `HB` after 5 s. Green on
  2022.3.62f2 and 6000.3.25f1 in batchmode.
- `tools/pack-kit.ps1` builds `dist/<package>-<version>.tgz` (UPM tarball) and, given a Unity project, a `.unitypackage`.

## [0.2.1] - 2026-10-30
- `PerfKit.Tests` asmdef requires `UNITY_INCLUDE_TESTS`, so the test assembly is left out of player builds and of
  projects that do not enable tests.
- Docs: "no allocation while off" reworded to "no per-frame work while off; one `File.Exists` every 2 s".

## [0.2.0] - 2026-10-30
- Frame-drop detection while on: `ft > tgt * 1.5` logs a `DROP` line with CPU main / render thread and GPU frame times
  (`FrameTimingManager`), RAM, GC, scene, context and the three previous frames' cpu/gpu; at most 10 per rolling second,
  the rest summarised as `DROPBURST`. `gpu=-1` when GPU time is unavailable; cpu / rend / gpu `-1` when Frame Timing
  Stats is off (warned once in the Editor).
- `HB` heartbeat every 5 s with fps and window averages.
- `MARK` names: whitespace, `;` and `=` become `_` (0.1.0 stripped them); scene names likewise.
- Development / Editor only: SPIKE hook, `adb shell am broadcast -a com.qa.perfkit.SPIKE --ei ms 80` or
  `Tools/PerfKit/Spike 80 ms` sleeps the main thread once for an end-to-end test drop
  (`Runtime/Plugins/Android/PerfKitSpikeReceiver.java`).
- `Copy adb Commands` adds the logcat (`-v epoch`) and spike commands.

## [0.1.0] - 2026-10-30
- Package skeleton: hidden runner bootstrapped at `BeforeSceneLoad`, dormant until `<persistentDataPath>/perfkit.on`
  exists (`File.Exists` every 2 s, `on` / `off` lines to logcat tag `PerfKit`), `PerfKit.Enabled` forces it on.
- Public API: `Set` (string / int / float), `Clear`, `Mark`; at most 8 context keys, values up to 32 chars.
- Editor menu `Tools/PerfKit`: toggle the flag file, copy adb commands.
- Frame-drop detection and heartbeat are not in this version.
