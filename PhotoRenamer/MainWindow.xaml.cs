using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace PhotoRenamer
{
    public partial class MainWindow : Window
    {
        static readonly (string label, string fmt)[] Presets =
        {
            ("20261106_142233（年月日_時分秒）", "{YYYY}{MM}{DD}_{HH}{mm}{SS}"),
            ("2026-11-06_14-22-33（区切りあり）", "{YYYY}-{MM}-{DD}_{HH}-{mm}-{SS}"),
            ("20261106_0001（年月日_連番）", "{YYYY}{MM}{DD}_{N}"),
            ("20261106_IMG_1234（年月日_元の名前）", "{YYYY}{MM}{DD}_{original}"),
            ("2026-11_0001（年月_連番）", "{YYYY}-{MM}_{N}"),
        };

        readonly Settings _settings = App.Settings ?? Settings.Load();
        string _folder;
        List<MediaFile> _files = new List<MediaFile>();
        List<PlanItem> _plan = new List<PlanItem>();
        List<RenameRecord> _undo;
        CancellationTokenSource _scanCts;
        bool _busy, _loading = true;

        public MainWindow()
        {
            InitializeComponent();
            var work = SystemParameters.WorkArea;
            if (Height > work.Height) Height = work.Height;
            if (Width > work.Width) Width = work.Width;

            foreach (var p in Presets) PresetBox.Items.Add(p.label);
            FormatBox.Text = _settings.Format;
            ConflictBox.SelectedIndex = (int)_settings.Conflict;
            BackupCheck.IsChecked = _settings.Backup;
            SyncPresetSelection();
            _loading = false;
            UpdateExample();

            VersionText.Text = "v" + AppVersion;
            VersionText.ToolTip = $"写真・動画リネーマー バージョン {AppVersion}\nクリックで更新を確認します";
            Loaded += async (s, e) => await CheckUpdateAsync(manual: false);
            UpdateThemeButton();
            SourceInitialized += (s, e) => ThemeManager.SetDarkTitleBar(this, ThemeManager.IsDark);
            ThemeManager.Changed += OnThemeChanged;
            Closed += (s, e) => ThemeManager.Changed -= OnThemeChanged;

            // exe にフォルダをドラッグした場合などは起動時に読み込む
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                string arg = args[1];
                string dir = Directory.Exists(arg) ? arg : File.Exists(arg) ? Path.GetDirectoryName(arg) : null;
                if (dir != null) Loaded += (s, e) => _ = LoadFolderAsync(dir);
            }

            Closing += (s, e) =>
            {
                _settings.Format = FormatBox.Text;
                _settings.Conflict = CurrentMode;
                _settings.Backup = BackupCheck.IsChecked == true;
                _settings.Save();
            };
        }

        ConflictMode CurrentMode => (ConflictMode)Math.Max(0, ConflictBox.SelectedIndex);

        static string AppVersion
        {
            get
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                return info?.InformationalVersion ?? asm.GetName().Version.ToString(3);
            }
        }

        // ---------------- 自動アップデート ----------------

        Updater.UpdateInfo _update;
        bool _checking;

        async void Version_Click(object sender, MouseButtonEventArgs e) => await CheckUpdateAsync(manual: true);

        async Task CheckUpdateAsync(bool manual)
        {
            if (_checking) return;
            _checking = true;
            try
            {
                if (manual) StatusText.Text = "更新を確認しています…";
                var info = await Updater.CheckAsync();
                if (info != null)
                {
                    _update = info;
                    UpdateText.Text = $"新しいバージョン v{info.Version.ToString(3)} があります（今は v{AppVersion}）";
                    UpdateBanner.Visibility = Visibility.Visible;
                    if (manual) StatusText.Text = "新しいバージョンが見つかりました";
                }
                else if (manual)
                {
                    StatusText.Text = "最新のバージョンです";
                    MessageBox.Show(this, $"最新のバージョン（v{AppVersion}）を使っています。", "更新の確認",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // 起動時はネットにつながっていなくても何も言わない
                if (manual)
                {
                    StatusText.Text = "更新を確認できませんでした";
                    MessageBox.Show(this, "更新を確認できませんでした。インターネット接続を確認してください。\n\n" + ex.Message,
                        "更新の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally { _checking = false; }
        }

        void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

        void UpdateNotes_Click(object sender, RoutedEventArgs e)
        {
            if (_update == null) return;
            string notes = string.IsNullOrWhiteSpace(_update.Notes) ? "（説明はありません）" : _update.Notes.Trim();
            if (notes.Length > 1500) notes = notes.Substring(0, 1500) + " …";
            var r = MessageBox.Show(this, notes + "\n\nGitHub のページを開きますか？", $"v{_update.Version.ToString(3)} の更新内容",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes) Updater.OpenReleasesPage();
        }

        async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            if (_update == null) return;
            if (_busy)
            {
                MessageBox.Show(this, "処理が終わってから更新してください。", "更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateNowButton.IsEnabled = false;
            SetBusy(true, "更新をダウンロードしています…");
            var progress = new Progress<int>(p =>
            {
                Progress.IsIndeterminate = false;
                Progress.Maximum = 100;
                Progress.Value = p;
                StatusText.Text = $"更新をダウンロードしています… {p}%";
            });
            try
            {
                string path = await Updater.DownloadAsync(_update, progress);
                StatusText.Text = "更新しています。アプリは自動で再起動します…";
                Updater.RunInstaller(path);
                _busy = false;
                Close();
            }
            catch (Exception ex)
            {
                SetBusy(false, "更新に失敗しました");
                UpdateNowButton.IsEnabled = true;
                var r = MessageBox.Show(this, "更新できませんでした。\n\n" + ex.Message +
                    "\n\nGitHub のページから手動でダウンロードしますか？", "更新", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Yes) Updater.OpenReleasesPage();
                RebuildPlan();
            }
        }

        // ---------------- テーマ ----------------

        void Theme_Click(object sender, RoutedEventArgs e)
        {
            _settings.Theme = _settings.Theme switch
            {
                ThemeMode.System => ThemeMode.Light,
                ThemeMode.Light => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
            ThemeManager.Apply(_settings.Theme);
            _settings.Save();
        }

        void OnThemeChanged()
        {
            UpdateThemeButton();
            Grid.Items.Refresh(); // 一覧のバッジ色を塗り直す
        }

        void UpdateThemeButton()
        {
            ThemeButton.Content = _settings.Theme switch
            {
                ThemeMode.Light => "☀ ライト",
                ThemeMode.Dark => "☾ ダーク",
                _ => "◐ 自動",
            };
        }

        // ---------------- フォルダ選択 ----------------

        void DropZone_Click(object sender, MouseButtonEventArgs e) => PickFolder();
        void PickFolder_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => PickFolder();

        void PickFolder()
        {
            if (_busy) return;
            string initial = _folder ?? _settings.LastFolder;
            string path = FolderPicker.Show(new WindowInteropHelper(this).Handle, "写真・動画のフォルダを選択", initial);
            if (path != null) _ = LoadFolderAsync(path);
        }

        void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = GetDroppedFolder(e) != null && !_busy ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            var folder = GetDroppedFolder(e);
            if (folder != null && !_busy) _ = LoadFolderAsync(folder);
        }

        static string GetDroppedFolder(DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths) || paths.Length == 0) return null;
            if (Directory.Exists(paths[0])) return paths[0];
            if (File.Exists(paths[0])) return Path.GetDirectoryName(paths[0]); // ファイルを落とした場合はその親フォルダ
            return null;
        }

        void Reload_Click(object sender, RoutedEventArgs e) => Reload();
        void Reload_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => Reload();
        void Reload() { if (_folder != null && !_busy) _ = LoadFolderAsync(_folder); }

        async Task LoadFolderAsync(string folder)
        {
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "フォルダが見つかりません。\n" + folder, "写真・動画リネーマー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scanCts?.Cancel();
            var cts = _scanCts = new CancellationTokenSource();
            if (!string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase)) _undo = null;
            _folder = folder;
            _settings.LastFolder = folder;

            FolderTitle.Text = Path.GetFileName(folder.TrimEnd('\\')) is string n && n.Length > 0 ? n : folder;
            FolderPath.Text = folder;
            ReloadButton.IsEnabled = true;

            SetBusy(true, "ファイルを読み込んでいます…");
            var progress = new Progress<(int done, int total)>(p =>
            {
                if (cts.IsCancellationRequested) return;
                Progress.IsIndeterminate = false;
                Progress.Maximum = p.total;
                Progress.Value = p.done;
                StatusText.Text = $"撮影日時を読み込んでいます… {p.done} / {p.total}";
            });

            try
            {
                var files = await Scanner.ScanAsync(folder, progress, cts.Token);
                if (cts.IsCancellationRequested) return;
                _files = files;
                SetBusy(false, null);
                RebuildPlan();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetBusy(false, "読み込みに失敗しました");
                MessageBox.Show(this, "フォルダを読み込めませんでした。\n\n" + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------- 形式・オプション ----------------

        void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || PresetBox.SelectedIndex < 0) return;
            string fmt = Presets[PresetBox.SelectedIndex].fmt;
            if (FormatBox.Text != fmt) FormatBox.Text = fmt;
        }

        void SyncPresetSelection()
        {
            int idx = Array.FindIndex(Presets, p => p.fmt == FormatBox.Text);
            bool prev = _loading;
            _loading = true;
            PresetBox.SelectedIndex = idx;
            _loading = prev;
        }

        void Token_Click(object sender, RoutedEventArgs e)
        {
            string token = (string)((Button)sender).Tag;
            int pos = FormatBox.SelectionStart;
            FormatBox.SelectedText = token;
            FormatBox.SelectionLength = 0;
            FormatBox.CaretIndex = pos + token.Length;
            FormatBox.Focus();
        }

        void FormatBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            SyncPresetSelection();
            UpdateExample();
            RebuildPlan();
        }

        void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            RebuildPlan();
        }

        void UpdateExample()
        {
            string fmt = FormatBox.Text;
            if (string.IsNullOrWhiteSpace(fmt)) { ExampleText.Text = "（形式を入力してください）"; return; }
            var sample = new DateTime(2026, 11, 6, 14, 22, 33);
            string name = Planner.FormatName(fmt, sample, 1, "IMG_1234");
            ExampleText.Text = name.Length == 0 ? "（名前が空になります）" : name + ".jpg";
        }

        // ---------------- プレビュー ----------------

        void RebuildPlan()
        {
            if (_busy) return;
            string fmt = FormatBox.Text;

            if (_folder == null)
            {
                ShowEmpty("フォルダを選ぶと、ここに変更後の名前が表示されます");
                return;
            }
            if (_files.Count == 0)
            {
                _plan = new List<PlanItem>();
                Grid.ItemsSource = null;
                SummaryText.Text = "";
                ShowEmpty("このフォルダには対象の写真・動画がありません");
                UpdateButtons();
                StatusText.Text = "対象ファイルが 0 件です";
                return;
            }

            _plan = string.IsNullOrWhiteSpace(fmt) ? new List<PlanItem>() : Planner.Build(_files, fmt, CurrentMode);
            Grid.ItemsSource = _plan;
            EmptyState.Visibility = _plan.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_plan.Count == 0) EmptyText.Text = "名前の形式を入力してください";

            int total = _files.Count;
            int change = _plan.Count(p => p.WillMove);
            int dup = _plan.Count(p => p.Status == ItemStatus.Suffixed || p.Status == ItemStatus.Skipped ||
                                       p.Status == ItemStatus.Overwrite || p.Status == ItemStatus.Remove);
            int byMtime = _plan.Count(p => p.File.Source == DateSource.FileTime);

            var parts = new List<string> { $"全 {total} 件", $"変更 {change} 件" };
            if (dup > 0) parts.Add($"重複 {dup} 件");
            SummaryText.Text = string.Join("　·　", parts);

            StatusText.Text = byMtime > 0
                ? $"{byMtime} 件は撮影日時が見つからないため、ファイルの更新日時を使います"
                : change > 0 ? "内容を確認して「名前を変更する」を押してください" : "変更が必要なファイルはありません";
            UpdateButtons();
        }

        void ShowEmpty(string text)
        {
            EmptyText.Text = text;
            EmptyState.Visibility = Visibility.Visible;
        }

        void UpdateButtons()
        {
            RunButton.IsEnabled = !_busy && _plan.Any(p => p.WillMove || p.Status == ItemStatus.Remove);
            UndoButton.IsEnabled = !_busy && _undo != null && _undo.Count > 0;
            ReloadButton.IsEnabled = !_busy && _folder != null;
        }

        void SetBusy(bool busy, string status)
        {
            _busy = busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.IsIndeterminate = busy;
            FormatBox.IsEnabled = PresetBox.IsEnabled = ConflictBox.IsEnabled = BackupCheck.IsEnabled = TokenPanel.IsEnabled = !busy;
            if (busy)
            {
                Grid.ItemsSource = null;
                ShowEmpty(status ?? "");
            }
            if (status != null) StatusText.Text = status;
            UpdateButtons();
        }

        // ---------------- 実行・元に戻す ----------------

        async void Run_Click(object sender, RoutedEventArgs e)
        {
            int change = _plan.Count(p => p.WillMove);
            int remove = _plan.Count(p => p.Status == ItemStatus.Remove);
            string msg = $"{change} 件のファイル名を変更します。";
            if (remove > 0) msg += $"\n\n⚠ 同じ名前になる {remove} 件のファイルはごみ箱へ移動します。";
            if (BackupCheck.IsChecked == true) msg += "\n\n実行前に .bak のコピーを作ります。";
            msg += "\n\nよろしいですか？";
            if (MessageBox.Show(this, msg, "確認", MessageBoxButton.OKCancel,
                    remove > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            string folder = _folder;
            var plan = _plan.ToList();
            bool backup = BackupCheck.IsChecked == true;
            SetBusy(true, "名前を変更しています…");
            var progress = new Progress<(int done, int total)>(p =>
            {
                Progress.IsIndeterminate = false;
                Progress.Maximum = p.total;
                Progress.Value = p.done;
            });

            RenameResult res;
            try { res = await Task.Run(() => Renamer.Execute(folder, plan, backup, progress)); }
            catch (Exception ex)
            {
                SetBusy(false, "エラーが発生しました");
                MessageBox.Show(this, ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SetBusy(false, null);

            _undo = res.Done;
            ShowResult("完了しました", res, $"変更: {res.Ok} 件",
                res.Removed > 0 ? $"ごみ箱へ移動: {res.Removed} 件" : null,
                res.Skipped > 0 ? $"変更しなかった: {res.Skipped} 件" : null);
            await LoadFolderAsync(folder);
            if (res.Done.Count > 0) StatusText.Text = $"{res.Ok} 件の名前を変更しました。「元に戻す」で取り消せます";
        }

        async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo == null || _undo.Count == 0) return;
            if (MessageBox.Show(this, $"直前に変更した {_undo.Count} 件のファイル名を元に戻しますか？", "元に戻す",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            var records = _undo;
            SetBusy(true, "元に戻しています…");
            RenameResult res;
            try { res = await Task.Run(() => Renamer.Undo(records)); }
            catch (Exception ex)
            {
                SetBusy(false, "エラーが発生しました");
                MessageBox.Show(this, ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SetBusy(false, null);
            _undo = null;
            ShowResult("元に戻しました", res, $"元に戻した: {res.Ok} 件");
            await LoadFolderAsync(_folder);
            StatusText.Text = $"{res.Ok} 件を元の名前に戻しました";
        }

        void ShowResult(string title, RenameResult res, params string[] lines)
        {
            var all = lines.Where(l => l != null).ToList();
            if (res.Failed > 0) all.Add($"失敗: {res.Failed} 件");
            if (res.Errors.Count > 0)
            {
                all.Add("");
                all.AddRange(res.Errors.Take(8));
                if (res.Errors.Count > 8) all.Add($"…ほか {res.Errors.Count - 8} 件");
            }
            MessageBox.Show(this, string.Join("\n", all), title, MessageBoxButton.OK,
                res.Failed > 0 || res.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }
}
