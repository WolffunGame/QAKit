# QA ExceptionKit

Dormant exception capture for QA/staging builds of **any** game. No game references, no dependencies, no setup.

- **Off by default.** Nothing is hooked; the app only checks once every 2 s whether a flag file exists.
- **On** when `<persistentDataPath>/exceptionkit.on` exists. Switch it with adb, Testing Tools Box or the Editor menu.
- While on, each **distinct** exception (deduped by message + first stack line) is written to:
  - logcat tag **`ExceptionKit`** — never the `Unity` tag, so `adb logcat -s Unity` shows nothing from the kit;
  - `<persistentDataPath>/exceptions.log` (512 KB cap);
  - the `ExceptionKit.Captured` C# event (main thread).

## Which builds contain it

| Build | Compiled in |
|---|---|
| Editor, Development, staging (no `PRODUCTION`) | yes, dormant |
| Defines `PRODUCTION` or `QAKIT_DISABLED` | **no** (asmdef define constraint) |

Games whose store build does not define `PRODUCTION` add `QAKIT_DISABLED` to that build.

## Install

Copy this folder into the game, either as `Packages/com.qa.exceptionkit` (embedded package) or under `Assets/`
(e.g. `Assets/QAKit/ExceptionKit`) when the game's `Packages/` must stay untouched:

```powershell
powershell -File tools/sync-kit.ps1 -Project <game> -Package com.qa.exceptionkit -Destination Assets/QAKit/ExceptionKit
```

No scene object, no init call, no code change in the game: it starts itself before the first scene.

## Use (Android)

```bash
PKG=<application id>
adb shell touch /sdcard/Android/data/$PKG/files/exceptionkit.on          # on  (within 2 s, no restart)
adb logcat -s ExceptionKit                                                # read
adb pull /sdcard/Android/data/$PKG/files/exceptions.log                   # full stacks
adb shell rm -f /sdcard/Android/data/$PKG/files/exceptionkit.on          # off
```

Put the line `errors` in the flag file to also capture `LogError` / `Assert`:
`adb shell "echo errors > /sdcard/Android/data/$PKG/files/exceptionkit.on"`.

Or in **Testing Tools Box → Crashlytics Check**, press **ExceptionKit** (switches the flag for the foreground app and
shows the kit lines). Editor: **Tools > ExceptionKit > On In Editor**; **Copy adb Commands** fills in the package id.

Output format:

```
I ExceptionKit: on com.studio.game v1.2.0 | Android | SM-A127F | Android OS 12 / API-31 | errors=False | file=...
E ExceptionKit: #1 NullReferenceException: Object reference not set to an instance of an object.
E ExceptionKit: #1 | Game.Player.Update () (at <000000000>:0)
I ExceptionKit: off captured=1
```

Exceptions thrown before the kit is switched on are not captured (switch it on before the step you test).
Switching on again resets the numbering and dedupe.

## API

Assembly `ExceptionKit.Runtime` is not auto-referenced; reference it explicitly and wrap calls in
`#if !PRODUCTION` (the assembly is absent from production builds).

```csharp
ExceptionKit.Captured += e => overlay.Show($"#{e.Index} {e.Condition}");
bool on = ExceptionKit.IsOn;
IReadOnlyList<CapturedException> all = ExceptionKit.Recent;
```
