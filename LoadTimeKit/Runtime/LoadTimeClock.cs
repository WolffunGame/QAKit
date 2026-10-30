using System;
using UnityEngine;

namespace QA.LoadTimeKit
{
    /// <summary>Boot clock. On Android T0 is the moment the OS started (or was asked to start) the process, so the
    /// number includes zygote fork, Unity engine start and splash. Player elsewhere: Unity engine start. Editor:
    /// entering Play (realtimeSinceStartup counts from Editor launch there).</summary>
    internal static class LoadTimeClock
    {
        public const string T0_PROCESS = "process";
        public const string T0_ENGINE = "engine";
        public const string T0_PLAY = "play";

        // A process older than this when Unity starts was woken earlier by something else (push receiver, service),
        // so its start time says nothing about this launch.
        private const long MAX_PLAUSIBLE_PROCESS_AGE_MS = 60_000;

        public static string T0 { get; private set; } = T0_ENGINE;
        /// <summary>Raw Android process age at <see cref="Init"/>; -1 when unknown.</summary>
        public static long RawProcessAgeMs { get; private set; } = -1;

        private static double _initRealtime;
        private static double _processAgeAtInitMs;

        public static void Init()
        {
            _initRealtime = Time.realtimeSinceStartupAsDouble;
            RawProcessAgeMs = ReadAndroidProcessAgeMs();

            bool usable = RawProcessAgeMs >= 0 && RawProcessAgeMs <= MAX_PLAUSIBLE_PROCESS_AGE_MS;
            if (usable) T0 = T0_PROCESS;
            else T0 = Application.isEditor ? T0_PLAY : T0_ENGINE;
            _processAgeAtInitMs = usable ? RawProcessAgeMs : 0;
        }

        public static double SecondsSinceInit => Time.realtimeSinceStartupAsDouble - _initRealtime;

        public static int NowMs()
        {
            double ms = T0 == T0_ENGINE
                ? Time.realtimeSinceStartupAsDouble * 1000.0
                : _processAgeAtInitMs + SecondsSinceInit * 1000.0;
            return (int)Math.Round(ms);
        }

        private static long ReadAndroidProcessAgeMs()
        {
            if (Application.platform != RuntimePlatform.Android) return -1;

            try
            {
                using var process = new AndroidJavaClass("android.os.Process");
                using var clock = new AndroidJavaClass("android.os.SystemClock");
                long start = CallLong(process, "getStartRequestedElapsedRealtime"); // API 33: includes zygote fork
                if (start <= 0) start = CallLong(process, "getStartElapsedRealtime"); // API 24
                if (start <= 0) return -1;
                return clock.CallStatic<long>("elapsedRealtime") - start;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static long CallLong(AndroidJavaClass target, string method)
        {
            try { return target.CallStatic<long>(method); }
            catch (Exception) { return -1; } // NoSuchMethodError on older API levels.
        }
    }
}
