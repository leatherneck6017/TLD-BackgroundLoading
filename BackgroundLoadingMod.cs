using System;
using System.Reflection;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace BackgroundLoading
{
    public class BackgroundLoadingMod : MelonMod
    {
        // How often the loading state is polled (realtime seconds).
        private const float PollInterval = 0.25f;

        // "Not loading" must hold this long (realtime seconds) before the original
        // setting is restored, so short gaps between load phases don't flicker it.
        private const float SettleSeconds = 4.0f;

        // How many times a reset of Application.runInBackground is logged before going quiet.
        private const int MaxResetLogs = 5;

        private static bool s_checkRequested;
        private static BackgroundLoadingMod s_instance;
        private static bool s_isOn;
        private static bool s_suppressedFocusPause;
        private static int s_resetLogCount;

        private bool _originalRunInBackground;
        private bool _capturedOriginal;
        private float _nextPollTime;
        private float _lastLoadingTime;

        // Set once the main menu has been shown and idle since the last scene load.
        // Until then the main menu scene still counts as loading (boot, quit to menu).
        private bool _mainMenuSettled;

        internal static void RequestImmediateCheck() => s_checkRequested = true;

        /// <summary>True while the mod is keeping the game running in the background.</summary>
        internal static bool IsActive => s_isOn && IsModEnabled();

        /// <summary>This mod's logger, for the other classes.</summary>
        internal static MelonLogger.Instance Log => s_instance?.LoggerInstance;

        /// <summary>Called by the Harmony patches when the game's own focus-loss pause was skipped.</summary>
        internal static void NoteSuppressedFocusPause(string what)
        {
            if (!s_suppressedFocusPause)
                s_instance?.LoggerInstance.Msg($"Window lost focus while run in background is ON; skipped {what} so the game keeps going.");
            s_suppressedFocusPause = true;
            EnforceRunInBackground(what);
        }

        /// <summary>Called by the Harmony patches when the window gets focus back.</summary>
        internal static void NoteFocusRegained()
        {
            if (s_suppressedFocusPause)
                s_instance?.LoggerInstance.Msg("Window focused again.");
            s_suppressedFocusPause = false;
        }

        /// <summary>
        /// While ON, puts Application.runInBackground back to true if anything (the game or
        /// another mod) switched it off, because Unity stops the whole game loop when it is
        /// false and the window is unfocused.
        /// </summary>
        internal static void EnforceRunInBackground(string where)
        {
            if (!IsActive)
                return;
            try
            {
                if (Application.runInBackground)
                {
                    RunInBackgroundWatch.SawForeignFalseSet = false;
                    return;
                }
                RunInBackgroundWatch.Set(true);

                // Say whether the setter patch saw who did it (see the [runInBackground culprit] line above),
                // or whether the change got past it (Case 3).
                bool seen = RunInBackgroundWatch.SawForeignFalseSet;
                RunInBackgroundWatch.SawForeignFalseSet = false;
                if (s_resetLogCount < MaxResetLogs)
                {
                    s_resetLogCount++;
                    string how = seen
                        ? "the caller is named in a [runInBackground culprit] line (logged the first time each caller is seen)"
                        : RunInBackgroundWatch.Installed
                            ? "Case 3: the setter patch did not see this change, so it came from native game code that bypasses the patch; no caller can be named"
                            : "the setter patch is not installed, so no caller can be named";
                    s_instance?.LoggerInstance.Warning($"Application.runInBackground was switched off by something else ({where}); switched it back on. {how}.");
                }
            }
            catch
            {
                // Unity may be shutting down.
            }
        }

        public override void OnInitializeMelon()
        {
            s_instance = this;
            _originalRunInBackground = Application.runInBackground;
            _capturedOriginal = true;
            LoggerInstance.Msg($"Original Application.runInBackground = {_originalRunInBackground}.");

            Settings.OnLoad();

            // Names whoever switches run-in-background off (log only).
            RunInBackgroundWatch.Install(HarmonyInstance, LoggerInstance);

            // Boot counts as loading until the main menu is shown and idle.
            _lastLoadingTime = Time.realtimeSinceStartup;
            if (IsModEnabled())
                SetOn("game starting");
            else
                LoggerInstance.Msg("Disabled in Mod Settings; leaving runInBackground unchanged.");
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName) => MarkSceneActivity("unloaded", sceneName);

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => MarkSceneActivity("loaded", sceneName);

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => MarkSceneActivity("initialized", sceneName);

        public override void OnUpdate()
        {
            EnforceRunInBackground("Update");

            float now = Time.realtimeSinceStartup;
            if (!s_checkRequested && now < _nextPollTime)
                return;
            s_checkRequested = false;
            _nextPollTime = now + PollInterval;

            if (!IsModEnabled())
            {
                if (s_isOn)
                    SetOff("disabled in Mod Settings");
                return;
            }

            if (Settings.settings.alwaysRunInBackground)
            {
                if (!s_isOn)
                    SetOn("always-on setting");
                return;
            }

            string loadingReason = GetLoadingReason();
            if (loadingReason != null)
            {
                _lastLoadingTime = now;
                if (!s_isOn)
                    SetOn($"loading: {loadingReason}");
                return;
            }

            if (s_isOn && now - _lastLoadingTime >= SettleSeconds)
                SetOff("loading finished");
        }

        // Catches a reset made during this frame's Update before Unity checks focus again.
        public override void OnLateUpdate() => EnforceRunInBackground("LateUpdate");

        public override void OnDeinitializeMelon() => RestoreOriginal();

        public override void OnApplicationQuit() => RestoreOriginal();

        private void MarkSceneActivity(string what, string sceneName)
        {
            // Any scene change means a load is in flight (or just finished and the
            // new scene is still settling). Switch on now rather than waiting for the poll.
            _mainMenuSettled = false;
            _lastLoadingTime = Time.realtimeSinceStartup;

            if (IsModEnabled() && !s_isOn)
                SetOn($"scene {what}: {sceneName}");
        }

        /// <summary>
        /// Returns why the game is considered to be loading, or null if it is idle
        /// (main menu shown and idle, or in gameplay with no load in progress).
        /// </summary>
        private string GetLoadingReason()
        {
            try
            {
                if (!InterfaceManager.HasInstance())
                    return "InterfaceManager not ready";

                // TLD's own scene manager: true while registered async scene loads are running.
                if (Il2Cpp.SceneManager.IsLoading())
                    return "SceneManager.IsLoading()";

                // The loading screen (region transitions, save loads, quit to menu).
                if (InterfaceManager.IsPanelLoadingEnabledOrLoading())
                    return "loading screen";

                // The empty scene is used as a stepping stone between scenes.
                if (GameManager.IsEmptySceneActive())
                    return "empty transition scene";

                if (GameManager.IsMainMenuActive())
                {
                    if (_mainMenuSettled)
                        return null; // Idle on the main menu or one of its sub-screens.

                    if (!InterfaceManager.IsPanelEnabled<Panel_MainMenu>())
                        return "main menu not shown yet";

                    Panel_MainMenu mainMenu = InterfaceManager.GetPanel<Panel_MainMenu>();
                    if (mainMenu == null)
                        return "main menu not shown yet";
                    if (mainMenu.ProfileIsLoading())
                        return "profile loading";
                    if (!mainMenu.HasFadedIn())
                        return "main menu fading in";

                    _mainMenuSettled = true;
                    return null;
                }

                // Gameplay scene: wait until the player exists.
                if (!GameManager.HasPlayerObject())
                    return "player not spawned";

                return null;
            }
            catch (Exception ex)
            {
                // A failed check (e.g. a manager mid-teardown) is most likely a transition.
                return $"check failed: {ex.Message}";
            }
        }

        private static bool IsModEnabled() => Settings.settings == null || Settings.settings.enabled;

        private void SetOn(string reason)
        {
            RunInBackgroundWatch.Set(true);
            s_isOn = true;
            LoggerInstance.Msg($"Run in background ON ({reason}).");
            UndoExistingFocusPause();
        }

        private void SetOff(string reason)
        {
            s_isOn = false;

            // If a focus-loss pause was skipped and the window is still unfocused, apply it now,
            // so the game is in the same state vanilla would have left it in. Refocusing undoes it
            // through the game's own GameManager.OnApplicationFocus(true).
            bool reapplied = false;
            if (s_suppressedFocusPause && !IsWindowFocused())
            {
                try
                {
                    GameManager.PauseWhenFocusLost(true);
                    reapplied = true;
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"Could not re-apply the game's focus-loss pause: {ex.Message}");
                }
            }
            s_suppressedFocusPause = false;

            RunInBackgroundWatch.Set(_originalRunInBackground);
            LoggerInstance.Msg($"Run in background OFF ({reason}); restored original value {_originalRunInBackground}."
                + (reapplied ? " Window is unfocused, so the game's own focus-loss pause was applied." : ""));
        }

        /// <summary>
        /// If the game already paused itself for focus loss before we switched on (for example
        /// "Keep running in background always" was just turned on), undo that pause, but only
        /// when the game's own flag says it made one, so a pause the player chose is left alone.
        /// </summary>
        private void UndoExistingFocusPause()
        {
            try
            {
                if (IsWindowFocused() || !ReadGamePausedWhenFocusLost())
                    return;
                GameManager.PauseWhenFocusLost(false);
                s_suppressedFocusPause = true;
                LoggerInstance.Msg("The game was paused for focus loss; undid that pause so loading keeps going.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Could not undo the game's focus-loss pause: {ex.Message}");
            }
        }

        private static bool IsWindowFocused()
        {
            try
            {
                return Application.isFocused;
            }
            catch
            {
                return true;
            }
        }

        // GameManager.s_PausedWhenFocusLost / m_PausedWhenFocusLost are private in the game, so
        // they are read by name. Il2CppInterop exposes fields as properties; a plain field is
        // also checked in case that changes. Returns false if neither can be read.
        private static bool ReadGamePausedWhenFocusLost()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "s_PausedWhenFocusLost", "m_PausedWhenFocusLost" })
            {
                PropertyInfo prop = typeof(GameManager).GetProperty(name, flags);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.GetValue(null) is bool p && p)
                    return true;
                FieldInfo field = typeof(GameManager).GetField(name, flags);
                if (field != null && field.FieldType == typeof(bool) && field.GetValue(null) is bool f && f)
                    return true;
            }
            return false;
        }

        private void RestoreOriginal()
        {
            if (!_capturedOriginal || !s_isOn)
                return;
            s_isOn = false;
            try
            {
                RunInBackgroundWatch.Set(_originalRunInBackground);
            }
            catch
            {
                // Unity may already be shutting down.
            }
        }
    }
}
