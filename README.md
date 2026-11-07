# 写真・動画リネーマー

写真や動画のファイル名を、**撮影日時**でまとめて変更する Windows アプリです。

例: `IMG_1234.JPG` → `20261106_142233.jpg`

## ダウンロード

[Releases](https://github.com/Hayato-Akiduki/photo_renamer/releases/latest) から `PhotoRenamer_Setup_x.x.x.exe` をダウンロードして実行してください。

- Windows 10 / 11 で動きます（追加のソフトは不要です）
- 管理者権限がなくてもインストールできます
- 新しいバージョンが出ると、アプリの上部にお知らせが出て、ボタン1つで更新できます

> 初めて実行するときに「Windows によって PC が保護されました」と出た場合は、「詳細情報」→「実行」を押してください（署名をしていないアプリで出る表示です）。

## 使い方

1. フォルダを選ぶ（ドラッグ＆ドロップでも OK）
2. 新しい名前の形式を選ぶ（「年」「月」「連番」などのボタンで自由に作れます）
3. 一覧で変更後の名前を確認して「名前を変更する」

間違えたときは「元に戻す」で直前の変更を取り消せます。

### 名前に使えるもの

| ボタン | 書き方 | 例 |
|---|---|---|
| 年 | `{YYYY}` | 2026 |
| 月 | `{MM}` | 11 |
| 日 | `{DD}` | 06 |
| 時 | `{HH}` | 14 |
| 分 | `{mm}` | 22 |
| 秒 | `{SS}` | 33 |
| 連番 | `{N}` | 0001（撮影日時の古い順） |
| 元の名前 | `{original}` | IMG_1234 |

### 撮影日時の読み取り

| 種類 | 読み取り元 |
|---|---|
| JPG / TIFF | EXIF の撮影日時 |
| MP4 / MOV / M4V / 3GP | 動画の作成日時 |
| HEIC / WebP / PNG / AVI / MKV など | Windows のファイル情報 |
| 上で見つからないとき | ファイルの更新日時 |

HEIC（iPhone の写真）を読むには、Microsoft Store の「HEIF 画像拡張機能」が必要です。

## 開発

- C# / WPF（.NET Framework 4.8）
- インストーラー: [Inno Setup 6](https://jrsoftware.org/isinfo.php)

```powershell
.\build.ps1   # アプリとインストーラーを dist-installer\ に作る
```

### リリースの手順

1. `PhotoRenamer/PhotoRenamer.csproj` の `<Version>` を上げる
2. コミットして、同じ番号のタグを push する

```powershell
git tag v2.2.0
git push origin main --tags
```

GitHub Actions がインストーラーを作って Releases に公開し、使っている人のアプリに更新のお知らせが出ます。
