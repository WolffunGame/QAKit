using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QA.LoadTimeKit
{
    /// <summary>
    /// Records the boot timeline from T0 (Android process start, else engine start) until the game calls
    /// <see cref="MarkReady"/>. Silent while booting so it does not slow what it measures; at Ready it writes one
    /// `RESULT` line plus the timeline under logcat tag <see cref="LogTag"/> and one JSON line to
    /// <see cref="LogFileName"/>. Marks for <see cref="AFTER_READY_SECONDS"/> more seconds are logged live (ads,
    /// first popups), then everything is unhooked. Main thread only.
    /// </summary>
    public static class LoadTimeKit
    {
        public const string LogTag = "LoadTimeKit";
        public const string LogFileName = "loadtime.jsonl";

        /// <summary>No Ready by then: the launch is reported as <see cref="LoadTimeResult.STATUS_TIMEOUT"/>.</summary>
        public const float READY_TIMEOUT_SECONDS = 180f;
        /// <summary>How long after Ready marks are still logged (App Open Ad, first popups).</summary>
        public const float AFTER_READY_SECONDS = 20f;

        private const int MAX_MARKS = 200;
        private const int MAX_ERROR_CHARS = 200;
        private const long MAX_FILE_BYTES = 256 * 1024;
        private const string HOST_OBJECT_NAME = "[LoadTimeKit]";

        /// <summary>Raised once per launch at Ready (or timeout), main thread.</summary>
        public static event Action<LoadTimeResult> Ready;
        /// <summary>Raised when the kit stops recording; adapters unhook here.</summary>
        public static event Action Finished;

        public static bool IsRecording { get; private set; }
        public static LoadTimeResult Result { get; private set; }
        public static string LogFilePath { get; private set; }

        private static readonly List<LoadTimeMark> Marks = new List<LoadTimeMark>();
        private static readonly object ErrorLock = new object();
        private static int _errors;
        private static string _firstError;
        private static double _readyRealtime;

        /// <summary>Editor Play without domain reload keeps statics from the previous session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unhook();
            IsRecording = false;
            Result = null;
            Ready = null;
            Finished = null;
            Marks.Clear();
            lock (ErrorLock)
            {
                _errors = 0;
                _firstError = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            LoadTimeClock.Init();
            LogFilePath = Path.Combine(Application.persistentDataPath, LogFileName);
            IsRecording = true;

            if (LoadTimeClock.T0 == LoadTimeClock.T0_PROCESS) AddMark("process_start", null, 0);
            Mark("engine_init");

            Application.logMessageReceivedThreaded += HandleLog;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void HandleBeforeSplash() => Mark("splash_start");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HandleBeforeSceneLoad()
        {
            Mark("first_scene_loading");

            // HideInHierarchy (not HideAndDontSave) so the Editor destroys it when Play mode ends.
            var host = new GameObject(HOST_OBJECT_NAME) { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<LoadTimeKitRunner>();
        }

        /// <summary>Adds a point to the timeline. Ignored once the kit has finished.</summary>
        public static void Mark(string name, string detail = null)
        {
            if (!IsRecording) return;
            AddMark(name, detail, LoadTimeClock.NowMs());
        }

        /// <summary>The player can use the first real screen. First call wins; <paramref name="label"/> names what
        /// was reached (e.g. the active scene).</summary>
        public static void MarkReady(string label)
        {
            if (!IsRecording || Result != null) return;

            int readyMs = LoadTimeClock.NowMs();
            AddMark("ready", label, readyMs);
            Complete(LoadTimeResult.STATUS_OK, label, readyMs);
        }

        /// <summary>Called by the runner each frame; false once the kit has finished.</summary>
        internal static bool Tick()
        {
            if (!IsRecording) return false;

            double now = Time.realtimeSinceStartupAsDouble;
            if (Result == null)
            {
                if (LoadTimeClock.SecondsSinceInit >= READY_TIMEOUT_SECONDS)
                    Complete(LoadTimeResult.STATUS_TIMEOUT, SceneManager.GetActiveScene().name, -1);
                return true;
            }

            if (now - _readyRealtime < AFTER_READY_SECONDS) return true;

            Finish();
            return false;
        }

        private static void AddMark(string name, string detail, int ms)
        {
            if (Marks.Count >= MAX_MARKS) return;

            var mark = new LoadTimeMark { name = name, detail = detail, ms = ms, frame = Time.frameCount };
            Marks.Add(mark);

            // Before Ready the kit stays silent; after it, marks are diagnostics and cost nothing that is measured.
            if (Result != null) LoadTimeOutput.Info("after " + FormatMark(mark, Result.readyMs));
        }

        private static void Complete(string status, string label, int readyMs)
        {
            _readyRealtime = Time.realtimeSinceStartupAsDouble;
            Application.logMessageReceivedThreaded -= HandleLog;

            var result = BuildResult(status, label, readyMs);
            Result = result;

            WriteOutput(result);
            AppendFile(result);

            try { Ready?.Invoke(result); }
            catch (Exception ex) { LoadTimeOutput.Info($"Ready handler threw: {ex.Message}"); }

            if (status == LoadTimeResult.STATUS_TIMEOUT) Finish();
        }

        private static void Finish()
        {
            if (!IsRecording) return;

            IsRecording = false;
            Unhook();
            LoadTimeOutput.Info($"end marks={Marks.Count}");

            try { Finished?.Invoke(); }
            catch (Exception ex) { LoadTimeOutput.Info($"Finished handler threw: {ex.Message}"); }
        }

        private static void Unhook()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        private static LoadTimeResult BuildResult(string status, string label, int readyMs)
        {
            var result = new LoadTimeResult
            {
                status = status,
                readyMs = readyMs,
                readyLabel = label,
                t0 = LoadTimeClock.T0,
                processAgeAtInitMs = (int)LoadTimeClock.RawProcessAgeMs,
                run = CountPreviousRuns() + 1,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                app = Application.identifier,
                version = Application.version,
                platform = Application.platform.ToString(),
                device = SystemInfo.deviceModel,
                os = SystemInfo.operatingSystem,
                ramMb = SystemInfo.systemMemorySize,
                cpu = SystemInfo.processorType,
                cores = SystemInfo.processorCount,
                gpu = SystemInfo.graphicsDeviceName,
                screen = $"{Screen.width}x{Screen.height}",
            };

            lock (ErrorLock)
            {
                result.errors = _errors;
                result.firstError = _firstError;
            }

            result.marks.AddRange(Marks);
            return result;
        }

        private static void WriteOutput(LoadTimeResult r)
        {
            string ready = r.status == LoadTimeResult.STATUS_OK ? $"{r.readyMs}ms" : "none";
            LoadTimeOutput.Info(
                $"RESULT ready={ready} status={r.status} reached={r.readyLabel} | {r.device} | {r.os} | RAM {r.ramMb}MB | " +
                $"{r.app} v{r.version} | t0={r.t0} run={r.run} errors={r.errors}");

            int previous = 0;
            foreach (var mark in r.marks)
            {
                LoadTimeOutput.Info(FormatMark(mark, previous));
                previous = mark.ms;
            }

            if (r.firstError != null) LoadTimeOutput.Info($"first error: {r.firstError}");
        }

        private static string FormatMark(LoadTimeMark mark, int previousMs)
        {
            var line = new StringBuilder(96);
            line.Append(mark.ms.ToString(CultureInfo.InvariantCulture).PadLeft(7)).Append("ms  +");
            line.Append((mark.ms - previousMs).ToString(CultureInfo.InvariantCulture).PadLeft(6)).Append("  ");
            line.Append(mark.name);
            if (!string.IsNullOrEmpty(mark.detail)) line.Append(' ').Append(mark.detail);
            return line.ToString();
        }

        private static int CountPreviousRuns()
        {
            try
            {
                if (!File.Exists(LogFilePath)) return 0;
                int lines = 0;
                foreach (string line in File.ReadLines(LogFilePath))
                    if (line.Length > 0) lines++;
                return lines;
            }
            catch (Exception) { return 0; }
        }

        private static void AppendFile(LoadTimeResult result)
        {
            try
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MAX_FILE_BYTES) File.Delete(LogFilePath);
                File.AppendAllText(LogFilePath, JsonUtility.ToJson(result) + "\n");
            }
            catch (IOException ex) { LoadTimeOutput.Info($"cannot write {LogFilePath}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { LoadTimeOutput.Info($"cannot write {LogFilePath}: {ex.Message}"); }
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Mark("scene_loaded", $"{scene.name} {mode}");

        private static void HandleSceneUnloaded(Scene scene) => Mark("scene_unloaded", scene.name);

        private static void HandleActiveSceneChanged(Scene previous, Scene next) => Mark("active_scene", next.name);

        // Any thread. Must never log: Unity drops logs issued inside a log callback.
        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            lock (ErrorLock)
            {
                _errors++;
                if (_firstError == null)
                    _firstError = condition.Length > MAX_ERROR_CHARS ? condition.Substring(0, MAX_ERROR_CHARS) : condition;
            }
        }
    }
}
