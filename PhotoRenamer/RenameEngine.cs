using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;

namespace PhotoRenamer
{
    public enum ConflictMode { Suffix, Skip, Overwrite }

    public enum ItemStatus { Rename, Unchanged, Suffixed, Skipped, Overwrite, Remove, NoDate, Invalid }

    public sealed class MediaFile
    {
        public string Path;
        public string Name;
        public DateTime? Date;
        public DateSource Source;
    }

    /// <summary>プレビュー一覧の1行。</summary>
    public sealed class PlanItem
    {
        public MediaFile File { get; set; }
        public string NewName { get; set; }
        public ItemStatus Status { get; set; }

        public bool WillMove => Status == ItemStatus.Rename || Status == ItemStatus.Suffixed || Status == ItemStatus.Overwrite;

        public string OriginalName => File.Name;
        public string NewNameDisplay => WillMove || Status == ItemStatus.Unchanged ? NewName : "—";
        public Brush NewNameBrush => WillMove ? Theme.Text : Theme.SubText;
        public string DateText => File.Date?.ToString("yyyy/MM/dd HH:mm:ss") ?? "—";

        public string SourceLabel => File.Source switch
        {
            DateSource.Exif => "撮影日時",
            DateSource.Video => "動画情報",
            DateSource.FileTime => "更新日時",
            _ => "なし",
        };
        public Brush SourceBg => File.Source switch
        {
            DateSource.Exif => Theme.GreenBg,
            DateSource.Video => Theme.BlueBg,
            DateSource.FileTime => Theme.OrangeBg,
            _ => Theme.GrayBg,
        };
        public Brush SourceFg => File.Source switch
        {
            DateSource.Exif => Theme.GreenFg,
            DateSource.Video => Theme.BlueFg,
            DateSource.FileTime => Theme.OrangeFg,
            _ => Theme.GrayFg,
        };

        public string StatusText => Status switch
        {
            ItemStatus.Rename => "変更",
            ItemStatus.Unchanged => "変更なし",
            ItemStatus.Suffixed => "重複 → 番号追加",
            ItemStatus.Skipped => "重複 → 変更しない",
            ItemStatus.Overwrite => "重複 → これを残す",
            ItemStatus.Remove => "重複 → ごみ箱へ",
            ItemStatus.NoDate => "日付なし",
            _ => "名前が空になる",
        };
        public Brush StatusBrush => Status switch
        {
            ItemStatus.Rename => Theme.GreenFg,
            ItemStatus.Suffixed => Theme.OrangeFg,
            ItemStatus.Overwrite => Theme.OrangeFg,
            ItemStatus.Remove => Theme.RedFg,
            ItemStatus.Invalid => Theme.RedFg,
            _ => Theme.SubText,
        };
    }

    /// <summary>一覧で使う色。ThemeManager.Apply で切り替わる。</summary>
    public static class Theme
    {
        public static Brush Text, SubText;
        public static Brush GreenBg, GreenFg, BlueBg, BlueFg, OrangeBg, OrangeFg, GrayBg, GrayFg, RedFg;
    }

    // ============================================================

    public static class Scanner
    {
        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(MetadataReader.ImageExt, ext) >= 0 || Array.IndexOf(MetadataReader.VideoExt, ext) >= 0;
        }

