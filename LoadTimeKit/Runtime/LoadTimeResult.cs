using System;
using System.Collections.Generic;

namespace QA.LoadTimeKit
{
    /// <summary>One point on the boot timeline. Fields are public for JsonUtility.</summary>
    [Serializable]
    public sealed class LoadTimeMark
    {
        public string name;
        public string detail;
        /// <summary>Milliseconds since T0 (see <see cref="LoadTimeResult.t0"/>).</summary>
        public int ms;
        public int frame;
    }

    /// <summary>One Error / Exception / Assert logged between T0 and Ready. The same message repeated is one entry
    /// with <see cref="count"/> > 1.</summary>
    [Serializable]
    public sealed class LoadTimeError
    {
        /// <summary>"Error", "Exception" or "Assert" (Unity LogType name).</summary>
        public string type;
        /// <summary>Milliseconds since T0, same clock as marks.</summary>
        public int ms;
        public string message;
        /// <summary>Capped stack trace; "" when Unity gave none.</summary>
        public string stack;
        public int count;
    }

    /// <summary>One launch: written as one line of <see cref="LoadTimeKit.LogFileName"/> and passed to
    /// <see cref="LoadTimeKit.Ready"/>.</summary>
    [Serializable]
    public sealed class LoadTimeResult
    {
        public const string STATUS_OK = "ok";
        public const string STATUS_TIMEOUT = "timeout";

        public string status;
        /// <summary>Milliseconds from T0 to Ready; -1 on timeout.</summary>
        public int readyMs;
        /// <summary>What the game reached (scene or label passed to MarkReady).</summary>
        public string readyLabel;
        /// <summary>"process" (Android process start), "engine" (Unity engine start) or "play" (Editor Play).</summary>
        public string t0;
        /// <summary>Android process age when the kit initialised; -1 when unknown or ignored.</summary>
        public int processAgeAtInitMs;
        /// <summary>Errors / exceptions / asserts logged between T0 and Ready.</summary>
        public int errors;
        public string firstError;
        /// <summary>Distinct errors in the order first seen, at most LoadTimeKit.MAX_ERRORS.</summary>
        public List<LoadTimeError> errorList = new List<LoadTimeError>();
        /// <summary>Distinct error messages that did not fit in <see cref="errorList"/>.</summary>
        public int errorsDropped;
        /// <summary>1 = first launch since the log file was created (fresh install or cleared data).</summary>
        public int run;

        public string date;
        public string app;
        public string version;
        public string platform;
        public string device;
        public string os;
        public int ramMb;
        public string cpu;
        public int cores;
        public string gpu;
        public string screen;

        public List<LoadTimeMark> marks = new List<LoadTimeMark>();
    }
}
