using System.Windows;

namespace PhotoRenamer
{
    public partial class App : Application
    {
        public static Settings Settings { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            Settings = Settings.Load();
            ThemeManager.Apply(Settings.Theme);

            // 「自動」のときは Windows の設定変更に追従する
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, a) =>
            {
                if (a.Category == Microsoft.Win32.UserPreferenceCategory.General && Settings.Theme == ThemeMode.System)
                    Dispatcher.BeginInvoke(new System.Action(() => ThemeManager.Apply(ThemeMode.System)));
            };
            base.OnStartup(e);
        }
    }
}
