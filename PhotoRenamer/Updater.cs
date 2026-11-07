using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhotoRenamer
{
    /// <summary>GitHub Releases を見て新しいバージョンがあれば更新する。</summary>
    public static class Updater
    {
        public const string Repo = "Hayato-Akiduki/photo_renamer";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
        const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

        public sealed class UpdateInfo
        {
            public Version Version;
            public string Tag;
            public string Notes;
            public string DownloadUrl;
            public string FileName;
            public string Sha256; // GitHub が出すハッシュ（無い場合は null）
        }

        public static Version CurrentVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "PhotoRenamer-Updater";
            return wc;
        }

        /// <summary>新しいバージョンがあれば返す。無い・確認できないときは null。</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            Release rel;
            using (var wc = NewClient())
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                var data = await wc.DownloadDataTaskAsync(LatestApi);
                using var ms = new MemoryStream(data);
                rel = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(ms);
            }
            if (rel == null || rel.Draft || rel.Prerelease) return null;
            if (!Version.TryParse((rel.TagName ?? "").TrimStart('v', 'V'), out var latest)) return null;
            if (Normalize(latest) <= Normalize(CurrentVersion)) return null;

            var asset = rel.Assets?.FirstOrDefault(a =>
                a.Name != null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0);
            if (asset == null) return null;

            string sha = null;
            if (asset.Digest != null && asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = asset.Digest.Substring(7).ToLowerInvariant();

            return new UpdateInfo
            {
                Version = latest,
                Tag = rel.TagName,
                Notes = rel.Body ?? "",
                DownloadUrl = asset.Url,
                FileName = Path.GetFileName(asset.Name),
                Sha256 = sha,
            };
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        /// <summary>インストーラーをダウンロードして確認する。保存先のパスを返す。</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int> progress)
        {
            // 必ず GitHub からのダウンロードだけを受け付ける
            var uri = new Uri(info.DownloadUrl);
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ダウンロード先が正しくありません。");

            string dir = Path.Combine(Path.GetTempPath(), "PhotoRenamerUpdate");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, info.FileName);

            using (var wc = NewClient())
            {
                wc.DownloadProgressChanged += (s, e) => progress?.Report(e.ProgressPercentage);
                await wc.DownloadFileTaskAsync(uri, path);
            }

            if (info.Sha256 != null)
            {
                string actual;
                using (var fs = File.OpenRead(path))
                using (var sha = SHA256.Create())
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (actual != info.Sha256)
                {
                    try { File.Delete(path); } catch { }
                    throw new InvalidOperationException("ダウンロードしたファイルが壊れています。もう一度お試しください。");
                }
            }
            return path;
        }

        /// <summary>インストーラーを起動する（このあとアプリは終了する）。</summary>
        public static void RunInstaller(string installerPath)
        {
            // 前回と同じ場所（自分用 / すべてのユーザー用）に入れる
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            bool perUser = appDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StringComparison.OrdinalIgnoreCase);
            string args = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /update=1 "
                        + (perUser ? "/CURRENTUSER" : "/ALLUSERS");
            Process.Start(new ProcessStartInfo(installerPath, args) { UseShellExecute = true });
        }

        public static void OpenReleasesPage() => Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });

        // ---- GitHub API の JSON ----

        [DataContract]
        sealed class Release
        {
            [DataMember(Name = "tag_name")] public string TagName;
            [DataMember(Name = "body")] public string Body;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Prerelease;
            [DataMember(Name = "assets")] public Asset[] Assets;
        }

        [DataContract]
        sealed class Asset
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string Url;
            [DataMember(Name = "digest")] public string Digest;
        }
    }
}
