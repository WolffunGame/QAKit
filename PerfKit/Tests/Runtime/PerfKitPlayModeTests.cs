using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QA.PerfKit.PlayTests
{
    /// <summary>End-to-end in the Editor: the kit host exists at startup, switches on through the public API, and
    /// writes the documented line kinds (on, MARK, DROP with scene/ctx, HB, off) to the Unity log.</summary>
    public class PerfKitPlayModeTests
    {
        /// <summary>Sleeps the main thread once so the next frame is a real drop, like the adb SPIKE hook.</summary>
        private sealed class SpikeOnce : MonoBehaviour
        {
            public int ms = 80;
            private bool _done;

            private void Update()
            {
                if (_done) return;
                _done = true;
                Thread.Sleep(ms);
            }
        }

        private int _savedTarget;
        private int _savedVSync;

        [SetUp]
        public void SetUp()
        {
            _savedTarget = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;   // tgt 16.7 ms, so an 80 ms frame is a drop on any machine
        }

        [TearDown]
        public void TearDown()
        {
            PerfKit.Enabled = false;
            PerfKit.Clear("test");
            Application.targetFrameRate = _savedTarget;
            QualitySettings.vSyncCount = _savedVSync;
        }

        [UnityTest]
        public IEnumerator Off_by_default_and_host_is_hidden()
        {
            yield return null;
            Assert.IsFalse(PerfKit.IsOn);
            Assert.IsNotNull(PerfKit.FlagFilePath);
            Assert.That(PerfKit.FlagFilePath, Does.EndWith(PerfKit.FlagFileName));
            GameObject host = null;
            foreach (var go in Object.FindObjectsOfType<GameObject>())
                if (go.name == "[PerfKit]") host = go;
            Assert.IsNotNull(host, "runner host is created at startup");
            Assert.IsTrue((host.hideFlags & HideFlags.HideInHierarchy) != 0, "host is hidden in the Hierarchy");
        }

        [UnityTest]
        public IEnumerator Enabled_logs_on_then_off()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[PerfKit\] on .*forced=True"));
            PerfKit.Enabled = true;
            Assert.IsTrue(PerfKit.IsOn);
            yield return null;

            LogAssert.Expect(LogType.Log, new Regex(@"^\[PerfKit\] off$"));
            PerfKit.Enabled = false;
            Assert.IsFalse(PerfKit.IsOn);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Mark_is_noop_while_off_and_logged_while_on()
        {
            PerfKit.Mark("ignored");   // no Expect: an unexpected [PerfKit] MARK would not fail, so check via the on path
            yield return null;

            PerfKit.Enabled = true;
            yield return null;
            LogAssert.Expect(LogType.Log, new Regex(@"^\[PerfKit\] MARK t=\d{13} name=boss$"));
            PerfKit.Mark("boss");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spike_frame_logs_DROP_with_scene_and_ctx()
        {
            PerfKit.Set("test", "spike");
            PerfKit.Enabled = true;
            yield return null;
            yield return null;   // one measured frame before the spike so ft/prev are populated

            LogAssert.Expect(LogType.Log, new Regex(
                @"^\[PerfKit\] DROP t=\d{13} ft=(\d{2,4})(\.\d)? tgt=16\.7 .*scene=\S+ ctx=test:spike"));
            var go = new GameObject("spike");
            go.AddComponent<SpikeOnce>().ms = 80;
            yield return null;   // frame with the sleep
            yield return null;   // frame whose deltaTime carries it → DROP
            yield return null;
            Object.Destroy(go);
        }

        [UnityTest]
        [Timeout(20000)]
        public IEnumerator Heartbeat_after_five_seconds()
        {
            PerfKit.Enabled = true;
            LogAssert.Expect(LogType.Log, new Regex(@"^\[PerfKit\] HB t=\d{13} fps=\d+(\.\d)? cpu=.* ram=-?\d+ scene=\S+"));
            float until = Time.realtimeSinceStartup + PerfKit.HEARTBEAT_MS / 1000f + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }
    }
}
