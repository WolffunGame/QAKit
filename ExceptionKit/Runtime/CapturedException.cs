using System;
using UnityEngine;

namespace QA.ExceptionKit
{
    public readonly struct CapturedException
    {
        /// <summary>1-based, order of first occurrence since the kit was switched on.</summary>
        public readonly int Index;
        public readonly DateTime Time;
        public readonly LogType Type;
        public readonly string Condition;
        public readonly string StackTrace;

        public CapturedException(int index, DateTime time, LogType type, string condition, string stackTrace)
        {
            Index = index;
            Time = time;
            Type = type;
            Condition = condition;
            StackTrace = stackTrace;
        }
    }
}
