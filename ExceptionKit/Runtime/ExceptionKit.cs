using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QA.ExceptionKit
{
    /// <summary>
    /// Dormant until the flag file <see cref="FlagFileName"/> exists in Application.persistentDataPath (created by
    /// `adb shell touch`, Testing Tools Box or the Editor menu). While on: every distinct exception becomes one line
    /// `#n Type: message` plus `#n | frame` lines under logcat tag <see cref="LogTag"/>, and a block in
    /// <see cref="LogFileName"/>. While off: nothing is hooked; one File.Exists every <see cref="PollSeconds"/> s.
    /// </summary>
    public static class ExceptionKit
    {
        public const string LogTag = "ExceptionKit";
        public const string FlagFileName = "exceptionkit.on";
        public const string LogFileName = "exceptions.log";
        /// <summary>Flag file line that also captures LogError / Assert.</summary>
        public const string OptionErrors = "errors";

        public const float PollSeconds = 2f;
        private const float TICK_SECONDS = 0.5f;
        private const int MAX_PER_SESSION = 100;
        private const int MAX_STACK_LINES = 30;
        private const long MAX_FILE_BYTES = 512 * 1024;
        private const string HOST_OBJECT_NAME = "[ExceptionKit]";

        /// <summary>Raised on the main thread (≤0.5 s later) for each new distinct exception while the kit is on.</summary>
        public static event Action<CapturedException> Captured;

        public static bool IsOn { get; private set; }
        public static int CapturedCount => _count;
        public static string FlagFilePath { get; private set; }
        public static string LogFilePath { get; private set; }

        /// <summary>Distinct exceptions since the kit was last switched on, oldest first.</summary>
        public static IReadOnlyList<CapturedException> Recent
        {
            get { lock (History) return History.ToArray(); }
        }

        private static readonly ConcurrentQueue<CapturedException> Pending = new ConcurrentQueue<CapturedException>();
        private static readonly List<CapturedException> History = new List<CapturedException>();
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static volatile bool _includeErrors;
        private static int _count;
        private static string _header;

        /// <summary>Editor Play without domain reload keeps statics from the previous session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
            IsOn = false;
            ClearCaptured();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            FlagFilePath = Path.Combine(Application.persistentDataPath, FlagFileName);
            LogFilePath = Path.Combine(Application.persistentDataPath, LogFileName);
            _header = $"{Application.identifier} v{Application.version} | {Application.platform} | " +
                      $"{SystemInfo.deviceModel} | {SystemInfo.operatingSystem}";

            // HideInHierarchy (not HideAndDontSave) so the Editor destroys it when Play mode ends.
            var host = new GameObject(HOST_OBJECT_NAME) { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ExceptionKitRunner>().StartCoroutine(MainLoop());
        }

        private static IEnumerator MainLoop()
        {
            var tick = new WaitForSecondsRealtime(TICK_SECONDS);
            var poll = new WaitForSecondsRealtime(PollSeconds);
            float nextPoll = 0f;

            while (true)
            {
                if (Time.realtimeSinceStartup >= nextPoll)
                {
                    nextPoll = Time.realtimeSinceStartup + PollSeconds;
                    bool wanted = FlagExists();
                    if (wanted && !IsOn) SwitchOn();
                    else if (!wanted && IsOn) SwitchOff();
                }

                if (IsOn) Drain();
                yield return IsOn ? tick : poll;
            }
        }

        private static bool FlagExists()
        {
            try { return File.Exists(FlagFilePath); }
            catch (Exception) { return false; }
        }

        private static void SwitchOn()
        {
            _includeErrors = ReadOptions().Contains(OptionErrors);
            ClearCaptured();
            Application.logMessageReceivedThreaded += HandleLog;
            IsOn = true;
            ExceptionKitOutput.Info($"on {_header} | errors={_includeErrors} | file={LogFilePath}");
        }

        private static void SwitchOff()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
            Drain();
            IsOn = false;
            ExceptionKitOutput.Info($"off captured={_count}");
        }

        private static HashSet<string> ReadOptions()
        {
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string line in File.ReadAllLines(FlagFilePath))
                    if (!string.IsNullOrWhiteSpace(line)) options.Add(line.Trim());
            }
            catch (Exception) { }
            return options;
        }

        private static void ClearCaptured()
        {
            while (Pending.TryDequeue(out _)) { }
            lock (History) History.Clear();
            lock (Seen)
            {
                Seen.Clear();
                _count = 0;
            }
        }

        // Any thread. Must never log: Unity drops logs issued inside a log callback, and a kit line here could recurse.
        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            bool wanted = type == LogType.Exception ||
                          (_includeErrors && (type == LogType.Error || type == LogType.Assert));
            if (!wanted) return;

            // Condition + first stack line identifies "the same" exception thrown every frame.
            int firstLineEnd = stackTrace.IndexOf('\n');
            int hash = (condition + (firstLineEnd > 0 ? stackTrace.Substring(0, firstLineEnd) : stackTrace)).GetHashCode();
            int index;
            lock (Seen)
            {
                if (_count >= MAX_PER_SESSION || !Seen.Add(hash)) return;
                index = ++_count;
            }

            var captured = new CapturedException(index, DateTime.Now, type, condition, stackTrace);
            Pending.Enqueue(captured);
            lock (History) History.Add(captured);
        }

        private static void Drain()
        {
            while (Pending.TryDequeue(out var e))
            {
                ExceptionKitOutput.Error($"#{e.Index} {e.Condition}");
                string[] frames = e.StackTrace.Split('\n');
                int written = 0;
                foreach (string frame in frames)
                {
                    if (string.IsNullOrWhiteSpace(frame)) continue;
                    if (++written > MAX_STACK_LINES) break;
                    ExceptionKitOutput.Error($"#{e.Index} | {frame.TrimEnd()}");
                }

                AppendFile($"#{e.Index} [{e.Time:HH:mm:ss}] {e.Type} {e.Condition}\n{e.StackTrace.TrimEnd()}\n\n");

                try { Captured?.Invoke(e); }
                catch (Exception ex) { ExceptionKitOutput.Info($"Captured handler threw: {ex.Message}"); }
            }
        }

        private static void AppendFile(string text)
        {
            try
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MAX_FILE_BYTES) File.Delete(LogFilePath);
                File.AppendAllText(LogFilePath, text);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
