using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace QA.PerfKit
{
    /// <summary>
    /// Per-frame CPU main / render thread and GPU times from <see cref="FrameTimingManager"/> (values describe a frame
    /// that completed 1-2 frames ago), plus GC and memory. When the manager returns nothing (Player Settings
    /// *Frame Timing Stats* off) cpu / rend / gpu are -1. Allocation-free; main thread only.
    /// </summary>
    internal sealed class PerfSampler
    {
        private const int GPU_BAD_LIMIT = 3;
        private const float FALLBACK_FPS = 60f;
        private const float MOBILE_DEFAULT_FPS = 30f;

        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private int _gpuBad;
        private double _lastGpu;
        private int _lastGcCount = -1;
        private float _targetFrameMs = 1000f / FALLBACK_FPS;
#if UNITY_EDITOR
        private bool _warnedNoTimings;
#endif

        /// <summary>False after <see cref="GPU_BAD_LIMIT"/> consecutive timings with a zero or unchanged GPU time (or
        /// no timings at all); true again on the next real value.</summary>
        public bool GpuAvailable => _gpuBad < GPU_BAD_LIMIT;

        public float TargetFrameMs => _targetFrameMs;

        public void BeginFrame() => FrameTimingManager.CaptureFrameTimings();

        /// <summary>Fills ft (from <c>Time.unscaledDeltaTime</c>), cpu / rend / gpu and gc. RAM is left 0: it is read
        /// by <see cref="ReadRamMb"/> only when a line is written. False when no frame timing was available.</summary>
        public bool TryRead(out FrameSample s)
        {
            s = default;
            s.ftMs = Time.unscaledDeltaTime * 1000f;

            int gcCount = GC.CollectionCount(0);
            s.gc = _lastGcCount >= 0 && gcCount != _lastGcCount;
            _lastGcCount = gcCount;

            if (FrameTimingManager.GetLatestTimings(1, _timings) == 0)
            {
                s.cpuMs = -1f;
                s.rendMs = -1f;
                s.gpuMs = -1f;
                _gpuBad = GPU_BAD_LIMIT;
                WarnNoTimingsOnce();
                return false;
            }

            FrameTiming timing = _timings[0];
#if UNITY_2022_1_OR_NEWER
            s.cpuMs = (float)timing.cpuMainThreadFrameTime;
            s.rendMs = (float)timing.cpuRenderThreadFrameTime;
#else
            s.cpuMs = (float)timing.cpuFrameTime;
            s.rendMs = -1f;
#endif
            double gpu = timing.gpuFrameTime;
            // ReSharper disable once CompareOfFloatsByEqualityOperator -- an exact repeat is what "unchanged" means.
            if (gpu <= 0.0 || gpu == _lastGpu) { if (_gpuBad < GPU_BAD_LIMIT) _gpuBad++; }
            else _gpuBad = 0;
            _lastGpu = gpu;
            s.gpuMs = GpuAvailable && gpu > 0.0 ? (float)gpu : -1f;
            return true;
        }

        public static int ReadRamMb() => (int)(Profiler.GetTotalAllocatedMemoryLong() >> 20);

        /// <summary>Re-reads the target frame time. Called about once a second, not per frame.</summary>
        public void RefreshTarget()
        {
            bool mobile = Application.isMobilePlatform;
            int vSync = mobile ? 0 : QualitySettings.vSyncCount;   // mobile ignores vSyncCount
            int requested = Application.targetFrameRate;
            float fps;
            if (vSync > 0) fps = RefreshRate() / vSync;             // desktop / Editor ignore targetFrameRate under vsync
            else if (requested > 0) fps = requested;
            else fps = mobile ? MOBILE_DEFAULT_FPS : RefreshRate(); // -1 = platform default: 30 on Android / iOS
            if (fps <= 0f || float.IsNaN(fps)) fps = FALLBACK_FPS;
            _targetFrameMs = 1000f / fps;
        }

        public void Reset()
        {
            _gpuBad = 0;
            _lastGpu = 0.0;
            _lastGcCount = -1;
        }

        private static float RefreshRate()
        {
#if UNITY_2022_2_OR_NEWER
            float hz = (float)Screen.currentResolution.refreshRateRatio.value;
#else
            float hz = Screen.currentResolution.refreshRate;
#endif
            return hz > 0f ? hz : FALLBACK_FPS;
        }

        private void WarnNoTimingsOnce()
        {
#if UNITY_EDITOR
            if (_warnedNoTimings) return;
            _warnedNoTimings = true;
            Debug.LogWarning("[PerfKit] FrameTimingManager returned no timings: turn on Player Settings > Other > " +
                             "Frame Timing Stats. Until then cpu / rend / gpu are logged as -1 and ft comes from " +
                             "Time.unscaledDeltaTime.");
#endif
        }
    }
}
