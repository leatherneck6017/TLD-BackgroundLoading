using ModSettings;

namespace BackgroundLoading
{
    internal class BackgroundLoadingSettings : JsonModSettings
    {
        [Name("Enabled")]
        [Description("Keeps the game running while it loads, even if the window loses focus. When off, the game uses Unity's normal setting and pauses whenever it is unfocused.")]
        public bool enabled = true;

        [Name("Keep running in background always")]
        [Description("Keeps the game running whenever the window is unfocused, not just while loading. Warning: during gameplay, time keeps passing (your survivor keeps getting colder and hungrier) and audio keeps playing while you are alt-tabbed.")]
        public bool alwaysRunInBackground = false;

        protected override void OnConfirm()
        {
            base.OnConfirm();
            BackgroundLoadingMod.RequestImmediateCheck();
        }
    }

    internal static class Settings
    {
        internal static BackgroundLoadingSettings settings;

        public static void OnLoad()
        {
            settings = new BackgroundLoadingSettings();
            settings.AddToModSettings("Background Loading");
        }
    }
}
