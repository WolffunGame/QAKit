using System;
using System.Text;

namespace QA.PerfKit
{
    /// <summary>One frame's measurements. Negative cpu / rend / gpu = not available (logged as `-1`).</summary>
    internal struct FrameSample
    {
        public float ftMs, cpuMs, rendMs, gpuMs;
        public int ramMb;
        public bool gc;
    }

    /// <summary>
    /// Builds the kit's log lines (DROP / HB / DROPBURST / MARK) and holds the drop rate limit and the ring of previous
    /// frames. Pure C# (no Unity API) so EditMode tests are fast. Numbers are written digit by digit into one reused
    /// <see cref="StringBuilder"/> (invariant, no culture lookup, no boxing); the only allocation is the final string.
    /// Main thread only.
    /// </summary>
    internal sealed class PerfLineBuilder
    {
        public const int MAX_DROPS_PER_SECOND = 10;
        public const int WINDOW_MS = 1000;
        public const int PREV_COUNT = 3;
        public const int MAX_NAME = 32;

        private readonly FrameSample[] _ring;
        private int _ringNext;
        private int _ringCount;

        // Times of the last MAX_DROPS_PER_SECOND allowed drops, circular; the slot at _allowedNext is the oldest.
        private readonly long[] _allowed = new long[MAX_DROPS_PER_SECOND];
        private int _allowedNext;
        private int _allowedCount;

        private int _suppressed;
        private float _suppressedWorst;
        private long _burstStartMs;

        private readonly StringBuilder _sb = new StringBuilder(320);

        public PerfLineBuilder(int ringSize = 4)
        {
            _ring = new FrameSample[Math.Max(PREV_COUNT, ringSize)];
        }

        /// <summary>Adds a finished frame to the ring that feeds `prev=`. Call it after <see cref="Drop"/> for the same
        /// frame, so `prev` lists the frames before the drop.</summary>
        public void Push(in FrameSample s)
        {
            _ring[_ringNext] = s;
            _ringNext = (_ringNext + 1) % _ring.Length;
            if (_ringCount < _ring.Length) _ringCount++;
        }

        /// <summary>Forgets previous frames, the rate-limit window and refused drops (kit switched on again).</summary>
        public void Reset()
        {
            _ringNext = 0;
            _ringCount = 0;
            _allowedNext = 0;
            _allowedCount = 0;
            _suppressed = 0;
            _suppressedWorst = 0f;
        }

        /// <summary>At least one missed vsync.</summary>
        public bool ShouldLogDrop(float ftMs, float tgtMs) => ftMs > tgtMs * 1.5f;

        /// <summary>At most <see cref="MAX_DROPS_PER_SECOND"/> drop lines per rolling second. A refused drop is counted
        /// (with its frame time) for the next <see cref="TakeBurst"/>.</summary>
        public bool AllowDrop(long nowMs, float ftMs = 0f)
        {
            if (_allowedCount < MAX_DROPS_PER_SECOND || nowMs - _allowed[_allowedNext] >= WINDOW_MS)
            {
                _allowed[_allowedNext] = nowMs;
                _allowedNext = (_allowedNext + 1) % MAX_DROPS_PER_SECOND;
                if (_allowedCount < MAX_DROPS_PER_SECOND) _allowedCount++;
                return true;
            }

            if (_suppressed == 0)
            {
                _burstStartMs = nowMs;
                _suppressedWorst = 0f;
            }
            _suppressed++;
            if (ftMs > _suppressedWorst) _suppressedWorst = ftMs;
            return false;
        }

        /// <summary>True once a second has passed since the first refused drop: hands out the refused count and the
        /// worst frame time, then starts over. Call every frame; it is a couple of compares.</summary>
        public bool TakeBurst(long nowMs, out int n, out float worst)
        {
            if (_suppressed == 0 || nowMs - _burstStartMs < WINDOW_MS)
            {
                n = 0;
                worst = 0f;
                return false;
            }

            n = _suppressed;
            worst = _suppressedWorst;
            _suppressed = 0;
            _suppressedWorst = 0f;
            return true;
        }

        public string Drop(long t, in FrameSample s, float tgtMs, string scene, PerfContext ctx)
        {
            StringBuilder sb = _sb.Clear();
            sb.Append("DROP t="); AppendLong(sb, t);
            sb.Append(" ft="); AppendF1(sb, s.ftMs);
            sb.Append(" tgt="); AppendF1(sb, tgtMs);
            sb.Append(" efps="); AppendLong(sb, s.ftMs > 0f ? (int)(1000f / s.ftMs) : 0);
            AppendTimes(sb, in s);
            sb.Append(" gc=").Append(s.gc ? '1' : '0');
            AppendSceneCtx(sb, scene, ctx);

            sb.Append(" prev=");
            int count = Math.Min(PREV_COUNT, _ringCount);
            if (count == 0) sb.Append('-');
            for (int i = 0; i < count; i++)
            {
                // Oldest first: the (count - i)-th most recent slot.
                int index = (_ringNext - count + i + _ring.Length) % _ring.Length;
                if (i > 0) sb.Append(',');
                AppendF1(sb, _ring[index].cpuMs);
                sb.Append('/');
                AppendF1(sb, _ring[index].gpuMs);
            }
            return sb.ToString();
        }

