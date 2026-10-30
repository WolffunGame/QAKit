# QAKit

QA-only Unity packages for staging builds. Each kit is its own UPM package in a subfolder.
All compile out when `PRODUCTION` or `QAKIT_DISABLED` is defined.

| Package | Folder |
|---|---|
| `com.qa.exceptionkit` | `ExceptionKit/` |
| `com.qa.loadtimekit` | `LoadTimeKit/` |
| `com.qa.perfkit` | `PerfKit/` |

## Install

Add to `Packages/manifest.json` (replace the tag as needed):

```json
"com.qa.exceptionkit": "git@github.com:WolffunGame/QAKit.git?path=/ExceptionKit#1.0.0",
"com.qa.loadtimekit": "git@github.com:WolffunGame/QAKit.git?path=/LoadTimeKit#1.0.2",
"com.qa.perfkit": "git@github.com:WolffunGame/QAKit.git?path=/PerfKit#1.0.2"
```

PerfKit needs **Project Settings > Player > Frame Timing Stats** on for CPU/GPU times (otherwise `cpu/gpu=-1`).
