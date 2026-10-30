using System;
using UnityEngine;

namespace QA.PerfKit
{
    /// <summary>Writes kit lines under the logcat tag <see cref="PerfKit.LogTag"/> (Android) so `adb logcat -s PerfKit`
    /// shows only them. In the Editor they go to the Unity log with a [PerfKit] prefix; other platforms drop them.
    /// Never touches the Unity log on Android (it would double every line under tag `Unity`).</summary>
    internal static class PerfKitOutput
    {
        private static AndroidJavaClass _log;

        /// <summary>Editor Play without domain reload keeps statics, and a stale JNI handle must not outlive Play.</summary>
        public static void Reset() => _log = null;

        public static void Info(string line)
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                try
                {
                    _log ??= new AndroidJavaClass("android.util.Log");
                    _log.CallStatic<int>("i", PerfKit.LogTag, line);
                }
                catch (Exception)
                {
                    // No JNI (e.g. during shutdown): drop the line, never log from here.
                }
                return;
            }

            if (Application.isEditor)
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[{0}] {1}", PerfKit.LogTag, line);
        }
    }
}
