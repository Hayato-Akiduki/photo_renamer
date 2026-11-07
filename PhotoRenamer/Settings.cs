using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhotoRenamer
{
    /// <summary>前回の設定を %APPDATA%\PhotoRenamer\settings.txt に保存する。</summary>
    public sealed class Settings
    {
        public string Format = "{YYYY}{MM}{DD}_{HH}{mm}{SS}";
        public ConflictMode Conflict = ConflictMode.Suffix;
        public bool Backup;
        public string LastFolder = "";
        public ThemeMode Theme = ThemeMode.System;

        static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoRenamer", "settings.txt");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
                if (d.TryGetValue("Format", out var f) && f.Length > 0) s.Format = f;
                if (d.TryGetValue("Conflict", out var c) && Enum.TryParse(c, out ConflictMode m)) s.Conflict = m;
                if (d.TryGetValue("Backup", out var b)) s.Backup = b == "1";
                if (d.TryGetValue("LastFolder", out var l)) s.LastFolder = l;
                if (d.TryGetValue("Theme", out var t) && Enum.TryParse(t, out ThemeMode tm)) s.Theme = tm;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "Format=" + Format,
                    "Conflict=" + Conflict,
                    "Backup=" + (Backup ? "1" : "0"),
                    "LastFolder=" + LastFolder,
                    "Theme=" + Theme,
                }, Encoding.UTF8);
            }
            catch { }
        }
    }
}
