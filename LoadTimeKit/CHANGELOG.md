# Changelog

## [1.0.0] - 2026-10-20
- Cold-boot timeline from Android process start (else engine start / Editor Play) to `MarkReady`.
- Own logcat tag `LoadTimeKit` (`RESULT` line + timeline), `loadtime.jsonl`, `Ready` / `Finished` events.
- Marks logged live for 20 s after Ready (focus loss from full-screen ads, game marks). Compiled out with
  `PRODUCTION` or `QAKIT_DISABLED`.
