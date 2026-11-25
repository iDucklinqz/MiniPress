using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MiniPress
{
    public enum Mode { Lossless, Normal, Super }

    public sealed class AudioStream
    {
        public string Codec;
        public int Kbps; // 0 = unknown
    }

    public sealed class MediaInfo
    {
        public double Duration; // seconds, 0 = unknown
        public bool HasVideo;
        public int Width;
        public int Height;
        public double Fps;      // 0 = unknown
        public readonly List<AudioStream> Audio = new List<AudioStream>();
    }

    /// <summary>Finding ffmpeg.exe, reading a file's streams, and turning a mode into ffmpeg arguments.</summary>
    public static class FFmpeg
    {
        static readonly Regex DurationRx = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        static readonly Regex StreamRx = new Regex(@"^Stream #\d+:\d+.*?: (Video|Audio): ([A-Za-z0-9_\-]+)");
        static readonly Regex SizeRx = new Regex(@"(?<!\d)(\d{2,5})x(\d{2,5})(?!\d)");
        static readonly Regex FpsRx = new Regex(@"(\d+(?:\.\d+)?) fps");
        static readonly Regex TbrRx = new Regex(@"(\d+(?:\.\d+)?) tbr");
        static readonly Regex KbpsRx = new Regex(@"(\d+) kb/s");

        // Keep width/height even (required by yuv420p H.264) without changing the picture.
        const string EvenSize = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        // Shorter side at most 720 px (works for landscape and portrait), never upscales.
        const string Shrink720 =
            "scale=w='if(gte(iw,ih),-2,trunc(min(720,iw)/2)*2)':h='if(gte(iw,ih),trunc(min(720,ih)/2)*2,-2)':flags=lanczos";

        /// <summary>
        /// The ffmpeg.exe embedded in MiniPress.exe, unpacked once to %LOCALAPPDATA%\MiniPress.
        /// Builds without the embedded copy fall back to an ffmpeg.exe found on this PC.
        /// </summary>
        public static string Locate()
        {
            return ExtractEmbedded() ?? FindInstalled();
        }

        static string ExtractEmbedded()
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniPress.ffmpeg.exe.gz"))
            {
                if (resource == null) return null;
                var packed = new MemoryStream();
                resource.CopyTo(packed);
                string id;
                using (var sha = SHA256.Create())
                    id = BitConverter.ToString(sha.ComputeHash(packed.ToArray()), 0, 6).Replace("-", "").ToLowerInvariant();

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPress");
                string target = Path.Combine(dir, "ffmpeg-" + id + ".exe");
                if (File.Exists(target)) return target;

                Directory.CreateDirectory(dir);
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                packed.Position = 0;
                using (var gz = new GZipStream(packed, CompressionMode.Decompress))
                using (var file = File.Create(temp))
                    gz.CopyTo(file);
                try { File.Move(temp, target); }
                catch (IOException)
                {
                    File.Delete(temp); // another MiniPress window unpacked it at the same moment
                    if (!File.Exists(target)) throw;
                }

                foreach (var old in Directory.GetFiles(dir, "ffmpeg-*.exe"))
                    if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(old); } catch (Exception) { }
                return target;
            }
        }

        static string FindInstalled()
        {
            var candidates = new List<string>();
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(appDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(appDir, "bin", "ffmpeg.exe"));
            candidates.Add(Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe"));

            // The process PATH can be stale (e.g. FFmpeg installed while MiniPress was open),
            // so also read the user and machine PATH from the registry.
            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string path = Environment.GetEnvironmentVariable("PATH", target);
                if (string.IsNullOrEmpty(path)) continue;
                foreach (var entry in path.Split(';'))
                {
                    string dir = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                    if (dir.Length == 0 || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                    candidates.Add(Path.Combine(dir, "ffmpeg.exe"));
                }
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe"));
            try
            {
                string packages = Path.Combine(local, @"Microsoft\WinGet\Packages");
                if (Directory.Exists(packages))
                    foreach (var dir in Directory.GetDirectories(packages, "*FFmpeg*"))
                        candidates.AddRange(Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories));
            }
            catch (Exception) { }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, @"scoop\shims\ffmpeg.exe"));
            candidates.Add(@"C:\ProgramData\chocolatey\bin\ffmpeg.exe");

            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>Reads stream information from the banner `ffmpeg -i file` prints.</summary>
        public static MediaInfo Probe(string ffmpeg, string file)
        {
            var psi = NewStartInfo(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", file });
            string text;
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEndAsync();
                text = p.StandardError.ReadToEnd();
                p.WaitForExit();
            }

            var info = new MediaInfo();
            var d = DurationRx.Match(text);
            if (d.Success)
                info.Duration = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60
                                + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);

            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                var m = StreamRx.Match(line);
                if (!m.Success) continue;
                string rest = line.Substring(m.Index + m.Length);
                if (m.Groups[1].Value == "Video")
                {
                    if (info.HasVideo || line.Contains("(attached pic)")) continue;
                    info.HasVideo = true;
                    var s = SizeRx.Match(rest);
                    if (s.Success) { info.Width = int.Parse(s.Groups[1].Value); info.Height = int.Parse(s.Groups[2].Value); }
                    var f = FpsRx.Match(rest);
                    if (!f.Success) f = TbrRx.Match(rest);
                    if (f.Success) info.Fps = double.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    var a = new AudioStream { Codec = m.Groups[2].Value.ToLowerInvariant() };
                    var k = KbpsRx.Match(rest);
                    if (k.Success) a.Kbps = int.Parse(k.Groups[1].Value);
                    info.Audio.Add(a);
                }
            }
            return info;
        }

        public static List<string> BuildArgs(Mode mode, MediaInfo info, string input, string output)
        {
            bool super = mode == Mode.Super;
            var a = new List<string> {
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
                "-i", input,
                "-map", "0:V:0", // first real video stream (skips cover art)
            };
            if (info.Audio.Count > 0) { a.Add("-map"); a.Add(super ? "0:a:0" : "0:a"); }

            int crf;
            string filter;
            switch (mode)
            {
                case Mode.Lossless: crf = 18; filter = EvenSize; break;
                case Mode.Normal: crf = 26; filter = EvenSize; break;
                default: crf = 28; filter = (info.Fps > 31 ? "fps=30," : "") + Shrink720; break;
            }
            a.AddRange(new[] {
                "-c:v", "libx264", "-preset", "slow", "-crf", crf.ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p", "-vf", filter,
            });

            if (info.Audio.Count > 0)
            {
                int kbps = mode == Mode.Lossless ? 192 : mode == Mode.Normal ? 128 : 96;
                var streams = super ? info.Audio.Take(1) : info.Audio;
                // Re-encoding AAC loses a little quality each time; copy it when it is already small enough.
                bool copy = streams.All(s => s.Codec == "aac" &&
                                             (mode == Mode.Lossless || (s.Kbps > 0 && s.Kbps <= kbps + kbps / 4)));
                if (copy) a.AddRange(new[] { "-c:a", "copy" });
                else
                {
                    a.AddRange(new[] { "-c:a", "aac", "-b:a", kbps + "k" });
                    if (super) a.AddRange(new[] { "-ac", "2" });
                }
            }

            a.AddRange(new[] { "-max_muxing_queue_size", "4096", "-movflags", "+faststart", "-f", "mp4", output });
            return a;
        }

        public static string Suffix(Mode mode)
        {
            switch (mode)
            {
                case Mode.Lossless: return "_无损压缩";
                case Mode.Normal: return "_压缩";
                default: return "_超级压缩";
            }
        }

        /// <summary>"dir\name_压缩.mp4", or "name_压缩 (2).mp4" etc. if that is taken.</summary>
        public static string OutputPathFor(string input, Mode mode, string dir)
        {
            string name = Path.GetFileNameWithoutExtension(input) + Suffix(mode);
            string path = Path.Combine(dir, name + ".mp4");
            for (int i = 2; File.Exists(path) || File.Exists(path + ".part"); i++)
                path = Path.Combine(dir, name + " (" + i + ").mp4");
            return path;
        }

        internal static ProcessStartInfo NewStartInfo(string exe, IEnumerable<string> args)
        {
            return new ProcessStartInfo(exe, JoinArgs(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        /// <summary>Quotes arguments the way CommandLineToArgvW / the MSVC runtime parse them.</summary>
        public static string JoinArgs(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) { sb.Append(arg); continue; }
                sb.Append('"');
                int slashes = 0;
                foreach (char c in arg)
                {
                    if (c == '\\') { slashes++; continue; }
                    if (c == '"') sb.Append('\\', slashes * 2 + 1);
                    else sb.Append('\\', slashes);
                    slashes = 0;
                    sb.Append(c);
                }
                sb.Append('\\', slashes * 2);
                sb.Append('"');
            }
            return sb.ToString();
        }
    }

    public sealed class EncodeResult
    {
        public int ExitCode;
        public bool Cancelled;
        public string Error;
    }

    /// <summary>One ffmpeg run, with progress (seconds of output written) and cancellation.</summary>
    public sealed class EncodeJob
    {
        readonly object gate = new object();
        Process process;
        bool cancelled;

        public event Action<double> Progress;

        public Task<EncodeResult> RunAsync(string ffmpeg, List<string> args)
        {
            return Task.Run(() => Run(ffmpeg, args));
        }

        EncodeResult Run(string ffmpeg, List<string> args)
        {
            var errors = new StringBuilder();
            long outUs = -1;
            using (var p = new Process { StartInfo = FFmpeg.NewStartInfo(ffmpeg, args) })
            {
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    int eq = e.Data.IndexOf('=');
                    if (eq <= 0) return;
                    string key = e.Data.Substring(0, eq);
                    string value = e.Data.Substring(eq + 1).Trim();
                    long us;
                    if ((key == "out_time_us" || key == "out_time_ms") && long.TryParse(value, out us) && us >= 0)
                        outUs = us; // both keys carry microseconds
                    else if (key == "progress" && outUs >= 0)
                    {
                        var handler = Progress;
                        if (handler != null) handler(outUs / 1e6);
                    }
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (errors) if (errors.Length < 16000) errors.AppendLine(e.Data);
                };

                lock (gate)
                {
                    if (cancelled) return new EncodeResult { Cancelled = true, ExitCode = -1 };
                    p.Start();
                    process = p;
                }
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                lock (gate) process = null;

                lock (errors)
                    return new EncodeResult { ExitCode = p.ExitCode, Cancelled = cancelled, Error = errors.ToString().Trim() };
            }
        }

        public void Cancel()
        {
            lock (gate)
            {
                cancelled = true;
                if (process != null)
                    try { process.Kill(); } catch (Exception) { }
            }
        }

        /// <summary>Blocks until the ffmpeg process has gone (used when the window closes mid-encode).</summary>
        public void WaitForExit(int milliseconds)
        {
            Process p;
            lock (gate) p = process;
            if (p == null) return;
            try { p.WaitForExit(milliseconds); } catch (Exception) { }
        }
    }
}
