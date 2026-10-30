using System.Collections;
using UnityEngine;

namespace QA.PerfKit
{
    /// <summary>Hidden host created by <see cref="PerfKit"/> at startup. Update forwards to <c>PerfKit.Tick</c>, which
    /// returns on a bool while the kit is off; a coroutine polls the flag file every <see cref="PerfKit.POLL_SECONDS"/>.</summary>
    [AddComponentMenu("")]
    internal sealed class PerfKitRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // One cached wait object: the poll loop allocates nothing.
            var wait = new WaitForSecondsRealtime(PerfKit.POLL_SECONDS);
            while (true)
            {
                PerfKit.PollFlag();
                yield return wait;
            }
        }

        private void Update() => PerfKit.Tick();
    }
}
