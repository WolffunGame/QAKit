# Changelog

## [1.1.0] - 2026-10-30
- `errorList` in the result: every distinct Error / Exception / Assert before Ready with `type`, `ms` since T0,
  `message` (≤ 600 chars), `stack` (≤ 800 chars) and `count`; at most 15 entries, the rest in `errorsDropped`.
- Errors are timed off the main thread with a Stopwatch (log callbacks run on any thread).
- `loadtime.jsonl` rotates at 2 MB instead of 256 KB so error-heavy launches do not reset the run counter.

## [1.0.0] - 2026-10-20
- Cold-boot timeline from Android process start (else engine start / Editor Play) to `MarkReady`.
- Own logcat tag `LoadTimeKit` (`RESULT` line + timeline), `loadtime.jsonl`, `Ready` / `Finished` events.
- Marks logged live for 20 s after Ready (focus loss from full-screen ads, game marks). Compiled out with
  `PRODUCTION` or `QAKIT_DISABLED`.