        /// <summary><paramref name="avg"/> holds window averages (ft unused); fps is written as a whole number.</summary>
        public string Heartbeat(long t, float fps, in FrameSample avg, string scene, PerfContext ctx)
        {
            StringBuilder sb = _sb.Clear();
            sb.Append("HB t="); AppendLong(sb, t);
            sb.Append(" fps="); AppendLong(sb, fps > 0f ? (long)(fps + 0.5f) : 0);
            AppendTimes(sb, in avg);
            AppendSceneCtx(sb, scene, ctx);
            return sb.ToString();
        }

        public string Burst(long t, int n, float worst)
        {
            StringBuilder sb = _sb.Clear();
            sb.Append("DROPBURST t="); AppendLong(sb, t);
            sb.Append(" n="); AppendLong(sb, n);
            sb.Append(" worst="); AppendF1(sb, worst);
            return sb.ToString();
        }

        public string Mark(long t, string name)
        {
            StringBuilder sb = _sb.Clear();
            sb.Append("MARK t="); AppendLong(sb, t);
            sb.Append(" name="); AppendToken(sb, name);
            return sb.ToString();
        }

        /// <summary>Scene / mark name as one whitespace-free token: whitespace, `;` and `=` become `_`, cut to
        /// <see cref="MAX_NAME"/>; empty becomes `-`. Returns the input itself when it is already clean.</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "-";
            bool clean = name.Length <= MAX_NAME;
            for (int i = 0; clean && i < name.Length; i++)
                if (IsBreaking(name[i])) clean = false;
            if (clean) return name;

            var sb = new StringBuilder(MAX_NAME);
            AppendToken(sb, name);
            return sb.ToString();
        }

        private static void AppendTimes(StringBuilder sb, in FrameSample s)
        {
            sb.Append(" cpu="); AppendF1(sb, s.cpuMs);
            sb.Append(" rend="); AppendF1(sb, s.rendMs);
            sb.Append(" gpu="); AppendF1(sb, s.gpuMs);
            sb.Append(" ram="); AppendLong(sb, s.ramMb);
        }

        private static void AppendSceneCtx(StringBuilder sb, string scene, PerfContext ctx)
        {
            sb.Append(" scene="); AppendToken(sb, scene);
            sb.Append(" ctx=");
            ctx?.AppendTo(sb);
        }

        private static void AppendToken(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                sb.Append('-');
                return;
            }
            int n = Math.Min(text.Length, MAX_NAME);
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                sb.Append(IsBreaking(c) ? '_' : c);
            }
        }

        private static bool IsBreaking(char c) => c == ';' || c == '=' || char.IsWhiteSpace(c);

        /// <summary>One decimal, rounded half away from zero; any negative value (not available) is written `-1`.</summary>
        internal static void AppendF1(StringBuilder sb, float value)
        {
            if (value < 0f || float.IsNaN(value))
            {
                sb.Append("-1");
                return;
            }
            double scaled = Math.Round(value * 10.0, MidpointRounding.AwayFromZero);
            long tenths = scaled > 9_999_999_999.0 ? 9_999_999_999L : (long)scaled;
            AppendLong(sb, tenths / 10);
            sb.Append('.').Append((char)('0' + (int)(tenths % 10)));
        }

        /// <summary>Invariant digits without <c>long.ToString()</c> (no allocation, no culture).</summary>
        internal static void AppendLong(StringBuilder sb, long value)
        {
            if (value < 0)
            {
                sb.Append('-');
                if (value == long.MinValue)
                {
                    sb.Append("9223372036854775808");
                    return;
                }
                value = -value;
            }
            if (value < 10)
            {
                sb.Append((char)('0' + value));
                return;
            }

            int start = sb.Length;
            while (value > 0)
            {
                sb.Append((char)('0' + (int)(value % 10)));
                value /= 10;
            }
            // Digits went in least significant first: reverse them in place.
            for (int i = start, j = sb.Length - 1; i < j; i++, j--)
            {
                char tmp = sb[i];
                sb[i] = sb[j];
                sb[j] = tmp;
            }
        }
    }
}
