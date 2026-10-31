# Changelog

## [1.0.3] - 2026-10-31
- `Runtime/link.xml` preserves the assembly: with Managed Stripping Level High, IL2CPP builds dropped it because
  nothing references it (it starts itself through `RuntimeInitializeOnLoadMethod`).
- `[assembly: AlwaysLinkAssembly]` so UnityLinker processes the assembly at all.
- Package version follows the repo tag.

## [1.0.0] - 2026-10-19
- Dormant exception capture switched by a flag file (adb / Testing Tools Box / Editor menu).
- Own logcat tag `ExceptionKit`, file log, `Captured` event. Compiled out with `PRODUCTION` or `QAKIT_DISABLED`.