        /// <summary>フォルダ内の対象ファイルと撮影日時を読み込む（Windows のプロパティ機能を使うため STA スレッドで実行）。</summary>
        public static Task<List<MediaFile>> ScanAsync(string folder, IProgress<(int done, int total)> progress, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<List<MediaFile>>();
            var th = new Thread(() =>
            {
                try
                {
                    var paths = Directory.EnumerateFiles(folder).Where(IsSupported)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                    var list = new List<MediaFile>(paths.Count);
                    for (int i = 0; i < paths.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (date, src) = MetadataReader.GetDate(paths[i]);
                        list.Add(new MediaFile { Path = paths[i], Name = Path.GetFileName(paths[i]), Date = date, Source = src });
                        if (i % 10 == 0 || i == paths.Count - 1) progress?.Report((i + 1, paths.Count));
                    }
                    tcs.SetResult(list);
                }
                catch (OperationCanceledException) { tcs.SetCanceled(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            return tcs.Task;
        }
    }

    // ============================================================

    public static class Planner
    {
        static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        public static string FormatName(string fmt, DateTime dt, int index, string originalStem)
        {
            var sb = new StringBuilder(fmt);
            sb.Replace("{YYYY}", dt.ToString("yyyy"));
            sb.Replace("{MM}", dt.ToString("MM"));
            sb.Replace("{DD}", dt.ToString("dd"));
            sb.Replace("{HH}", dt.ToString("HH"));
            sb.Replace("{mm}", dt.ToString("mm"));
            sb.Replace("{SS}", dt.ToString("ss"));
            sb.Replace("{N}", index.ToString("0000"));
            sb.Replace("{original}", originalStem);
            for (int i = 0; i < sb.Length; i++)
                if (Array.IndexOf(Invalid, sb[i]) >= 0) sb[i] = '_';
            return sb.ToString().Trim().TrimEnd('.');
        }

        /// <summary>
        /// リネーム計画を作る。撮影日時の古い順に並べ、{N} もこの順で振る。
        /// </summary>
        public static List<PlanItem> Build(IEnumerable<MediaFile> files, string fmt, ConflictMode mode)
        {
            var ordered = files
                .OrderBy(f => f.Date == null ? 1 : 0)
                .ThenBy(f => f.Date ?? DateTime.MaxValue)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var items = new List<PlanItem>();
            int n = 0;
            foreach (var f in ordered)
            {
                var item = new PlanItem { File = f };
                items.Add(item);
                if (f.Date == null) { item.Status = ItemStatus.NoDate; continue; }

                n++;
                string ext = Path.GetExtension(f.Name);
                string stem = FormatName(fmt, f.Date.Value, n, Path.GetFileNameWithoutExtension(f.Name));
                if (stem.Length == 0) { item.Status = ItemStatus.Invalid; continue; }
                item.NewName = stem + ext.ToLowerInvariant();
                item.Status = ItemStatus.Rename;
            }

            // 上書きモード: 同じ名前になるファイルのうち最後の1つだけ残し、他はごみ箱へ
            if (mode == ConflictMode.Overwrite)
            {
                foreach (var g in items.Where(i => i.Status == ItemStatus.Rename)
                                       .GroupBy(i => i.NewName, StringComparer.OrdinalIgnoreCase)
                                       .Where(g => g.Count() > 1))
                {
                    var list = g.ToList();
                    for (int i = 0; i < list.Count - 1; i++) list[i].Status = ItemStatus.Remove;
                    list[list.Count - 1].Status = ItemStatus.Overwrite;
                }
            }

            // 動かないファイルの名前は予約済みにする
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in items.Where(i => i.Status == ItemStatus.NoDate || i.Status == ItemStatus.Invalid))
                used.Add(i.File.Name);

            foreach (var item in items)
            {
                if (item.Status != ItemStatus.Rename && item.Status != ItemStatus.Overwrite) continue;

                string name = item.NewName;
                if (used.Contains(name))
                {
                    if (mode == ConflictMode.Skip)
                    {
                        item.Status = ItemStatus.Skipped;
                        item.NewName = null;
                        used.Add(item.File.Name);
                        continue;
                    }
                    string b = Path.GetFileNameWithoutExtension(name), e = Path.GetExtension(name);
                    int k = 1;
                    while (used.Contains($"{b}_{k}{e}")) k++;
                    name = $"{b}_{k}{e}";
                    item.Status = ItemStatus.Suffixed;
                }
                item.NewName = name;
                used.Add(name);
                if (string.Equals(name, item.File.Name, StringComparison.Ordinal))
                    item.Status = ItemStatus.Unchanged;
            }
            return items;
        }
    }

    // ============================================================

    public sealed class RenameRecord
    {
        public string From; // 元のパス
        public string To;   // 変更後のパス
    }

    public sealed class RenameResult
    {
        public int Ok, Failed, Skipped, Removed;
        public List<RenameRecord> Done = new List<RenameRecord>();
        public List<string> Errors = new List<string>();
    }

    public static class Renamer
    {
        public static RenameResult Execute(string folder, IList<PlanItem> plan, bool backup, IProgress<(int, int)> progress)
        {
            var res = new RenameResult();
            var movers = plan.Where(p => p.WillMove).ToList();
            res.Skipped = plan.Count(p => p.Status == ItemStatus.Skipped);
            res.Ok = plan.Count(p => p.Status == ItemStatus.Unchanged);
            int total = movers.Count * 2 + 1, step = 0;

            // 1. バックアップ
            if (backup)
            {
                foreach (var p in movers)
                {
                    try { File.Copy(p.File.Path, UniquePath(p.File.Path + ".bak"), false); }
                    catch (Exception ex) { res.Errors.Add($"{p.File.Name}: バックアップ失敗 ({ex.Message})"); }
                }
            }

            // 2. 上書きで不要になるファイルをごみ箱へ
            foreach (var p in plan.Where(p => p.Status == ItemStatus.Remove))
            {
                try
                {
                    FileSystem.DeleteFile(p.File.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    res.Removed++;
                }
                catch (Exception ex) { res.Errors.Add($"{p.File.Name}: ごみ箱へ移動できません ({ex.Message})"); }
            }
            progress?.Report((++step, total));

            // 3. いったん一時的な名前に変える（A→B, B→C のような入れ替わりでも衝突しないように）
            var temps = new List<(PlanItem item, string temp)>();
            foreach (var p in movers)
            {
                string temp = Path.Combine(folder, $"~prtmp_{Guid.NewGuid():N}{Path.GetExtension(p.File.Name)}");
                try { File.Move(p.File.Path, temp); temps.Add((p, temp)); }
                catch (Exception ex) { res.Failed++; res.Errors.Add($"{p.File.Name}: {ex.Message}"); }
                progress?.Report((++step, total));
            }

            // 4. 本来の名前へ
            foreach (var (p, temp) in temps)
            {
                string dst = Path.Combine(folder, p.NewName);
                try
                {
                    if (File.Exists(dst)) throw new IOException("同じ名前のファイルがすでにあります");
                    File.Move(temp, dst);
                    res.Ok++;
                    res.Done.Add(new RenameRecord { From = p.File.Path, To = dst });
                }
                catch (Exception ex)
                {
                    res.Failed++;
                    res.Errors.Add($"{p.File.Name}: {ex.Message}");
                    try { File.Move(temp, File.Exists(p.File.Path) ? UniquePath(p.File.Path) : p.File.Path); } catch { }
                }
                progress?.Report((++step, total));
            }
            return res;
        }

        /// <summary>直前のリネームを元に戻す。</summary>
        public static RenameResult Undo(IList<RenameRecord> records)
        {
            var res = new RenameResult();
            var temps = new List<(RenameRecord rec, string temp)>();
            foreach (var r in records)
            {
                string temp = Path.Combine(Path.GetDirectoryName(r.To), $"~prtmp_{Guid.NewGuid():N}{Path.GetExtension(r.To)}");
                try { File.Move(r.To, temp); temps.Add((r, temp)); }
                catch (Exception ex) { res.Failed++; res.Errors.Add($"{Path.GetFileName(r.To)}: {ex.Message}"); }
            }
            foreach (var (r, temp) in temps)
            {
                string dst = File.Exists(r.From) ? UniquePath(r.From) : r.From;
                try { File.Move(temp, dst); res.Ok++; }
                catch (Exception ex)
                {
                    res.Failed++;
                    res.Errors.Add($"{Path.GetFileName(r.From)}: {ex.Message}");
                    try { File.Move(temp, r.To); } catch { }
                }
            }
            return res;
        }

        static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, $"{stem}_{i}{ext}");
                if (!File.Exists(p)) return p;
            }
        }
    }
}
