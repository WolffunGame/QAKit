# QAKit

QA-only Unity packages for staging builds. Each kit is its own UPM package in a subfolder.
Both compile out when `PRODUCTION` or `QAKIT_DISABLED` is defined.

| Package | Folder |
|---|---|
| `com.qa.exceptionkit` | `ExceptionKit/` |
| `com.qa.loadtimekit` | `LoadTimeKit/` |

## Install

Add to `Packages/manifest.json` (replace the tag as needed):

```json
"com.qa.exceptionkit": "git@github.com:WolffunGame/QAKit.git?path=/ExceptionKit#1.0.0",
"com.qa.loadtimekit": "git@github.com:WolffunGame/QAKit.git?path=/LoadTimeKit#1.0.0"
```
