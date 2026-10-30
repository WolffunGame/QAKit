using System.IO;
using UnityEditor;
using UnityEngine;

namespace QA.PerfKit.Editor
{
    /// <summary>Switch the kit on in the Editor (the flag file), force a test spike, and adb commands for a device.</summary>
    public static class PerfKitMenu
    {
        private const string ROOT = "Tools/PerfKit/";
        private const string TOGGLE = ROOT + "Flag File On";
        private const string SPIKE = ROOT + "Spike 80 ms";

        private static string FlagPath => Path.Combine(Application.persistentDataPath, PerfKit.FlagFileName);

        [MenuItem(TOGGLE)]
        private static void ToggleFlag()
        {
            string path = FlagPath;
            if (File.Exists(path)) File.Delete(path);
            else File.WriteAllText(path, string.Empty);
            Debug.Log($"[PerfKit] flag {(File.Exists(path) ? "created" : "removed")}: {path} (picked up within {PerfKit.POLL_SECONDS:0} s while playing)");
        }

        [MenuItem(TOGGLE, true)]
        private static bool ToggleFlagValidate()
        {
            Menu.SetChecked(TOGGLE, File.Exists(FlagPath));
            return true;
        }

        [MenuItem(SPIKE)]
        private static void Spike()
        {
            PerfSpikeReceiver.Request(PerfSpikeReceiver.DEFAULT_MS);
            Debug.Log(PerfKit.IsOn
                ? $"[PerfKit] spike {PerfSpikeReceiver.DEFAULT_MS} ms on the next frame"
                : $"[PerfKit] spike {PerfSpikeReceiver.DEFAULT_MS} ms queued; it runs once the kit is on (Flag File On)");
        }

        [MenuItem(SPIKE, true)]
        private static bool SpikeValidate() => EditorApplication.isPlaying;

        [MenuItem(ROOT + "Copy adb Commands")]
        private static void CopyAdbCommands()
        {
            string package = PlayerSettings.applicationIdentifier;
            string dir = $"/sdcard/Android/data/{package}/files";
            string text =
                $"adb shell touch {dir}/{PerfKit.FlagFileName}    # on (picked up within 2 s)\n" +
                $"adb logcat -v epoch -s {PerfKit.LogTag}:I        # read\n" +
                $"adb shell am broadcast -a {PerfSpikeReceiver.Action} -p {package} --ei ms {PerfSpikeReceiver.DEFAULT_MS}" +
                "    # one test drop (development build, kit on)\n" +
                $"adb shell rm {dir}/{PerfKit.FlagFileName}       # off\n";
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log("[PerfKit] adb commands copied:\n" + text);
        }
    }
}
