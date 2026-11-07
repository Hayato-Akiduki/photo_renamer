using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PhotoRenamer
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>ライト／ダークの配色を切り替える。</summary>
    public static class ThemeManager
    {
        // キー: (ライト, ダーク)
        static readonly Dictionary<string, (string light, string dark)> Palette = new Dictionary<string, (string, string)>
        {
            ["BgBrush"]          = ("#F3F4F7", "#16171B"),
            ["CardBrush"]        = ("#FFFFFF", "#202228"),
            ["LineBrush"]        = ("#DFE2E8", "#353841"),
            ["SoftLineBrush"]    = ("#EEF0F4", "#2A2D34"),
            ["TextBrush"]        = ("#1D2230", "#E8EAF0"),
            ["SubTextBrush"]     = ("#5E6577", "#A5ABBA"),
            ["MutedBrush"]       = ("#8A90A0", "#7D8394"),
            ["AccentBrush"]      = ("#3355E0", "#5577FF"),
            ["AccentSoftBrush"]  = ("#ECF0FF", "#28304F"),
            ["AccentTextBrush"]  = ("#3355E0", "#AFC0FF"),
            ["SelectBrush"]      = ("#E3E9FF", "#2E3966"),
            ["InputBrush"]       = ("#FFFFFF", "#18191E"),
            ["HeaderBrush"]      = ("#F7F8FA", "#25282F"),
            ["AltRowBrush"]      = ("#FAFBFC", "#23252B"),
            ["HoverBrush"]       = ("#F0F2F6", "#2C2F37"),
            ["DropBgBrush"]      = ("#F7F9FF", "#1D2235"),
            ["DropLineBrush"]    = ("#B9C3E8", "#3D4A7A"),
            ["TrackBrush"]       = ("#E6E9F0", "#33363E"),
            ["ThumbBrush"]       = ("#C3C8D2", "#4B4F5A"),
            ["ThumbHoverBrush"]  = ("#A5ABB8", "#636874"),
        };

        public static bool IsDark { get; private set; }
        public static event Action Changed;

        public static bool SystemIsDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static void Apply(ThemeMode mode)
        {
            bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemIsDark());
            IsDark = dark;
            var res = Application.Current.Resources;
            foreach (var kv in Palette)
                res[kv.Key] = Brush(dark ? kv.Value.dark : kv.Value.light);

            Theme.Text = (Brush)res["TextBrush"];
            Theme.SubText = (Brush)res["SubTextBrush"];
            Theme.GreenBg = Brush(dark ? "#1D3A28" : "#E3F5E8"); Theme.GreenFg = Brush(dark ? "#7FD69A" : "#1B7A3A");
            Theme.BlueBg = Brush(dark ? "#1E2D4F" : "#E5EEFF"); Theme.BlueFg = Brush(dark ? "#93B6FF" : "#1F56C2");
            Theme.OrangeBg = Brush(dark ? "#3D2D16" : "#FFF0DB"); Theme.OrangeFg = Brush(dark ? "#F2B566" : "#A55A00");
            Theme.GrayBg = Brush(dark ? "#2E3138" : "#EEF0F3"); Theme.GrayFg = Brush(dark ? "#A5ABBA" : "#5B6270");
            Theme.RedFg = Brush(dark ? "#FF8A8A" : "#C62828");

            foreach (Window w in Application.Current.Windows) SetDarkTitleBar(w, dark);
            Changed?.Invoke();
        }

        static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        /// <summary>タイトルバーも暗くする（Windows 10 20H1 以降 / Windows 11）。</summary>
        public static void SetDarkTitleBar(Window w, bool dark)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int on = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int)); // 古い Windows 10
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
