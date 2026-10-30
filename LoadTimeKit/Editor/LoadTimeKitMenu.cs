using System.IO;
using UnityEditor;
using UnityEngine;

namespace QA.LoadTimeKit.Editor
{
    /// <summary>adb commands for a cold-boot measurement of the current project, and the Editor log file.</summary>
    public static class LoadTimeKitMenu
    {
        private const string ROOT = "Tools/LoadTimeKit/";

        [MenuItem(ROOT + "Copy adb Commands")]
        private static void CopyAdbCommands()
        {
            string package = PlayerSettings.applicationIdentifier;
            string dir = $"/sdcard/Android/data/{package}/files";
            string text =
                $"adb shell am force-stop {package}                                   # cold start: kill first\n" +
                $"adb logcat -c\n" +
                $"adb shell monkey -p {package} -c android.intent.category.LAUNCHER 1  # launch like a tap on the icon\n" +
                $"adb logcat -s {LoadTimeKit.LogTag}                                   # read (RESULT line = the number)\n" +
                $"adb pull {dir}/{LoadTimeKit.LogFileName}                             # one JSON line per launch\n";
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log("[LoadTimeKit] adb commands copied:\n" + text);
        }

        [MenuItem(ROOT + "Reveal Log File (Editor)")]
        private static void RevealLogFile()
        {
            string path = Path.Combine(Application.persistentDataPath, LoadTimeKit.LogFileName);
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else Debug.Log($"[LoadTimeKit] No log file yet: {path}");
        }
    }
}
