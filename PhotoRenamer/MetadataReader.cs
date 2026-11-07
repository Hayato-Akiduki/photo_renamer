using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PhotoRenamer
{
    public enum DateSource { Exif, Video, FileTime, None }

    /// <summary>写真・動画ファイルから撮影日時を読み取る。</summary>
    public static class MetadataReader
    {
        static readonly string[] JpegExt = { ".jpg", ".jpeg" };
        static readonly string[] TiffExt = { ".tif", ".tiff" };
        static readonly string[] IsoMediaExt = { ".mp4", ".mov", ".m4v", ".3gp" };

        public static readonly string[] ImageExt =
            { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".tiff", ".tif", ".webp", ".bmp" };
        public static readonly string[] VideoExt =
            { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".flv", ".mts", ".m2ts" };

        public static bool IsVideo(string ext) => Array.IndexOf(VideoExt, ext) >= 0;

        public static (DateTime? date, DateSource source) GetDate(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool video = IsVideo(ext);
            DateTime? dt = null;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
                if (Array.IndexOf(JpegExt, ext) >= 0) dt = ReadJpegExif(fs);
                else if (Array.IndexOf(TiffExt, ext) >= 0) dt = ReadTiffDate(fs, 0);
                else if (Array.IndexOf(IsoMediaExt, ext) >= 0) dt = ReadMp4CreationTime(fs);
            }
            catch { /* 読めない場合は次の方法へ */ }

            // HEIC・WebP・AVI・MKV などは Windows のプロパティ機能で読む
            if (dt == null) dt = ShellDate(path, video);

            if (dt != null) return (dt, video ? DateSource.Video : DateSource.Exif);

            try { return (File.GetLastWriteTime(path), DateSource.FileTime); }
            catch { return (null, DateSource.None); }
        }

        // ---------------- JPEG / TIFF (EXIF) ----------------

        static DateTime? ReadJpegExif(Stream s)
        {
            if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8) return null;
            while (s.Position < s.Length)
            {
                if (s.ReadByte() != 0xFF) return null;
                int marker = s.ReadByte();
                while (marker == 0xFF) marker = s.ReadByte();
                if (marker < 0 || marker == 0xD9 || marker == 0xDA) return null;
                if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0x01) continue;

                int len = (s.ReadByte() << 8) | s.ReadByte();
                if (len < 2) return null;
                long next = s.Position + len - 2;

                if (marker == 0xE1 && len > 8)
                {
                    var head = new byte[6];
                    if (s.Read(head, 0, 6) == 6 && Encoding.ASCII.GetString(head, 0, 4) == "Exif")
                    {
                        var dt = ReadTiffDate(s, s.Position);
                        if (dt != null) return dt;
                    }
                }
                s.Position = next;
            }
            return null;
        }

        /// <summary>TIFF 構造（EXIF 本体）から日時を取り出す。優先度: 撮影 > デジタル化 > 更新</summary>
        static DateTime? ReadTiffDate(Stream s, long baseOffset)
        {
            var r = new TiffReader(s, baseOffset);
            if (!r.Init()) return null;

            string dateTime = null, original = null, digitized = null;
            uint exifIfd = 0;

            r.ReadIfd(r.FirstIfd, (tag, type, count, valuePos) =>
            {
                if (tag == 0x0132) dateTime = r.ReadAscii(type, count, valuePos);
                else if (tag == 0x8769) exifIfd = r.U32(valuePos);
                // 一部のソフトは撮影日時を IFD0 に直接書く
                else if (tag == 0x9003) original = r.ReadAscii(type, count, valuePos);
                else if (tag == 0x9004) digitized = r.ReadAscii(type, count, valuePos);
            });
            if (exifIfd != 0)
            {
                r.ReadIfd(exifIfd, (tag, type, count, valuePos) =>
                {
                    if (tag == 0x9003) original = r.ReadAscii(type, count, valuePos);
                    else if (tag == 0x9004) digitized = r.ReadAscii(type, count, valuePos);
                });
            }
            return ParseExifDate(original) ?? ParseExifDate(digitized) ?? ParseExifDate(dateTime);
        }

        static DateTime? ParseExifDate(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim('\0', ' ');
            if (v.Length > 19) v = v.Substring(0, 19);
            return DateTime.TryParseExact(v, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt) && dt.Year > 1900 ? dt : (DateTime?)null;
        }

        sealed class TiffReader
        {
            readonly Stream _s;
            readonly long _base;
            bool _le;
            public uint FirstIfd;

            public TiffReader(Stream s, long baseOffset) { _s = s; _base = baseOffset; }

            public bool Init()
            {
                var b = Bytes(0, 8);
                if (b == null) return false;
                if (b[0] == 'I' && b[1] == 'I') _le = true;
                else if (b[0] == 'M' && b[1] == 'M') _le = false;
                else return false;
                if (U16(2) != 42) return false;
                FirstIfd = U32(4);
                return true;
            }

            byte[] Bytes(long pos, int n)
            {
                if (_base + pos + n > _s.Length || pos < 0) return null;
                _s.Position = _base + pos;
                var buf = new byte[n];
                int read = 0;
                while (read < n) { int k = _s.Read(buf, read, n - read); if (k <= 0) return null; read += k; }
                return buf;
            }

            public ushort U16(long pos)
            {
                var b = Bytes(pos, 2) ?? throw new EndOfStreamException();
                return _le ? (ushort)(b[0] | b[1] << 8) : (ushort)(b[0] << 8 | b[1]);
            }

            public uint U32(long pos)
            {
                var b = Bytes(pos, 4) ?? throw new EndOfStreamException();
                return _le ? (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24)
                           : (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            }

            public void ReadIfd(uint offset, Action<ushort, ushort, uint, long> onEntry)
            {
                int n = U16(offset);
                if (n > 1000) return;
                for (int i = 0; i < n; i++)
                {
                    long e = offset + 2 + i * 12L;
                    onEntry(U16(e), U16(e + 2), U32(e + 4), e + 8);
                }
            }

            public string ReadAscii(ushort type, uint count, long valuePos)
            {
                if (type != 2 || count == 0 || count > 64) return null;
                long pos = count <= 4 ? valuePos : U32(valuePos);
                var b = Bytes(pos, (int)count);
                return b == null ? null : Encoding.ASCII.GetString(b);
            }
        }

        // ---------------- MP4 / MOV ----------------

        static DateTime? ReadMp4CreationTime(Stream s)
        {
            var moov = FindBox(s, 0, s.Length, "moov");
            if (moov == null) return null;
            var mvhd = FindBox(s, moov.Value.start, moov.Value.end, "mvhd");
            if (mvhd == null) return null;

            s.Position = mvhd.Value.start;
            int version = s.ReadByte();
            s.Position += 3;
            ulong secs = version == 1 ? ReadBE(s, 8) : ReadBE(s, 4);
            if (secs == 0) return null;

            // QuickTime は 1904/1/1 からの秒数 (UTC) で記録している
            var utc = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs);
            if (utc.Year < 1971 || utc > DateTime.UtcNow.AddDays(2)) return null;
            return utc.ToLocalTime();
        }

        static (long start, long end)? FindBox(Stream s, long from, long to, string type)
        {
            long pos = from;
            while (pos + 8 <= to)
            {
                s.Position = pos;
                long size = (long)ReadBE(s, 4);
                var t = new byte[4];
                if (s.Read(t, 0, 4) != 4) return null;
                int header = 8;
                if (size == 1) { size = (long)ReadBE(s, 8); header = 16; }
                else if (size == 0) size = to - pos;
                if (size < header) return null;
                if (Encoding.ASCII.GetString(t) == type) return (pos + header, Math.Min(pos + size, to));
                pos += size;
            }
            return null;
        }

        static ulong ReadBE(Stream s, int n)
        {
            ulong v = 0;
            for (int i = 0; i < n; i++)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                v = (v << 8) | (uint)b;
            }
            return v;
        }

        // ---------------- Windows プロパティシステム ----------------

        static PROPERTYKEY PKEY_Photo_DateTaken = new PROPERTYKEY(new Guid("14B81DA1-0135-4D31-96D9-6CBFC9671A99"), 36867);
        static PROPERTYKEY PKEY_Media_DateEncoded = new PROPERTYKEY(new Guid("2E4B640D-5019-46D8-8881-55414CC5CAA0"), 100);

        static DateTime? ShellDate(string path, bool video)
        {
            IPropertyStore store = null;
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreFromParsingPath(path, IntPtr.Zero, 0, ref iid, out store) != 0 || store == null)
                    return null;
                var key = video ? PKEY_Media_DateEncoded : PKEY_Photo_DateTaken;
                if (store.GetValue(ref key, out var pv) != 0) return null;
                try
                {
                    if (pv.vt != 64 /* VT_FILETIME */ || pv.filetime == 0) return null;
                    var dt = DateTime.FromFileTimeUtc(pv.filetime).ToLocalTime();
                    return dt.Year > 1900 ? dt : (DateTime?)null;
                }
                finally { PropVariantClear(ref pv); }
            }
            catch { return null; }
            finally
            {
                // ハンドルを残すとリネームできなくなるので確実に解放する
                if (store != null) Marshal.ReleaseComObject(store);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
            public PROPERTYKEY(Guid g, uint p) { fmtid = g; pid = p; }
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public long filetime;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetPropertyStoreFromParsingPath(string path, IntPtr pbc, int flags,
            ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);
    }
}
