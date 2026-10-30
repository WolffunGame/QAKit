using System.IO;
using UnityEditor;
using UnityEngine;

namespace QA.ExceptionKit.Editor
{
    /// <summary>Editor switch (same flag file as on device) and the adb commands for the current project.</summary>
    public static class ExceptionKitMenu
    {
        private const string ROOT = "Tools/ExceptionKit/";
        private const string TOGGLE = ROOT + "On In Editor";

        private static string EditorFlagPath => Path.Combine(Application.persistentDataPath, ExceptionKit.FlagFileName);

        [MenuItem(TOGGLE)]
        private static void Toggle()
        {
            if (File.Exists(EditorFlagPath)) File.Delete(EditorFlagPath);
            else File.WriteAllText(EditorFlagPath, "");
        }

        [MenuItem(TOGGLE, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(TOGGLE, File.Exists(EditorFlagPath));
            return true;
        }

        [MenuItem(ROOT + "Copy adb Commands")]
        private static void CopyAdbCommands()
        {
            string dir = $"/sdcard/Android/data/{PlayerSettings.applicationIdentifier}/files";
            string text =
                $"adb shell touch {dir}/{ExceptionKit.FlagFileName}        # on (app checks every {ExceptionKit.PollSeconds:0} s)\n" +
                $"adb logcat -s {ExceptionKit.LogTag}                       # read\n" +
                $"adb pull {dir}/{ExceptionKit.LogFileName}            # full stacks\n" +
                $"adb shell rm -f {dir}/{ExceptionKit.FlagFileName}       # off\n";
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log("[ExceptionKit] adb commands copied:\n" + text);
        }

        [MenuItem(ROOT + "Reveal Log File (Editor)")]
        private static void RevealLogFile()
        {
            string path = Path.Combine(Application.persistentDataPath, ExceptionKit.LogFileName);
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else Debug.Log($"[ExceptionKit] No log file yet: {path}");
        }
    }
}
