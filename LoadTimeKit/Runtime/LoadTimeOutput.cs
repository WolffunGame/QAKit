using System;
using UnityEngine;

namespace QA.LoadTimeKit
{
    /// <summary>Writes kit lines under the logcat tag <see cref="LoadTimeKit.LogTag"/> (Android) so `adb logcat -s
    /// LoadTimeKit` shows only them. Elsewhere (Editor, iOS) they go to the Unity log with a [LoadTimeKit] prefix.</summary>
    internal static class LoadTimeOutput
    {
        private static AndroidJavaClass _log;

        public static void Info(string message)
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                try
                {
                    _log ??= new AndroidJavaClass("android.util.Log");
                    _log.CallStatic<int>("i", LoadTimeKit.LogTag, message);
                }
                catch (Exception)
                {
                    // No JNI (e.g. during shutdown): drop the line, never log from here.
                }
                return;
            }

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[{0}] {1}", LoadTimeKit.LogTag, message);
        }
    }
}
