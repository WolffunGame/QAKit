using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("PerfKit.Tests")]
[assembly: InternalsVisibleTo("PerfKit.Editor")]

namespace QA.PerfKit
{
    /// <summary>
    /// Dormant until the flag file <see cref="FlagFileName"/> exists in Application.persistentDataPath (created by the
    /// Testing Tools Box, `adb shell touch` or the Editor menu) or game code sets <see cref="Enabled"/>. While off the
    /// per-frame cost is one bool test and one `File.Exists` every <see cref="POLL_SECONDS"/> s. Lines go to logcat tag
    /// <see cref="LogTag"/>. Main thread only.
    /// </summary>
    public static class PerfKit
    {
        public const string LogTag = "PerfKit";
        public const string FlagFileName = "perfkit.on";
        public const float POLL_SECONDS = 2f;
        public const int HEARTBEAT_MS = 5000;

        private const string HOST_OBJECT_NAME = "[PerfKit]";

        /// <summary>True while the flag file exists or <see cref="Enabled"/> is forced.</summary>
        public static bool IsOn => _forced || _flagPresent;

        /// <summary>Forces the kit on from game code. False means "follow the flag file", not "force off".</summary>
        public static bool Enabled
        {
            get => _forced;
            set
            {
                _forced = value;
                Announce();
            }
        }

        public static string FlagFilePath { get; private set; }

        internal static readonly PerfContext Context = new PerfContext();

        private const int SLOW_TICK_MS = 1000;

        private static readonly PerfSampler Sampler = new PerfSampler();
        private static readonly PerfLineBuilder Lines = new PerfLineBuilder();

        private static bool _forced;
        private static bool _flagPresent;
        private static bool _announcedOn;

        // Measuring state; reset when the kit switches on.
        private static bool _measuring;
        private static string _scene;          // sanitised once per scene change
        private static long _nextSlowMs;
        private static long _hbStartMs;
        private static int _hbFrames;
        private static double _hbCpu, _hbRend, _hbGpu;
        private static int _hbCpuN, _hbRendN, _hbGpuN;

        /// <summary>Editor Play without domain reload keeps statics from the previous session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _forced = false;
            _flagPresent = false;
            _announcedOn = false;
            _measuring = false;
            _scene = null;
            FlagFilePath = null;
            Context.ClearAll();
            PerfKitOutput.Reset();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            PerfSpikeReceiver.Reset();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            FlagFilePath = Path.Combine(Application.persistentDataPath, FlagFileName);
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;   // Play without domain reload keeps handlers
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            // HideInHierarchy (not HideAndDontSave) so the Editor destroys it when Play mode ends.
            var host = new GameObject(HOST_OBJECT_NAME) { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<PerfKitRunner>();
        }

        /// <summary>Context shown as `ctx=` on kit lines. Set before the kit is on is remembered, so the first line is
        /// already right. Call it when the value changes, not every frame (int / float build a string).</summary>
        public static void Set(string key, string value) => Context.Set(key, value);

        public static void Set(string key, int value) => Context.Set(key, value.ToString(CultureInfo.InvariantCulture));

        public static void Set(string key, float value) => Context.Set(key, value.ToString("0.##", CultureInfo.InvariantCulture));

        public static void Clear(string key) => Context.Clear(key);

        /// <summary>Logs one `MARK t=... name=...` line for a game event ("boss spawned"). No-op while off.</summary>
        public static void Mark(string name)
        {
            if (!IsOn) return;
            PerfKitOutput.Info(Lines.Mark(NowMs(), name));
        }

        /// <summary>Called by the runner every frame. Returns at once while the kit is off. While on: one frame
        /// sample, drop check (rate limited, overflow summarised as DROPBURST), heartbeat every
        /// <see cref="HEARTBEAT_MS"/>. A normal frame allocates nothing; strings are built only for a line.</summary>
        internal static void Tick()
        {
            if (!IsOn)
            {
                _measuring = false;
                return;
            }

            long nowMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);   // monotonic, for scheduling only
            if (!_measuring) StartMeasuring(nowMs);

