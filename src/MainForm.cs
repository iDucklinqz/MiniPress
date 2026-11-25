using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniPress
{
    sealed class MainForm : Form
    {
        static readonly string[] VideoExtensions = {
            ".mp4", ".mkv", ".mov", ".avi", ".flv", ".wmv", ".webm", ".m4v", ".ts", ".mts", ".m2ts",
            ".3gp", ".mpg", ".mpeg", ".vob", ".f4v", ".rmvb", ".rm", ".asf", ".ogv",
        };
        const string EmptyText = "拖入视频，或点击选择";
        const string IdleHint = "普通最常用，超级体积最小。原视频不会被改动。";

        sealed class Item
        {
            public string Path;
            public long Size;
            public MediaInfo Info;
        }

        readonly DropZone drop = new DropZone();
        readonly Button[] modeButtons;
        readonly ProgressBar bar = new ProgressBar();
        readonly Label status = new Label();
        readonly Button actionButton = new Button();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        readonly string[] startupFiles;

        List<Item> items = new List<Item>();
        string ffmpeg;
        bool busy;
        EncodeJob job;
        string currentPart;
        string lastOutput;
        int progressToken;

        public MainForm(string[] args)
        {
            startupFiles = args ?? new string[0];
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = "MiniPress";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            ClientSize = new Size(Dpi.S(460), Dpi.S(262));
            Icon = LoadAppIcon();

            drop.Bounds = Dpi.R(12, 12, 436, 124);
            drop.Text = EmptyText;
            drop.Click += delegate { PickAndLoad(); };
            Controls.Add(drop);

            var modes = new[] { Mode.Lossless, Mode.Normal, Mode.Super };
            string[] names = { "无损", "普通", "超级" };
            string[] hints = {
                "画质肉眼无差别，体积适度减小（H.264 CRF 18）",
                "画质清晰，体积大幅减小（H.264 CRF 26）",
                "最高 720p、30 帧，体积最小（H.264 CRF 28）",
            };
            modeButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var b = new Button { Text = names[i], Tag = modes[i], Bounds = Dpi.R(12 + i * 148, 148, 140, 34) };
                b.Click += OnModeClick;
                tips.SetToolTip(b, hints[i]);
                modeButtons[i] = b;
                Controls.Add(b);
            }

            bar.Bounds = Dpi.R(12, 194, 436, 10);
            bar.Maximum = 1000;
            Controls.Add(bar);

            status.Bounds = Dpi.R(12, 212, 330, 42);
            status.UseMnemonic = false;
            status.AutoEllipsis = true;
            Controls.Add(status);

            actionButton.Bounds = Dpi.R(348, 214, 100, 30);
            actionButton.Visible = false;
            actionButton.Click += delegate { if (job != null) CancelEncoding(); else RevealLastOutput(); };
            Controls.Add(actionButton);

            ResumeLayout(false);
            WireDragDrop(this);
            SetStatus("选好视频后，点一个模式开始压缩", IdleHint);
        }

        // ---------- lifecycle ----------

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // First launch unpacks the built-in FFmpeg (about a second); later launches reuse it.
            SetInteractive(false);
            SetStatus("正在准备…", null);
            try { ffmpeg = await Task.Run(() => FFmpeg.Locate()); }
            catch (Exception) { ffmpeg = null; }
            finally { SetInteractive(true); }
            SetStatus("选好视频后，点一个模式开始压缩", IdleHint);
            if (ffmpeg == null) EnsureFFmpeg(); // shows why
            if (startupFiles.Length > 0)
                await Guard(LoadFilesAsync(startupFiles));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var running = job;
            if (running != null)
            {
                var answer = MessageBox.Show(this, "正在压缩，确定要退出吗？\n没压完的文件会被删除，原视频不受影响。",
                    "MiniPress", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                running.Cancel();
                running.WaitForExit(5000);
                TryDelete(currentPart);
            }
            Native.KeepAwake(false);
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && job != null) { CancelEncoding(); e.Handled = true; }
            else if (e.KeyCode == Keys.O && e.Control && !busy) { PickAndLoad(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        // ---------- choosing files ----------

        void WireDragDrop(Control root)
        {
            root.AllowDrop = true;
            root.DragEnter += OnDragOverAny;
            root.DragOver += OnDragOverAny;
            root.DragLeave += delegate { drop.DragActive = false; };
            root.DragDrop += OnDragDropAny;
            foreach (Control child in root.Controls) WireDragDrop(child);
        }

        void OnDragOverAny(object sender, DragEventArgs e)
        {
            bool ok = !busy && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            drop.DragActive = ok;
        }

        async void OnDragDropAny(object sender, DragEventArgs e)
        {
            drop.DragActive = false;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (busy || paths == null) return;
            Activate();
            await Guard(LoadFilesAsync(paths));
        }

        async void PickAndLoad()
        {
            if (busy) return;
            var paths = PickFiles();
            if (paths != null) await Guard(LoadFilesAsync(paths));
        }

        string[] PickFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要压缩的视频";
                dialog.Multiselect = true;
                dialog.Filter = "视频文件|" + string.Join(";", VideoExtensions.Select(x => "*" + x)) + "|所有文件|*.*";
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
            }
        }

        /// <summary>Reads the dropped/picked files. Returns false if nothing usable was chosen.</summary>
        async Task<bool> LoadFilesAsync(IEnumerable<string> paths)
        {
            var files = ExpandPaths(paths);
            if (files.Count == 0)
            {
                SetStatus("没有找到视频文件", "可以拖入视频文件，或者装着视频的文件夹");
                return false;
            }
            if (!EnsureFFmpeg()) return false;

            SetInteractive(false);
            drop.Text = "正在读取…";
            string exe = ffmpeg;
            List<Item> probed;
            try { probed = await Task.Run(() => files.Select(f => ProbeItem(exe, f)).ToList()); }
            finally { SetInteractive(true); }

            var valid = probed.Where(i => i.Info != null && i.Info.HasVideo).ToList();
            int bad = probed.Count - valid.Count;
            if (valid.Count == 0)
            {
                UpdateDrop();
                SetStatus("无法识别", files.Count == 1
                    ? Path.GetFileName(files[0]) + " 不是视频，或者文件已经损坏"
                    : "这些文件都不是能识别的视频");
                return false;
            }

            items = valid;
            lastOutput = null;
            actionButton.Visible = false;
            bar.Value = 0;
            UpdateDrop();
            SetStatus(bad > 0 ? "已忽略 " + bad + " 个无法识别的文件" : "选好视频后，点一个模式开始压缩", IdleHint);
            return true;
        }

        static List<string> ExpandPaths(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    try
                    {
                        result.AddRange(Directory.GetFiles(p)
                            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                    }
                    catch (Exception) { }
                }
                else if (File.Exists(p) && !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    result.Add(p);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static Item ProbeItem(string exe, string path)
        {
            var item = new Item { Path = path };
            try
            {
                item.Size = new FileInfo(path).Length;
                item.Info = FFmpeg.Probe(exe, path);
            }
            catch (Exception) { item.Info = null; }
            return item;
        }

        void UpdateDrop()
        {
            if (items.Count == 0) { drop.Text = EmptyText; return; }
            if (items.Count == 1)
            {
                var it = items[0];
                var parts = new List<string> { FormatSize(it.Size) };
                if (it.Info.Duration > 0) parts.Add(FormatClock(it.Info.Duration));
                if (it.Info.Width > 0) parts.Add(it.Info.Width + "×" + it.Info.Height);
                if (it.Info.Fps > 0) parts.Add(Math.Round(it.Info.Fps) + " fps");
                drop.Text = Path.GetFileName(it.Path) + "\n" + string.Join(" · ", parts);
                return;
            }
            double seconds = items.Sum(i => i.Info.Duration);
            drop.Text = "已选择 " + items.Count + " 个视频\n共 " + FormatSize(items.Sum(i => i.Size)) +
                        (seconds > 0 ? " · 总时长 " + FormatClock(seconds) : "");
        }

        // ---------- compressing ----------

        async void OnModeClick(object sender, EventArgs e)
        {
            if (busy) return;
            var mode = (Mode)((Button)sender).Tag;
            try
            {
                if (items.Count == 0)
                {
                    // No file yet: let the user pick one, then go straight on with this mode.
                    var paths = PickFiles();
                    if (paths == null || !await LoadFilesAsync(paths)) return;
                }
                await CompressAllAsync(mode);
            }
            catch (Exception ex) { ShowCrash(ex); }
        }

        async Task CompressAllAsync(Mode mode)
        {
            if (!EnsureFFmpeg()) return;

            var queue = items.ToList();
            double total = queue.Sum(i => Weight(i));
            double doneWeight = 0;
            int ok = 0, skipped = 0, failed = 0;
            long before = 0, after = 0;
            string lastError = null, lastErrorDetail = null;
            bool cancelled = false;

            SetInteractive(false);
            lastOutput = null;
            actionButton.Text = "取消";
            actionButton.Enabled = true;
            actionButton.Visible = true;
            bar.Value = 0;
            tips.SetToolTip(status, null);
            Native.KeepAwake(true);
            try
            {
                for (int n = 0; n < queue.Count; n++)
                {
                    var item = queue[n];
                    string name = Path.GetFileName(item.Path);
                    string heading = queue.Count > 1
                        ? string.Format("正在压缩（{0}/{1}）：{2}", n + 1, queue.Count, name)
                        : "正在压缩：" + name;
                    SetStatus(heading, "准备中…");
                    double weight = Weight(item);

                    if (!File.Exists(item.Path))
                    {
                        failed++;
                        lastError = name + "：文件不见了";
                        doneWeight += weight;
                        continue;
                    }

                    string output = FFmpeg.OutputPathFor(item.Path, mode, ChooseOutputDir(item.Path));
                    string part = output + ".part";
                    currentPart = part;

                    int token = ++progressToken;
                    double baseWeight = doneWeight, duration = item.Info.Duration;
                    var clock = Stopwatch.StartNew();
                    job = new EncodeJob();
                    job.Progress += seconds => PostToUi(() =>
                    {
                        if (token == progressToken)
                            ShowProgress(heading, seconds, duration, baseWeight, weight, total, clock.Elapsed);
                    });

                    EncodeResult result = await job.RunAsync(ffmpeg, FFmpeg.BuildArgs(mode, item.Info, item.Path, part));
                    job = null;
                    progressToken++;

                    if (result.Cancelled)
                    {
                        TryDelete(part);
                        cancelled = true;
                        break;
                    }

                    long newSize = File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (result.ExitCode != 0 || newSize == 0)
                    {
                        TryDelete(part);
                        failed++;
                        lastError = name + "：" + LastLine(result.Error);
                        lastErrorDetail = result.Error;
                    }
                    else if (newSize >= item.Size)
                    {
                        TryDelete(part); // compressing made it bigger: keep only the original
                        skipped++;
                    }
                    else
                    {
                        if (File.Exists(output)) output = FFmpeg.OutputPathFor(item.Path, mode, Path.GetDirectoryName(output));
                        File.Move(part, output);
                        ok++;
                        before += item.Size;
                        after += newSize;
                        lastOutput = output;
                    }
                    currentPart = null;
                    doneWeight += weight;
                    SetBar(doneWeight / total);
                }
            }
            finally
            {
                job = null;
                currentPart = null;
                progressToken++;
                Native.KeepAwake(false);
                actionButton.Visible = false;
                SetInteractive(true);
            }

            ShowSummary(mode, queue.Count, ok, skipped, failed, before, after, cancelled, lastError, lastErrorDetail);
        }

        void ShowProgress(string heading, double seconds, double duration, double baseWeight, double weight, double total, TimeSpan elapsed)
        {
            double fraction = duration > 0 ? Math.Max(0, Math.Min(1, seconds / duration)) : 0;
            SetBar((baseWeight + fraction * weight) / total);

            if (duration <= 0) { SetStatus(heading, "已处理 " + FormatClock(seconds)); return; }
            string text = (int)(fraction * 100) + "%";
            if (fraction >= 0.03 && elapsed.TotalSeconds >= 2)
                text += " · 预计还要 " + FormatRemaining(elapsed.TotalSeconds * (1 - fraction) / fraction);
            SetStatus(heading, text);
        }

        void ShowSummary(Mode mode, int count, int ok, int skipped, int failed, long before, long after,
                         bool cancelled, string lastError, string lastErrorDetail)
        {
            if (lastOutput != null)
            {
                actionButton.Text = "打开文件夹";
                actionButton.Enabled = true;
                actionButton.Visible = true;
            }
            string saved = ok > 0 ? string.Format("{0} → {1}，减小了 {2}%", FormatSize(before), FormatSize(after),
                                                  (int)Math.Round(100.0 * (before - after) / before)) : "";
            if (ok == 0) bar.Value = 0;
            if (cancelled)
            {
                SetStatus(ok > 0 ? "已取消（之前的 " + ok + " 个已经压好）" : "已取消", ok > 0 ? saved : "原视频不受影响");
                return;
            }

            if (count == 1)
            {
                if (ok == 1) SetStatus("压缩完成", saved);
                else if (skipped == 1)
                    SetStatus("这个视频已经很精简了", mode == Mode.Super
                        ? "压缩后没有变小，已保留原文件"
                        : "压缩后没有变小，已保留原文件。可以试试「超级」");
                else
                {
                    SetStatus("压缩失败", lastError);
                    tips.SetToolTip(status, lastErrorDetail);
                }
                return;
            }

            var parts = new List<string>();
            if (ok > 0) parts.Add("成功 " + ok + " 个");
            if (skipped > 0) parts.Add(skipped + " 个已经很精简，没有压缩");
            if (failed > 0) parts.Add("失败 " + failed + " 个");
            SetStatus(failed == 0 && skipped == 0 ? "全部完成，共 " + ok + " 个视频" : string.Join("，", parts),
                      ok > 0 ? saved : (lastError ?? ""));
            if (failed > 0) tips.SetToolTip(status, lastErrorDetail);
        }

        void CancelEncoding()
        {
            var running = job;
            if (running == null) return;
            actionButton.Enabled = false;
            running.Cancel();
        }

        void RevealLastOutput()
        {
            if (lastOutput == null) return;
            if (File.Exists(lastOutput)) Process.Start("explorer.exe", "/select,\"" + lastOutput + "\"");
            else if (Directory.Exists(Path.GetDirectoryName(lastOutput))) Process.Start(Path.GetDirectoryName(lastOutput));
        }

        static string ChooseOutputDir(string input)
        {
            string dir = Path.GetDirectoryName(input);
            return CanWrite(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".minipress-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (Exception) { return false; }
        }

        static double Weight(Item item) { return Math.Max(item.Info.Duration, 1); }

        // ---------- FFmpeg availability ----------

        bool EnsureFFmpeg()
        {
            if (ffmpeg != null && File.Exists(ffmpeg)) return true;
            try { ffmpeg = FFmpeg.Locate(); }
            catch (Exception ex)
            {
                ffmpeg = null;
                SetStatus("无法准备 FFmpeg", ex.Message);
                return false;
            }
            if (ffmpeg != null) return true;
            SetStatus("找不到 FFmpeg", "这个版本没有内置 FFmpeg，请把 ffmpeg.exe 放到 MiniPress.exe 旁边");
            return false;
        }

        // ---------- helpers ----------

        void SetInteractive(bool on)
        {
            busy = !on;
            foreach (var b in modeButtons) b.Enabled = on;
            drop.Enabled = on;
            drop.Cursor = on ? Cursors.Hand : Cursors.Default;
        }

        void SetStatus(string main, string sub)
        {
            status.Text = string.IsNullOrEmpty(sub) ? main : main + "\n" + sub;
        }

        void SetBar(double fraction)
        {
            bar.Value = (int)(Math.Max(0, Math.Min(1, fraction)) * bar.Maximum);
        }

        void PostToUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        async Task Guard(Task task)
        {
            try { await task; }
            catch (Exception ex) { ShowCrash(ex); }
        }

        void ShowCrash(Exception ex)
        {
            SetInteractive(true);
            actionButton.Visible = false;
            SetStatus("出了点问题", ex.Message);
            tips.SetToolTip(status, ex.ToString());
        }

        static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < 10 && File.Exists(path); i++)
            {
                try { File.Delete(path); return; }
                catch (IOException) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) { Thread.Sleep(100); }
            }
        }

        static string LastLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "FFmpeg 没有说明原因";
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string line = lines[lines.Length - 1].Trim();
            return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.00") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        static string FormatClock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        static string FormatRemaining(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + " 秒";
            if (t.TotalHours < 1) return t.Minutes + " 分 " + t.Seconds + " 秒";
            return (int)t.TotalHours + " 小时 " + t.Minutes + " 分";
        }

        static Icon LoadAppIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniPress.icon.ico"))
                    return s != null ? new Icon(s) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
