using HarmonyLib;
using Il2Cpp;

namespace BackgroundLoading
{
    // TLD has its own pause-on-focus-loss on top of Unity's run-in-background:
    // GameManager.OnApplicationFocus(bool) reacts to the window losing focus and
    // GameManager.PauseWhenFocusLost(bool) does the pausing (it remembers that in
    // GameManager.s_PausedWhenFocusLost / m_PausedWhenFocusLost so it can undo it on refocus).
    // While BackgroundLoading is ON, the "focus lost" half is skipped so the game keeps
    // loading; the "focus gained" half always runs normally.

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PauseWhenFocusLost))]
    internal static class GameManager_PauseWhenFocusLost
    {
        private static bool Prefix(bool focusLost)
        {
            if (!focusLost || !BackgroundLoadingMod.IsActive)
                return true;

            BackgroundLoadingMod.NoteSuppressedFocusPause("GameManager.PauseWhenFocusLost(true)");
            return false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "OnApplicationFocus")]
    internal static class GameManager_OnApplicationFocus
    {
        private static bool Prefix(bool focusStatus)
        {
            if (focusStatus)
            {
                // Focus is back: let vanilla run (it undoes any pause it made itself).
                BackgroundLoadingMod.NoteFocusRegained();
                return true;
            }

            if (!BackgroundLoadingMod.IsActive)
                return true;

            BackgroundLoadingMod.NoteSuppressedFocusPause("GameManager.OnApplicationFocus(false)");
            return false;
        }

        private static void Postfix()
        {
            // Whatever ran above, make sure nothing turned run-in-background back off while we need it.
            BackgroundLoadingMod.EnforceRunInBackground("after GameManager.OnApplicationFocus");
        }
    }
}