            Sampler.BeginFrame();
            Sampler.TryRead(out FrameSample s);

            float tgt = Sampler.TargetFrameMs;
            if (Lines.ShouldLogDrop(s.ftMs, tgt) && Lines.AllowDrop(nowMs, s.ftMs))
            {
                s.ramMb = PerfSampler.ReadRamMb();
                PerfKitOutput.Info(Lines.Drop(NowMs(), in s, tgt, _scene, Context));
            }
            Lines.Push(in s);

            if (Lines.TakeBurst(nowMs, out int n, out float worst))
                PerfKitOutput.Info(Lines.Burst(NowMs(), n, worst));

            _hbFrames++;
            if (s.cpuMs >= 0f) { _hbCpu += s.cpuMs; _hbCpuN++; }
            if (s.rendMs >= 0f) { _hbRend += s.rendMs; _hbRendN++; }
            if (s.gpuMs >= 0f) { _hbGpu += s.gpuMs; _hbGpuN++; }
            if (nowMs - _hbStartMs >= HEARTBEAT_MS) WriteHeartbeat(nowMs);

            if (nowMs >= _nextSlowMs)
            {
                _nextSlowMs = nowMs + SLOW_TICK_MS;
                Sampler.RefreshTarget();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                PerfSpikeReceiver.Poll();
#endif
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            PerfSpikeReceiver.ApplyPending();   // after measuring: the next frame's ft carries the sleep
#endif
        }

        private static void StartMeasuring(long nowMs)
        {
            _measuring = true;
            Sampler.Reset();
            Sampler.RefreshTarget();
            Lines.Reset();
            _nextSlowMs = nowMs + SLOW_TICK_MS;
            ResetHeartbeat(nowMs);
            if (_scene == null) _scene = PerfLineBuilder.SafeName(SceneManager.GetActiveScene().name);
        }

        private static void WriteHeartbeat(long nowMs)
        {
            float seconds = (nowMs - _hbStartMs) / 1000f;
            var avg = new FrameSample
            {
                cpuMs = _hbCpuN > 0 ? (float)(_hbCpu / _hbCpuN) : -1f,
                rendMs = _hbRendN > 0 ? (float)(_hbRend / _hbRendN) : -1f,
                gpuMs = _hbGpuN > 0 ? (float)(_hbGpu / _hbGpuN) : -1f,
                ramMb = PerfSampler.ReadRamMb(),
            };
            PerfKitOutput.Info(Lines.Heartbeat(NowMs(), seconds > 0f ? _hbFrames / seconds : 0f, in avg, _scene, Context));
            ResetHeartbeat(nowMs);
        }

        private static void ResetHeartbeat(long nowMs)
        {
            _hbStartMs = nowMs;
            _hbFrames = 0;
            _hbCpu = _hbRend = _hbGpu = 0.0;
            _hbCpuN = _hbRendN = _hbGpuN = 0;
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next) => _scene = PerfLineBuilder.SafeName(next.name);

        /// <summary>Called by the runner every <see cref="POLL_SECONDS"/> s.</summary>
        internal static void PollFlag()
        {
            _flagPresent = FlagExists();
            Announce();
        }

        private static bool FlagExists()
        {
            try { return !string.IsNullOrEmpty(FlagFilePath) && File.Exists(FlagFilePath); }
            catch (Exception) { return false; }
        }

        /// <summary>Logs `on` / `off` when <see cref="IsOn"/> changed since the last line.</summary>
        private static void Announce()
        {
            bool on = IsOn;
            if (on == _announcedOn) return;
            _announcedOn = on;

            if (on)
            {
                PerfKitOutput.Info($"on {Application.identifier} v{Application.version} | {Application.platform} | " +
                                   $"{SystemInfo.deviceModel} | forced={_forced}");
            }
            else
            {
                PerfKitOutput.Info("off");
            }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
