using System;
using UnityEngine;

namespace QA.ExceptionKit
{
    /// <summary>Writes kit lines under the logcat tag <see cref="ExceptionKit.LogTag"/> (Android) so they never show
    /// in `adb logcat -s Unity`. Elsewhere (Editor, iOS) they go to the Unity log with a [ExceptionKit] prefix.</summary>
    internal static class ExceptionKitOutput
    {
        private static AndroidJavaClass _log;

        public static void Info(string message) => Write(false, message);

        public static void Error(string message) => Write(true, message);

        private static void Write(bool error, string message)
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                try
                {
                    _log ??= new AndroidJavaClass("android.util.Log");
                    _log.CallStatic<int>(error ? "e" : "i", ExceptionKit.LogTag, message);
                }
                catch (Exception)
                {
                    // No JNI (e.g. during shutdown): drop the line, never log from here.
                }
                return;
            }

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[{0}] {1}", ExceptionKit.LogTag, message);
        }
    }
}
