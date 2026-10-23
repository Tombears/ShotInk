using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class MainForm : Form
    {
        private readonly string dataRoot;
        private readonly string historyRoot;
        private readonly bool desktopIntegration;
        private readonly ListView history;
        private readonly ImageList thumbnails;
        private readonly Label notice;
        private readonly CheckBox autoSave;
        private readonly NumericUpDown historyLimit;
        private NotifyIcon tray;
        private Icon appIcon;
        private bool captureBusy;
        private bool quitting;
        private HotkeyBindings hotkeys;
        private readonly AppSettings settings;
        private readonly Timer settingsTimer;
        private bool settingsDirty;
        private readonly Label shortcuts;
        private readonly List<Button> actionCards = new List<Button>();
        private string SettingsPath { get { return Path.Combine(dataRoot, "settings.ini"); } }

        internal MainForm() : this(null, true) { }
        internal MainForm(string storageRoot, bool integrateDesktop)
        {
            desktopIntegration = integrateDesktop;
            dataRoot = storageRoot ?? ResolveDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            settings = AppSettings.Load(SettingsPath);
            settingsTimer = new Timer { Interval = 350 };
            settingsTimer.Tick += delegate { settingsTimer.Stop(); PersistSettings(); };
            settings.Changed += delegate { settingsDirty = true; settingsTimer.Stop(); settingsTimer.Start(); };
            historyRoot = Path.Combine(dataRoot, "captures");
            Directory.CreateDirectory(historyRoot);
            Text = "ShotInk " + Application.ProductVersion + " · 截图与长截图";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9);
            Size = new Size(1080, 730);
            MinimumSize = new Size(930, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            appIcon = CreateIcon(); Icon = appIcon;

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 202, BackColor = Color.FromArgb(24, 32, 53), Padding = new Padding(22) };
            var brand = Theme.Label("▣  ShotInk", 20, Color.White); brand.Location = new Point(20, 30); sidebar.Controls.Add(brand);
            var edition = Theme.Label("S H O T I N K   /   W I N D O W S", 8, Color.FromArgb(149, 163, 194)); edition.Location = new Point(27, 75); sidebar.Controls.Add(edition);
            var nav = Theme.Label("●   截图工作台", 11, Color.FromArgb(170, 181, 255)); nav.Location = new Point(25, 145); sidebar.Controls.Add(nav);
            var sub = Theme.Label("捕捉 · 标注 · 贴图", 9, Color.FromArgb(149, 163, 194)); sub.Location = new Point(25, 180); sidebar.Controls.Add(sub);
            shortcuts = Theme.Label("", 9, Color.FromArgb(185, 197, 219));
            shortcuts.MaximumSize = new Size(152, 0);
            shortcuts.Location = new Point(25, 292); sidebar.Controls.Add(shortcuts);
            var preferences = Theme.Button("设置", false, 145); preferences.Location = new Point(25, 460);
            preferences.Click += delegate { OpenSettings(); }; sidebar.Controls.Add(preferences);
            var local = Theme.Label("所有截图保存在本机\n无需登录 · 无云端上传", 8, Color.FromArgb(130, 148, 176)); local.Location = new Point(25, 540); sidebar.Controls.Add(local);
            Controls.Add(sidebar);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(30, 22, 30, 18), BackColor = Theme.Background };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(content); content.BringToFront();

            var heading = new Panel { Dock = DockStyle.Fill };
            var title = Theme.Label("截图工作台", 20, Theme.Ink); title.Location = new Point(0, 0); heading.Controls.Add(title);
            var intro = Theme.Label("记录屏幕上的每一个好想法。", 9, Theme.Muted); intro.Location = new Point(2, 40); heading.Controls.Add(intro);
            content.Controls.Add(heading, 0, 0);

            var hero = new HeroPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 10) };
            var heroTitle = Theme.Label("把灵感，留在此刻。", 24, Color.White); heroTitle.Location = new Point(24, 20); hero.Controls.Add(heroTitle);
            var heroText = Theme.Label("从一小块屏幕，到一整页内容。", 10, Color.FromArgb(219, 224, 255)); heroText.Location = new Point(28, 76); hero.Controls.Add(heroText);
            content.Controls.Add(hero, 0, 1);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
            for (int i = 0; i < 2; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.Controls.Add(ActionCard("截图", "F1 · 选区标注 / 滚动长截图", true, delegate { BeginCapture(false); }), 0, 0);
            actions.Controls.Add(ActionCard("剪贴板贴图", "F3 · 让图片悬浮在桌面", false, delegate { PinClipboard(); }), 1, 0);
            content.Controls.Add(actions, 0, 2);

            var historyBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
            var recent = Theme.Label("最近截图", 12, Theme.Ink); recent.Margin = new Padding(0, 10, 12, 0); historyBar.Controls.Add(recent);
            var folder = Theme.Button("打开文件夹", false, 108); folder.Click += delegate { Process.Start("explorer.exe", historyRoot); }; historyBar.Controls.Add(folder);
            var refresh = Theme.Button("刷新", false, 62); refresh.Click += delegate { LoadHistory(); }; historyBar.Controls.Add(refresh);
            autoSave = new CheckBox { Text = "保留历史", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 15, 0, 0), Checked = settings.AutoHistory };
            historyLimit = new NumericUpDown { Name = "history-limit", Minimum = 1, Maximum = 10000, Value = settings.HistoryLimit, Width = 65, Margin = new Padding(3, 12, 2, 0), Enabled = autoSave.Checked };
            autoSave.CheckedChanged += delegate { settings.AutoHistory = autoSave.Checked; historyLimit.Enabled = autoSave.Checked; settings.NotifyChanged(); };
            historyLimit.ValueChanged += delegate { settings.HistoryLimit = (int)historyLimit.Value; settings.NotifyChanged(); PruneHistory(); LoadHistory(); };
            historyBar.Controls.Add(autoSave); historyBar.Controls.Add(historyLimit);
            historyBar.Controls.Add(new Label { Text = "张", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 16, 0, 0) }); content.Controls.Add(historyBar, 0, 3);

            thumbnails = new ImageList { ImageSize = new Size(150, 94), ColorDepth = ColorDepth.Depth32Bit };
            history = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = thumbnails, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink, HideSelection = false, Margin = new Padding(0, 3, 0, 0) };
            history.DoubleClick += delegate { OpenHistory(); };
            history.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) OpenHistory(); };
            content.Controls.Add(history, 0, 4);
            notice = Theme.Label("准备就绪 · 截图后可标注、复制、保存和贴图；双击历史图片继续编辑。", 8, Theme.Muted);
            notice.Dock = DockStyle.Fill; notice.Padding = new Padding(0, 13, 0, 0); content.Controls.Add(notice, 0, 5);
            LoadHistory();
            if (desktopIntegration) SetupTray();
            RefreshShortcutLabels();
        }

        internal static string ResolveDataRoot(string localApplicationData)
        {
            var current = Path.Combine(localApplicationData, "ShotInk");
            var legacy = Path.Combine(localApplicationData, "SnipasteLite");
            // Reuse existing history and settings when upgrading from the previous name.
            return !Directory.Exists(current) && Directory.Exists(legacy) ? legacy : current;
        }

        private void PersistSettings()
        {
            if (!settingsDirty) return;
            try { settings.Save(SettingsPath); settingsDirty = false; }
            catch (Exception ex) { if (notice != null) notice.Text = "设置保存失败：" + ex.Message; }
        }
        private void OpenSettings() { using (var dialog = new SettingsForm(settings, ApplySettings)) dialog.ShowDialog(this); }
        internal string ApplySettings(AppSettings candidate)
        {
            try { candidate.Validate(); } catch (Exception ex) { return ex.Message; }
            string error;
            if (hotkeys != null && !settings.Shortcuts.SequenceEqual(candidate.Shortcuts))
            { if (!hotkeys.TryApply(candidate.Shortcuts, delegate { candidate.Save(SettingsPath); }, out error)) return error; }
            else try { candidate.Save(SettingsPath); } catch (Exception ex) { return "设置未保存：" + ex.Message; }
            settings.CopyFrom(candidate); settingsDirty = false; settingsTimer.Stop();
            RefreshShortcutLabels(); if (notice != null) notice.Text = "设置已保存并立即生效。"; return null;
        }
        private void RefreshShortcutLabels()
        {
            shortcuts.Text = "全局快捷键\n\n" + Shortcut.Display(settings.CaptureShortcut) + "  区域截图\n" + Shortcut.Display(settings.PinShortcut) + "  剪贴板贴图\n" + Shortcut.Display(settings.ScrollShortcut) + "  长截图";
            if (actionCards.Count == 2)
            {
                actionCards[0].Text = "截图\n" + Shortcut.Display(settings.CaptureShortcut) + " · 标注 / 滚动长截图";
                actionCards[1].Text = "剪贴板贴图\n" + Shortcut.Display(settings.PinShortcut) + " · 让图片悬浮在桌面";
            }
            if (tray != null)
            {
                var items = tray.ContextMenuStrip.Items;
                items[1].Text = "区域截图  " + Shortcut.Display(settings.CaptureShortcut);
                items[2].Text = "滚动长截图  " + Shortcut.Display(settings.ScrollShortcut);
                items[3].Text = "剪贴板贴图  " + Shortcut.Display(settings.PinShortcut);
                tray.Text = "ShotInk · 截图 / 长截图 / 贴图";
            }
        }

        private Control ActionCard(string title, string subtitle, bool primary, Action action)
        {
            var button = Theme.Button(title + "\n" + subtitle, primary, 210);
            button.Dock = DockStyle.Fill; button.Height = 72; button.Font = new Font("Microsoft YaHei UI", 10); button.Margin = new Padding(0, 0, 10, 0);
            button.Click += delegate { action(); };
            actionCards.Add(button);
            return button;
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开截图工作台", null, delegate { ShowHome(); });
            menu.Items.Add("区域截图  F1", null, delegate { BeginCapture(false); });
            menu.Items.Add("滚动长截图  Ctrl+Alt+L", null, delegate { BeginCapture(true); });
            menu.Items.Add("剪贴板贴图  F3", null, delegate { PinClipboard(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置", null, delegate { ShowHome(); OpenSettings(); });
            menu.Items.Add("退出", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = appIcon, Text = "ShotInk · F1 截图 / Ctrl+Alt+L 长截图", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowHome(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!desktopIntegration) return;
            IntPtr window = Handle;
            hotkeys = new HotkeyBindings(delegate(int id, Keys keys) { return Native.RegisterHotKey(window, id, Shortcut.Modifiers(keys), (uint)(keys & Keys.KeyCode)); },
                delegate(int id) { Native.UnregisterHotKey(window, id); });
            string conflicts = hotkeys.RegisterInitial(settings.Shortcuts);
            if (conflicts.Length > 0 && notice != null) notice.Text = conflicts + " 已被占用，可在设置中修改，或使用工作台与托盘菜单。";
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeys != null) { hotkeys.Clear(); hotkeys = null; } base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.HotKeyMessage)
            {
                int id = m.WParam.ToInt32();
                if (id == 1) BeginCapture(false);
                if (id == 2 && !captureBusy) PinClipboard();
                if (id == 3) BeginCapture(true);
            }
            base.WndProc(ref m);
        }

        private async void BeginCapture(bool scroll)
        {
            if (captureBusy) return;
            captureBusy = true;
            var restore = new List<Form>();
            foreach (Form form in Application.OpenForms)
                if (form.Visible && !(form is PinForm)) restore.Add(form);
            foreach (var form in restore) form.Hide();
            Action restoreWindows = delegate { foreach (var form in restore) if (!form.IsDisposed) form.Show(); };
            try
            {
                await Task.Delay(200);
                if (IsDisposed) return;
                var overlay = new CaptureOverlay(scroll, settings);
                overlay.Cancelled += delegate { captureBusy = false; restoreWindows(); };
                overlay.Completed += delegate(Bitmap image, CaptureAction action)
                {
                    captureBusy = false; restoreWindows();
                    AcceptCapture(image, false, action == CaptureAction.Pin, action == CaptureAction.Copy);
                };
                overlay.Show(); overlay.Activate();
            }
            catch (Exception ex) { captureBusy = false; restoreWindows(); MessageBox.Show(ex.Message, "无法截图"); }
        }

        private void AcceptCapture(Bitmap image, bool openEditor = true, bool pinImage = false, bool copyImage = true)
        {
            try
            {
                if (copyImage)
                {
                    try { Clipboard.SetDataObject(image, true, 5, 100); }
                    catch (System.Runtime.InteropServices.ExternalException) { notice.Text = "剪贴板暂时不可用，已打开编辑器供重新复制。"; openEditor = true; }
                }
                if (autoSave.Checked)
                {
                    try
                    {
                        string file = Path.Combine(historyRoot, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                        image.Save(file, ImageFormat.Png);
                        PruneHistory();
                        LoadHistory();
                    }
                    catch (Exception ex) { notice.Text = "自动保存失败：" + ex.Message + "；仍可手动保存。"; }
                }
                if (openEditor) { var editor = new EditorForm(image, PinImage, settings); editor.Show(); }
                else if (pinImage) PinImage(image);
                else image.Dispose();
            }
            catch { image.Dispose(); throw; }
        }

        private void PruneHistory()
        {
            if (!settings.AutoHistory) return;
            foreach (var stale in new DirectoryInfo(historyRoot).GetFiles("*.png").OrderByDescending(file => file.LastWriteTimeUtc).Skip(settings.HistoryLimit))
                try { stale.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private void LoadHistory()
        {
            history.BeginUpdate(); history.Items.Clear(); thumbnails.Images.Clear();
            // Materialize the native list before temporary thumbnail bitmaps are disposed.
            IntPtr imageListHandle = thumbnails.Handle;
            try
            {
                foreach (var file in new DirectoryInfo(historyRoot).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Take(settings.HistoryLimit))
                {
                    try
                    {
                        using (var image = Image.FromFile(file.FullName))
                        using (var thumbnail = new Bitmap(150, 94))
                        {
                            using (var g = Graphics.FromImage(thumbnail))
                            {
                                g.Clear(Color.FromArgb(239, 242, 248));
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                float factor = Math.Min(146F / image.Width, 90F / image.Height);
                                int w = Math.Max(1, (int)(image.Width * factor)); int h = Math.Max(1, (int)(image.Height * factor));
                                g.DrawImage(image, (150 - w) / 2, (94 - h) / 2, w, h);
                            }
                            thumbnails.Images.Add(thumbnail);
                            var item = new ListViewItem(file.LastWriteTime.ToString("MM-dd HH:mm:ss"), thumbnails.Images.Count - 1) { Tag = file.FullName, ToolTipText = image.Width + " × " + image.Height + " px" };
                            history.Items.Add(item);
                        }
                    }
                    catch (ArgumentException) { }
                    catch (IOException) { }
                    catch (OutOfMemoryException) { }
                }
            }
            catch (IOException ex) { notice.Text = "读取历史失败：" + ex.Message; }
            finally { history.EndUpdate(); }
        }
        private void OpenHistory()
        {
            if (history.SelectedItems.Count == 0) return;
            try { using (var source = Image.FromFile((string)history.SelectedItems[0].Tag)) new EditorForm(new Bitmap(source), PinImage, settings).Show(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开图片"); LoadHistory(); }
        }
        private void PinClipboard()
        {
            try
            {
                if (!Clipboard.ContainsImage()) { MessageBox.Show("剪贴板中没有图片。先截图或复制一张图片，再使用贴图快捷键。", "剪贴板贴图"); return; }
                using (var source = Clipboard.GetImage()) if (source != null) PinImage(new Bitmap(source));
            }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("剪贴板正在被其他程序使用，请稍后重试。", "无法读取剪贴板"); }
        }
        private void PinImage(Bitmap image) { new PinForm(image).Show(); }
        private void ShowHome() { Show(); WindowState = FormWindowState.Normal; Activate(); LoadHistory(); }
        private void Quit()
        {
            // Let editors finish their save prompts before disposing the main message loop.
            var editors = Application.OpenForms.Cast<Form>().OfType<EditorForm>().ToArray();
            foreach (var editor in editors) { editor.Close(); if (!editor.IsDisposed) return; }
            quitting = true; Close();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && desktopIntegration && e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; Hide(); if (tray != null) tray.ShowBalloonTip(1500, "ShotInk 仍在运行", "截图快捷键：" + Shortcut.Display(settings.CaptureShortcut) + "；可从托盘打开设置或退出。", ToolTipIcon.Info); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                settingsTimer.Stop(); PersistSettings(); settingsTimer.Dispose();
                if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
                if (thumbnails != null) thumbnails.Dispose();
                if (appIcon != null) appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
        internal static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Theme.Accent);
                using (var p = new Pen(Color.White, 3)) { g.DrawRectangle(p, 7, 9, 18, 14); g.DrawLine(p, 12, 6, 20, 6); }
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    }

    internal sealed class HeroPanel : Panel
    {
        internal HeroPanel() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(83, 96, 236), Color.FromArgb(117, 102, 230), 15)) e.Graphics.FillRectangle(gradient, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(45, Color.White), 2))
            { e.Graphics.DrawEllipse(pen, Width - 200, -85, 240, 240); e.Graphics.DrawEllipse(pen, Width - 170, -55, 180, 180); }
        }
    }
}
