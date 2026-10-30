using UnityEngine;

namespace QA.LoadTimeKit
{
    /// <summary>Hidden host: first frame, focus/pause (full-screen ads show up as focus loss) and the kit's timers.
    /// Created at runtime only; destroys itself when the kit finishes.</summary>
    [AddComponentMenu("")]
    internal sealed class LoadTimeKitRunner : MonoBehaviour
    {
        private bool _sawFirstFrame;

        private void Update()
        {
            if (!_sawFirstFrame)
            {
                _sawFirstFrame = true;
                LoadTimeKit.Mark("first_frame");
            }

            if (!LoadTimeKit.Tick())
                Destroy(gameObject);
        }

        private void OnApplicationFocus(bool hasFocus) => LoadTimeKit.Mark(hasFocus ? "focus_gained" : "focus_lost");

        private void OnApplicationPause(bool paused) => LoadTimeKit.Mark(paused ? "paused" : "resumed");
    }
}
