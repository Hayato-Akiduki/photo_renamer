"""
写真・動画 一括リネームツール - 撮影日/制作日でファイル名を変更
必要ライブラリ: pip install pillow hachoir
"""

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import os
import re
import shutil
from pathlib import Path
from datetime import datetime, timezone
from PIL import Image
from PIL.ExifTags import TAGS


# ============================================================
# 日時取得
# ============================================================

def _get_video_date(filepath: Path) -> datetime | None:
    """動画ファイルのメタデータから制作日時を取得（hachoir使用）。"""
    try:
        from hachoir.parser import createParser
        from hachoir.metadata import extractMetadata

        parser = createParser(str(filepath))
        if not parser:
            return None
        with parser:
            metadata = extractMetadata(parser)
        if not metadata:
            return None

        # 優先順位: creation_date > last_modification > date_time_original
        for key in ("creation_date", "last_modification", "date_time_original"):
            val = metadata.get(key)
            if val:
                # hachoir は datetime オブジェクトを返す (UTC の場合あり)
                if isinstance(val, datetime):
                    # タイムゾーン情報があればローカルに変換
                    if val.tzinfo is not None:
                        val = val.astimezone().replace(tzinfo=None)
                    return val
        return None
    except Exception:
        return None


def get_date_taken(filepath: Path) -> tuple[datetime | None, str]:
    """
    ファイルの撮影/制作日時と取得ソースを返す。
    ソース: "exif" | "video_meta" | "mtime" | None
    """
    ext = filepath.suffix.lower()

    # --- 動画 ---
    VIDEO_EXT = {".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".flv", ".mts", ".m2ts"}
    if ext in VIDEO_EXT:
        dt = _get_video_date(filepath)
        if dt:
            return dt, "video_meta"
        # フォールバック: ファイル更新日時
        try:
            return datetime.fromtimestamp(os.path.getmtime(filepath)), "mtime"
        except Exception:
            return None, "no_date"

    # --- 画像 ---
    try:
        with Image.open(filepath) as img:
            exif_data = img._getexif()
            if exif_data:
                for tag_id, value in exif_data.items():
                    tag = TAGS.get(tag_id, tag_id)
                    if tag in ("DateTimeOriginal", "DateTimeDigitized", "DateTime"):
                        return datetime.strptime(value, "%Y:%m:%d %H:%M:%S"), "exif"
    except Exception:
        pass

    try:
        return datetime.fromtimestamp(os.path.getmtime(filepath)), "mtime"
    except Exception:
        return None, "no_date"


# ============================================================
# リネームロジック
# ============================================================

SUPPORTED_EXT = {
    # 画像
    ".jpg", ".jpeg", ".png", ".heic", ".heif", ".tiff", ".tif", ".webp", ".bmp",
    # 動画
    ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".flv", ".mts", ".m2ts",
}

def collect_files(folder: Path) -> list[Path]:
    return sorted(
        p for p in folder.iterdir()
        if p.is_file() and p.suffix.lower() in SUPPORTED_EXT
    )

def build_new_name(dt: datetime, fmt: str, index: int, original_stem: str, keep_original: bool) -> str:
    name = fmt
    name = name.replace("{YYYY}", dt.strftime("%Y"))
    name = name.replace("{MM}",   dt.strftime("%m"))
    name = name.replace("{DD}",   dt.strftime("%d"))
    name = name.replace("{HH}",   dt.strftime("%H"))
    name = name.replace("{mm}",   dt.strftime("%M"))
    name = name.replace("{SS}",   dt.strftime("%S"))
    name = name.replace("{N}",    f"{index:04d}")
    name = name.replace("{original}", original_stem if keep_original else "")
    return name

def preview_rename(folder: Path, fmt: str, keep_original: bool,
                   conflict_mode: str = "suffix"
                   ) -> list[tuple[str, str, str]]:
    """
    戻り値: [(元ファイル名, 新ファイル名, ステータス), ...]
    conflict_mode: "suffix"=連番追加 / "skip"=スキップ / "overwrite"=上書き
    """
    files = collect_files(folder)
    results = []
    seen_names: dict[str, int] = {}  # suffix モード用カウンタ

    for i, p in enumerate(files, start=1):
        dt, source = get_date_taken(p)

        if source == "no_date" or dt is None:
            results.append((p.name, "—", "no_date"))
            continue

        new_stem = build_new_name(dt, fmt, i, p.stem, keep_original)
        new_stem = re.sub(r'[\\/:*?"<>|]', "_", new_stem)
        new_name = new_stem + p.suffix.lower()

        key = new_name.lower()
        is_dup = key in seen_names

        if is_dup:
            if conflict_mode == "suffix":
                seen_names[key] += 1
                base, ext = os.path.splitext(new_name)
                new_name = f"{base}_{seen_names[key]}{ext}"
                status = "conflict_suffix"
            elif conflict_mode == "skip":
                results.append((p.name, "—（スキップ）", "conflict_skip"))
                continue
            else:  # overwrite
                status = "conflict_overwrite"
        else:
            seen_names[key] = 0
            status = source  # "exif" | "video_meta" | "mtime"

        results.append((p.name, new_name, status))

    return results

def execute_rename(folder: Path, preview: list[tuple[str, str, str]],
                   backup: bool) -> tuple[int, int, int]:
    """(成功数, 失敗数, スキップ数) を返す"""
    ok = fail = skipped = 0
    for orig, new, status in preview:
        if status in ("no_date", "conflict_skip"):
            if status == "conflict_skip":
                skipped += 1
            else:
                fail += 1
            continue
        src = folder / orig
        dst = folder / new
        if src == dst:
            ok += 1
            continue
        try:
            if backup:
                shutil.copy2(src, folder / (orig + ".bak"))
            # 上書きモードの場合: 既存ファイルを先に消してから rename
            if status == "conflict_overwrite" and dst.exists():
                dst.unlink()
            src.rename(dst)
            ok += 1
        except Exception:
            fail += 1
    return ok, fail, skipped


# ============================================================
# GUI
# ============================================================

STATUS_COLOR = {
    "exif":              "#2ecc71",
    "video_meta":        "#3498db",
    "mtime":             "#f39c12",
    "conflict_suffix":   "#e67e22",
    "conflict_skip":     "#95a5a6",
    "conflict_overwrite":"#e74c3c",
    "no_date":           "#636375",
}
STATUS_LABEL = {
    "exif":              "EXIF",
    "video_meta":        "動画メタ",
    "mtime":             "更新日",
    "conflict_suffix":   "重複→連番",
    "conflict_skip":     "重複→スキップ",
    "conflict_overwrite":"重複→上書き",
    "no_date":           "日付なし",
}

BG         = "#0f0f13"
PANEL      = "#1a1a22"
ACCENT     = "#7c6af7"
ACCENT2    = "#a78bfa"
FG         = "#e8e8f0"
FG_DIM     = "#888899"
BORDER     = "#2a2a38"
FONT_MAIN  = ("Segoe UI", 10)
FONT_TITLE = ("Segoe UI Semibold", 13)
FONT_MONO  = ("Consolas", 9)


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("📷 写真・動画リネーマー")
        self.geometry("940x660")
        self.minsize(720, 520)
        self.configure(bg=BG)
        self.resizable(True, True)

        self._folder = tk.StringVar()
        self._fmt    = tk.StringVar(value="{YYYY}{MM}{DD}_{HH}{mm}{SS}")
        self._backup = tk.BooleanVar(value=False)
        self._keep   = tk.BooleanVar(value=False)
        self._conflict = tk.StringVar(value="suffix")  # suffix / skip / overwrite
        self._preview_data: list[tuple[str, str, str]] = []

        self._build_ui()
        self._check_hachoir()

    def _check_hachoir(self):
        try:
            import hachoir  # noqa
        except ImportError:
            self._status.set(
                "⚠️  hachoir 未インストール: MP4等の動画メタデータを読むには  pip install hachoir  を実行してください"
            )

    # ---- UI構築 ------------------------------------------------

    def _build_ui(self):
        tk.Frame(self, bg=ACCENT, height=4).pack(fill="x")
        body = tk.Frame(self, bg=BG)
        body.pack(fill="both", expand=True, padx=16, pady=12)

        left = tk.Frame(body, bg=PANEL, bd=0, padx=16, pady=16,
                        highlightbackground=BORDER, highlightthickness=1)
        left.pack(side="left", fill="y", padx=(0, 12))
        left.pack_propagate(False)
        left.config(width=268)
        self._build_left(left)

        right = tk.Frame(body, bg=BG)
        right.pack(side="left", fill="both", expand=True)
        self._build_right(right)

    def _lbl(self, parent, text, font=None, color=FG):
        return tk.Label(parent, text=text, font=font or FONT_MAIN,
                        fg=color, bg=parent["bg"], anchor="w")

    def _build_left(self, p):
        self._lbl(p, "📁 フォルダ選択", FONT_TITLE).pack(anchor="w", pady=(0, 8))
        frm = tk.Frame(p, bg=PANEL); frm.pack(fill="x")
        tk.Entry(frm, textvariable=self._folder, font=FONT_MONO,
                 bg="#12121a", fg=FG, insertbackground=FG,
                 relief="flat", bd=4).pack(side="left", fill="x", expand=True)
        tk.Button(frm, text="…", command=self._pick_folder,
                  bg=ACCENT, fg="white", relief="flat", font=FONT_MAIN,
                  padx=8, cursor="hand2").pack(side="left", padx=(4, 0))

        _sep(p)
        self._lbl(p, "✏️ ファイル名フォーマット", FONT_TITLE).pack(anchor="w", pady=(0, 6))
        tk.Entry(p, textvariable=self._fmt, font=FONT_MONO,
                 bg="#12121a", fg=ACCENT2, insertbackground=ACCENT2,
                 relief="flat", bd=4).pack(fill="x")

        hints = [
            ("{YYYY}", "年 (4桁)"), ("{MM}", "月"), ("{DD}", "日"),
            ("{HH}", "時"), ("{mm}", "分"), ("{SS}", "秒"),
            ("{N}", "連番 (0001〜)"), ("{original}", "元のファイル名"),
        ]
        hf = tk.Frame(p, bg=PANEL); hf.pack(fill="x", pady=(8, 0))
        for tag, desc in hints:
            row = tk.Frame(hf, bg=PANEL); row.pack(fill="x", pady=1)
            tk.Label(row, text=tag,  font=FONT_MONO, fg=ACCENT2, bg=PANEL, width=12, anchor="w").pack(side="left")
            tk.Label(row, text=desc, font=("Segoe UI", 9), fg=FG_DIM, bg=PANEL, anchor="w").pack(side="left")

        _sep(p)
        self._lbl(p, "⚙️ オプション", FONT_TITLE).pack(anchor="w", pady=(0, 6))
        for text, var in [("元ファイル名を保持 ({original})", self._keep),
                           ("実行前にバックアップ (.bak)",    self._backup)]:
            tk.Checkbutton(p, text=text, variable=var,
                           bg=PANEL, fg=FG, selectcolor="#252535",
                           activebackground=PANEL, activeforeground=FG,
                           font=("Segoe UI", 9), anchor="w",
                           command=self._on_opt).pack(fill="x", pady=2)

        _sep(p, pady=6)
        self._lbl(p, "重複ファイル名の処理", color=FG_DIM).pack(anchor="w", pady=(0, 4))
        conflict_opts = [
            ("suffix",    "連番を追加  (例: _1, _2)"),
            ("skip",      "スキップ（リネームしない）"),
            ("overwrite", "上書き（元ファイルを削除）"),
        ]
        for val, text in conflict_opts:
            tk.Radiobutton(p, text=text, variable=self._conflict, value=val,
                           bg=PANEL, fg=FG, selectcolor="#252535",
                           activebackground=PANEL, activeforeground=FG,
                           font=("Segoe UI", 9), anchor="w",
                           command=self._on_opt).pack(fill="x", pady=1)

        _sep(p)
        tk.Button(p, text="🔍  プレビュー", command=self._do_preview,
                  bg=ACCENT, fg="white", font=("Segoe UI Semibold", 11),
                  relief="flat", cursor="hand2", pady=8).pack(fill="x", pady=(0, 6))
        self._run_btn = tk.Button(p, text="▶  リネーム実行", command=self._do_rename,
                                  bg="#2ecc71", fg="white", font=("Segoe UI Semibold", 11),
                                  relief="flat", cursor="hand2", pady=8, state="disabled")
        self._run_btn.pack(fill="x")

        # 対応形式メモ
        _sep(p, pady=6)
        self._lbl(p, "対応形式", color=FG_DIM).pack(anchor="w")
        self._lbl(p, "画像: JPG PNG HEIC TIFF WebP BMP", font=("Segoe UI", 8), color=FG_DIM).pack(anchor="w")
        self._lbl(p, "動画: MP4 MOV AVI MKV M4V WMV …", font=("Segoe UI", 8), color=FG_DIM).pack(anchor="w")

    def _build_right(self, p):
        hdr = tk.Frame(p, bg=BG); hdr.pack(fill="x", pady=(0, 8))
        self._lbl(hdr, "プレビュー", FONT_TITLE).pack(side="left")
        self._count_lbl = self._lbl(hdr, "", color=FG_DIM)
        self._count_lbl.pack(side="left", padx=12)

        leg = tk.Frame(p, bg=BG); leg.pack(fill="x", pady=(0, 6))
        for key, label in STATUS_LABEL.items():
            tk.Label(leg, text="●", fg=STATUS_COLOR[key], bg=BG, font=("Segoe UI", 9)).pack(side="left")
            tk.Label(leg, text=label, fg=FG_DIM, bg=BG, font=("Segoe UI", 9)).pack(side="left", padx=(0, 10))

        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Ph.Treeview",
                        background="#14141e", foreground=FG,
                        fieldbackground="#14141e", rowheight=22,
                        font=FONT_MONO, borderwidth=0)
        style.configure("Ph.Treeview.Heading",
                        background=PANEL, foreground=FG_DIM,
                        font=("Segoe UI Semibold", 9), relief="flat")
        style.map("Ph.Treeview", background=[("selected", ACCENT)])

        tf = tk.Frame(p, bg=BG); tf.pack(fill="both", expand=True)
        self._tree = ttk.Treeview(tf, columns=("original", "new", "status"),
                                  show="headings", style="Ph.Treeview", selectmode="none")
        self._tree.heading("original", text="元のファイル名")
        self._tree.heading("new",      text="変更後のファイル名")
        self._tree.heading("status",   text="ソース")
        self._tree.column("original", width=290, minwidth=160)
        self._tree.column("new",      width=290, minwidth=160)
        self._tree.column("status",   width=90,  minwidth=70, anchor="center")

        vsb = ttk.Scrollbar(tf, orient="vertical", command=self._tree.yview)
        self._tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y")
        self._tree.pack(fill="both", expand=True)

        self._status = tk.StringVar(value="フォルダを選択してプレビューしてください")
        tk.Label(p, textvariable=self._status, font=("Segoe UI", 9),
                 fg=FG_DIM, bg=BG, anchor="w").pack(fill="x", pady=(6, 0))

    # ---- イベント -----------------------------------------------

    def _pick_folder(self):
        d = filedialog.askdirectory(title="フォルダを選択")
        if d:
            self._folder.set(d)
            self._do_preview()

    def _on_opt(self):
        if self._preview_data:
            self._do_preview()

    def _do_preview(self):
        folder = self._folder.get().strip()
        if not folder or not os.path.isdir(folder):
            messagebox.showwarning("フォルダ未選択", "有効なフォルダを選択してください。")
            return
        fmt = self._fmt.get().strip()
        if not fmt:
            messagebox.showwarning("フォーマット未入力", "ファイル名フォーマットを入力してください。")
            return

        self._status.set("プレビュー生成中…")
        self.update_idletasks()

        try:
            data = preview_rename(Path(folder), fmt, self._keep.get(),
                                  self._conflict.get())
        except Exception as e:
            messagebox.showerror("エラー", str(e))
            return

        self._preview_data = data

        for row in self._tree.get_children():
            self._tree.delete(row)
        for orig, new, status in data:
            self._tree.insert("", "end", values=(orig, new, STATUS_LABEL.get(status, status)),
                              tags=(status,))
            self._tree.tag_configure(status, foreground=STATUS_COLOR.get(status, FG))

        total    = len(data)
        no_date  = sum(1 for _, _, s in data if s == "no_date")
        skipped  = sum(1 for _, _, s in data if s == "conflict_skip")
        conflict = sum(1 for _, _, s in data if s in ("conflict_suffix", "conflict_overwrite"))
        self._count_lbl.config(text=f"{total} 件")
        msgs = [f"合計 {total} ファイル"]
        if no_date:   msgs.append(f"日付不明 {no_date} 件")
        if skipped:   msgs.append(f"重複スキップ {skipped} 件")
        if conflict:  msgs.append(f"重複あり {conflict} 件")
        self._status.set("　|　".join(msgs))
        self._run_btn.config(state="normal" if total > 0 else "disabled")

    def _do_rename(self):
        if not self._preview_data:
            return
        folder = Path(self._folder.get())
        n = sum(1 for _, _, s in self._preview_data if s not in ("no_date", "conflict_skip"))
        overwrite_n = sum(1 for _, _, s in self._preview_data if s == "conflict_overwrite")

        msg = f"{n} 件のファイルをリネームしますか？"
        if overwrite_n:
            msg += f"\n\n⚠️  上書きされるファイルが {overwrite_n} 件あります。\n元のファイルは削除されます。"
        if not messagebox.askyesno("確認", msg):
            return

        ok, fail, skipped = execute_rename(folder, self._preview_data, self._backup.get())
        lines = [f"✅ 成功: {ok} 件"]
        if skipped: lines.append(f"⏭️  スキップ: {skipped} 件")
        if fail:    lines.append(f"❌ 失敗: {fail} 件")
        messagebox.showinfo("完了", "\n".join(lines))
        self._do_preview()


# ---- ユーティリティ ------------------------------------------------

def _sep(parent, pady=8):
    tk.Frame(parent, bg=BORDER, height=1).pack(fill="x", pady=pady)


# ============================================================
if __name__ == "__main__":
    app = App()
    app.mainloop()
