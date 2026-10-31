# QAKit

QA-only Unity packages for staging builds. Each kit is its own UPM package in a subfolder.
All compile out when `PRODUCTION` or `QAKIT_DISABLED` is defined.

| Package | Folder |
|---|---|
| `com.qa.exceptionkit` | `ExceptionKit/` |
| `com.qa.loadtimekit` | `LoadTimeKit/` |
| `com.qa.perfkit` | `PerfKit/` |

## Install

Add to `Packages/manifest.json` (replace the tag as needed). All three packages share the repo tag; https works without
an SSH key since the repo is public:

```json
"com.qa.exceptionkit": "https://github.com/WolffunGame/QAKit.git?path=/ExceptionKit#1.0.3",
"com.qa.loadtimekit": "https://github.com/WolffunGame/QAKit.git?path=/LoadTimeKit#1.0.3",
"com.qa.perfkit": "https://github.com/WolffunGame/QAKit.git?path=/PerfKit#1.0.3"
```

PerfKit needs **Project Settings > Player > Frame Timing Stats** on for CPU/GPU times (otherwise `cpu/gpu=-1`).

## Builds with code stripping

Each kit ships `Runtime/link.xml`, so Managed Stripping Level Medium / High keeps it although no game code calls it.
PerfKit's spike receiver is Java: if the build is minified (R8), add
`-keep class com.qa.perfkit.PerfKitSpikeReceiver { *; }` to the game's proguard file.
