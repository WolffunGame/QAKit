#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;

namespace QA.PerfKit
{
    /// <summary>
    /// Development-only test hook: produces one real dropped frame on demand by sleeping the main thread.
    /// Android: `adb shell am broadcast -a com.qa.perfkit.SPIKE --ei ms 80` reaches the Java receiver
    /// (Plugins/Android/PerfKitSpikeReceiver.java, registered on first use), which stores the request; it is read here
    /// about once a second through a cached JNI method id (no per-frame JNI, no allocation). Editor: menu
    /// Tools/PerfKit/Spike 80 ms. Applied only while the kit is on. The whole class is compiled out of
    /// non-development players.
    /// </summary>
    internal static class PerfSpikeReceiver
    {
        public const string Action = "com.qa.perfkit.SPIKE";
        public const int DEFAULT_MS = 80;
        public const int MAX_MS = 2000;

        private static int _pendingMs;

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string JAVA_CLASS = "com.qa.perfkit.PerfKitSpikeReceiver";
        private static readonly jvalue[] NoArgs = new jvalue[0];
        private static AndroidJavaClass _receiverClass;   // keeps the global class ref alive
        private static IntPtr _takeMethod;
        private static bool _registerTried;
#endif

        /// <summary>Asks for one main-thread sleep of <paramref name="ms"/> (clamped to 1..<see cref="MAX_MS"/>) on
        /// the next kit frame.</summary>
        public static void Request(int ms) => _pendingMs = ms <= 0 ? 0 : Math.Min(ms, MAX_MS);

        public static int PendingMs => _pendingMs;

        /// <summary>Called about once a second while the kit is on: registers the receiver the first time, then
        /// collects a broadcast request.</summary>
        public static void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_registerTried) Register();
            if (_takeMethod == IntPtr.Zero) return;
            try
            {
                int ms = AndroidJNI.CallStaticIntMethod(_receiverClass.GetRawClass(), _takeMethod, NoArgs);
                if (ms > 0) Request(ms);
            }
            catch (Exception)
            {
                _takeMethod = IntPtr.Zero;   // JNI gone: stop asking
            }
#endif
        }

        /// <summary>Called at the end of the kit frame: sleeps once if a spike is pending.</summary>
        public static void ApplyPending()
        {
            int ms = _pendingMs;
            if (ms <= 0) return;
            _pendingMs = 0;
            Thread.Sleep(ms);
        }

        /// <summary>Drops a pending request. The Java receiver stays registered for the process lifetime.</summary>
        public static void Reset() => _pendingMs = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void Register()
        {
            _registerTried = true;
            try
            {
                // Not disposed: on Unity 6 the context object is Unity's own cached instance.
                AndroidJavaObject context = CurrentContext();
                _receiverClass = new AndroidJavaClass(JAVA_CLASS);
                _receiverClass.CallStatic<bool>("register", context);
                _takeMethod = AndroidJNIHelper.GetMethodID(_receiverClass.GetRawClass(), "take", "()I", true);
                PerfKitOutput.Info("spike receiver on: " + Action);
            }
            catch (Exception e)
            {
                // Class stripped (minify) or no context: the hook is unavailable, the kit keeps working.
                _takeMethod = IntPtr.Zero;
                PerfKitOutput.Info("spike receiver unavailable: " + e.GetType().Name);
            }
        }

        private static AndroidJavaObject CurrentContext()
        {
#if UNITY_6000_0_OR_NEWER
            return UnityEngine.Android.AndroidApplication.currentContext;
#else
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
#endif
        }
#endif
    }
}
#endif
