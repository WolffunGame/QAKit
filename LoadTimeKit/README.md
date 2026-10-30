# QA LoadTimeKit

Cold-boot load time per device, for QA/staging builds of **any** game. No game references, no dependencies.

- **Always recording** in builds that contain it: the boot is over before anyone could switch a flag on. Cost: a few
  timestamps in memory, silent until Ready.
- **T0** = Android process start (`Process.getStartRequestedElapsedRealtime`, API 33+, else
  `getStartElapsedRealtime`, API 24+), so the number includes engine start and Unity splash. Elsewhere: engine start;
  Editor: entering Play. A process older than 60 s when Unity starts (woken earlier by a push receiver) falls back to
  engine start.
- **Ready** = the game's adapter calls `LoadTimeKit.MarkReady(label)` when the player can use the first screen.
  Without an adapter the launch ends as `status=timeout` after 180 s, with the scene timeline only.
- Recorded marks: `process_start`, `engine_init`, `splash_start`, `first_scene_loading`, `first_frame`, every
  `scene_loaded` / `scene_unloaded` / `active_scene`, `focus_lost` / `focus_gained`, `paused` / `resumed`, plus any
  `LoadTimeKit.Mark(name)` from the adapter. Errors / exceptions logged before Ready are counted.
- For 20 s after Ready, new marks are logged live as `after ...` (App Open Ad = `focus_lost`, first popups).

## Which builds contain it

| Build | Compiled in |
|---|---|
| Editor, Development, staging (no `PRODUCTION`) | yes |
| Defines `PRODUCTION` or `QAKIT_DISABLED` | **no** (asmdef define constraint) |

## Install

```powershell
powershell -File tools/sync-kit.ps1 -Project <game> -Package com.qa.loadtimekit -Destination Assets/QAKit/LoadTimeKit
```

Then add a small adapter in the game (see below). The adapter lives in the game, outside the synced folder.

## Measure (Android)

Measure on a staging/dev build on the real device; Editor numbers are not representative.

```bash
PKG=<application id>
adb shell am force-stop $PKG                                   # cold start: kill first, every run
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1  # launch like a tap on the icon
adb logcat -s LoadTimeKit                                      # all kit lines
adb logcat -s LoadTimeKit | grep RESULT                        # only the numbers
adb pull /sdcard/Android/data/$PKG/files/loadtime.jsonl        # one JSON line per launch
```

**Tools > LoadTimeKit > Copy adb Commands** fills in the package id.

Output:

```
I LoadTimeKit: RESULT ready=6421ms status=ok reached=Home | SM-A127F | Android OS 12 / API-31 | RAM 3712MB | com.studio.game v1.8.0 | t0=process run=3 errors=0
I LoadTimeKit:       0ms  +     0  process_start
I LoadTimeKit:     412ms  +   412  engine_init
I LoadTimeKit:     430ms  +    18  splash_start
I LoadTimeKit:    1890ms  +  1460  first_scene_loading
I LoadTimeKit:    2034ms  +   144  scene_loaded Logo Single
...
I LoadTimeKit:    6421ms  +    12  ready Home
I LoadTimeKit: after    7624ms  +  1203  focus_lost        <- ms since T0, +ms since Ready
I LoadTimeKit: end marks=31
```

Reading the numbers:
- `run=1` is the first launch after install / clear data (usually slower: caches, first downloads). Compare like
  with like.
- `errors>0` means something failed during boot (see `first error:`); that launch may be degraded.
- Cross-check T0 with Android's own number: `adb shell am start -W -n $PKG/<activity>` prints `TotalTime`
  (until the first frame, so it is shorter than `ready`).

## Adapter (per game)

Reference `LoadTimeKit.Runtime` from a game-side assembly that also has `!PRODUCTION` + `!QAKIT_DISABLED` define
constraints (the kit assembly is absent from production builds), then call:

```csharp
LoadTimeKit.Mark("loading_started");                                  // optional game milestones
LoadTimeKit.MarkReady(SceneManager.GetActiveScene().name);            // loading screen gone, player can tap
LoadTimeKit.Finished += Unhook;                                       // stop watching the game
```

Pick Ready as the moment the game itself treats loading as over (its loading screen hidden), not "scene loaded".

## API

```csharp
LoadTimeKit.Ready += r => Debug.Log($"{r.readyMs} ms on {r.device}");
LoadTimeResult last = LoadTimeKit.Result;   // null before Ready
bool recording = LoadTimeKit.IsRecording;   // false after the post-Ready window
```
