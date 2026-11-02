using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sonic06Launcher
{
    class MainForm : Form
    {
        readonly Config cfg = Config.Load();
        readonly Setup setup;
        GameSettings settings;
        readonly List<string> logLines = new List<string>();
        readonly List<Control> actions = new List<Control>();
        Process game;
        TabControl tabs;

        public void SelectTab(int i) => tabs.SelectedIndex = Math.Max(0, Math.Min(i, tabs.TabCount - 1));

        readonly ListView status = new ListView();
        readonly TextBox logBox = new TextBox();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label barText = new Label();
        Button playButton;
        ComboBox display, size, language;
        CheckBox vsync, mute, keyboard;

        public MainForm()
        {
            setup = new Setup(cfg, Log, Progress);
            settings = GameSettings.Load(cfg);
            // Every size below is in 96-DPI pixels; WinForms scales them to the
            // monitor, as the designer's generated code does. Without it, at 150%
            // the fonts grew and the boxes did not, and the text was cut off.
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "sonic06-recomp launcher";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(940, 700);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            ResumeLayout(false);
            Shown += (s, e) => RefreshStatus();
        }

        // ---- layout ---------------------------------------------------------

        void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(12, 8, 12, 0) };
            header.Controls.Add(new Label
            {
                Text = "An unofficial PC port of the PlayStation 3 version (BLES00028). It is built on this PC from your own disc;" +
                       " nothing from the game comes with the launcher.",
                Dock = DockStyle.Top, Height = 34, ForeColor = SystemColors.GrayText,
            });
            header.Controls.Add(new Label { Text = "sonic06-recomp", Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI Semibold", 14f) });

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(SetupTab());
            tabs.TabPages.Add(PlayTab());
            tabs.TabPages.Add(SupportTab());

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 210, Padding = new Padding(8) };
            logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Dock = DockStyle.Fill; logBox.Font = new Font("Consolas", 8.5f); logBox.BackColor = SystemColors.Window;
            var barRow = new Panel { Dock = DockStyle.Top, Height = 26 };
            bar.Dock = DockStyle.Fill; bar.Visible = false;
            barText.Dock = DockStyle.Right; barText.Width = 200; barText.TextAlign = ContentAlignment.MiddleRight;
            barRow.Controls.Add(bar); barRow.Controls.Add(barText);
            bottom.Controls.Add(logBox); bottom.Controls.Add(barRow);

            Controls.Add(tabs); Controls.Add(bottom); Controls.Add(header);
        }

        Button Action(string text, Func<Task> work, int width = 170)
        {
            var b = new Button { Text = text, Width = width, Height = 32, Margin = new Padding(4) };
            b.Click += async (s, e) => await Busy(work);
            actions.Add(b);
            return b;
        }

        TabPage SetupTab()
        {
            var page = new TabPage("Setup") { Padding = new Padding(8) };
            status.View = View.Details; status.FullRowSelect = true; status.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            status.Dock = DockStyle.Fill;
            status.Columns.Add("Step", 300); status.Columns.Add("", 60); status.Columns.Add("Details", 500);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 124, WrapContents = true };
            buttons.Controls.Add(Action("Set up everything", SetUpEverything, 200));
            buttons.Controls.Add(Action("Install build tools", InstallTools));
            buttons.Controls.Add(Action("Choose game ISO...", ChooseIso));
            buttons.Controls.Add(Action("Choose game folder...", ChooseFolder));
            buttons.Controls.Add(Action("Choose rpcs3.exe...", () => { ChooseRpcs3(); return Task.CompletedTask; }));
            buttons.Controls.Add(Action("Decrypt", () => setup.Decrypt()));
            buttons.Controls.Add(Action("Download FFmpeg", () => setup.DownloadFfmpeg()));
            buttons.Controls.Add(Action("Choose FFmpeg folder...", () => { ChooseFfmpeg(); return Task.CompletedTask; }));
            buttons.Controls.Add(Action("Build", () => setup.Build()));
            page.Controls.Add(status);
            page.Controls.Add(buttons);
            return page;
        }

        TabPage PlayTab()
        {
            var page = new TabPage("Play") { Padding = new Padding(16) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 230, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            display = Combo(new[] { "Window", "Borderless fullscreen" }, settings.Fullscreen ? 1 : 0);
            size = Combo(GameSettings.Sizes, Math.Max(0, Array.IndexOf(GameSettings.Sizes, settings.WindowSize)));
            language = Combo(GameSettings.Languages.Select(l => l.Name).ToArray(),
                             Math.Max(0, Array.FindIndex(GameSettings.Languages, l => l.Code == settings.Language)));
            vsync = new CheckBox { Text = "Wait for the display (VSync)", Checked = settings.VSync, AutoSize = true };
            mute = new CheckBox { Text = "Mute the sound", Checked = settings.Mute, AutoSize = true };
            keyboard = new CheckBox { Text = "Keyboard works as a controller", Checked = settings.Keyboard, AutoSize = true };
            Row(grid, "Display", display);
            Row(grid, "Window size", size);
            Row(grid, "Language", language);
            Row(grid, "", vsync);
            Row(grid, "", mute);
            Row(grid, "", keyboard);
            display.SelectedIndexChanged += (s, e) => size.Enabled = display.SelectedIndex == 0;
            size.Enabled = display.SelectedIndex == 0;
            foreach (var c in new Control[] { display, size, language }) ((ComboBox)c).SelectedIndexChanged += (s, e) => SaveSettings();
            foreach (var c in new[] { vsync, mute, keyboard }) c.CheckedChanged += (s, e) => SaveSettings();

            playButton = new Button { Text = "Play", Width = 200, Height = 48, Font = new Font("Segoe UI Semibold", 12f), Top = 250, Left = 16 };
            playButton.Click += (s, e) => Play();
            var note = new Label
            {
                Text = "An Xbox-style (XInput) controller works. Without one, the keyboard is the controller." +
                       " Fullscreen covers the primary monitor and stretches the 16:9 picture to it.",
                Top = 310, Left = 16, Width = 640, Height = 40, ForeColor = SystemColors.GrayText,
            };
            page.Controls.Add(grid); page.Controls.Add(playButton); page.Controls.Add(note);
            return page;
        }

        static ComboBox Combo(string[] items, int index)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            c.Items.AddRange(items);
            c.SelectedIndex = index;
            return c;
        }

        static void Row(TableLayoutPanel grid, string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) });
            grid.Controls.Add(c);
        }

        TabPage SupportTab()
        {
            var page = new TabPage("Support") { Padding = new Padding(16) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
            buttons.Controls.Add(Action("Update and rebuild", () => Support.Update(setup, Log)));
            buttons.Controls.Add(Action("Create a bug report", () => { MakeReport(); return Task.CompletedTask; }));
            var logs = new Button { Text = "Open the logs folder", Width = 170, Height = 32, Margin = new Padding(4) };
            logs.Click += (s, e) => { Directory.CreateDirectory(Workspace.Logs); Process.Start("explorer.exe", Workspace.Logs); };
            buttons.Controls.Add(logs);
            page.Controls.Add(new Label
            {
                Text = "A bug report is a zip on your desktop with this PC's specs, the setup status and the last game logs." +
                       " It has no game files, and your Windows user folder is replaced by %USERPROFILE%." +
                       " Attach it to an issue on the project's page, with what you did and what you saw.\r\n\r\n" +
                       "Update pulls the latest version of the project and builds the game again. The build is redone in" +
                       " full, so it takes as long as the first one.",
                Top = 60, Left = 16, Width = 640, Height = 120,
            });
            page.Controls.Add(buttons);
            return page;
        }

        // ---- log, progress, busy -------------------------------------------

        void Log(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
            logLines.Add(line);
            if (logBox.TextLength > 400000) logBox.Text = logBox.Text.Substring(200000);
            logBox.AppendText(line + Environment.NewLine);
        }

        void Progress(int done, int total)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int, int>(Progress), done, total); return; }
            bar.Visible = total > 0;
            barText.Text = total > 0 ? $"{done} / {total}" : "";
            if (total > 0) { bar.Maximum = total; bar.Value = Math.Min(done, total); }
        }

        async Task Busy(Func<Task> work)
        {
            foreach (var c in actions) c.Enabled = false;
            playButton.Enabled = false;
            try { await work(); }
            catch (Exception e)
            {
                Log("Stopped: " + e.Message);
                MessageBox.Show(this, e.Message, "sonic06-recomp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Progress(0, 0);
                foreach (var c in actions) c.Enabled = true;
                RefreshStatus();
            }
        }

        void RefreshStatus()
        {
            status.BeginUpdate();
            status.Items.Clear();
            foreach (var i in setup.Check())
            {
                var it = new ListViewItem(new[] { i.Step, i.Ok ? "ready" : "missing", i.Detail });
                it.ForeColor = i.Ok ? Color.DarkGreen : Color.Firebrick;
                status.Items.Add(it);
            }
            status.EndUpdate();
            playButton.Enabled = setup.ReadyToPlay && (game == null || game.HasExited);
        }

        // ---- steps ----------------------------------------------------------

        async Task SetUpEverything()
        {
            var tools = setup.MissingTools();
            var needElf = !File.Exists(Workspace.EbootElf);
            var plan = new List<string>();
            if (tools.Count > 0) plan.Add("install with winget: " + string.Join(", ", tools.Select(t => t.Name)));
            if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"))) plan.Add("copy your disc (ISO or folder) into game\\disc");
            if (needElf) plan.Add("decrypt EBOOT.BIN with your RPCS3");
            if (setup.FfmpegBin() == null || setup.FfmpegInclude() == null) plan.Add("download FFmpeg 8 (LGPL) from github.com/BtbN/FFmpeg-Builds");
            if (!File.Exists(Workspace.GameExe))
                plan.Add($"download ps3recomp from GitHub, translate the game code and build it ({Tools.BuildJobs()} files at a time; an hour or more on a typical PC)");
            if (plan.Count == 0) { Log("Everything is set up. Press Play."); return; }
            if (MessageBox.Show(this, "The launcher will:\n\n- " + string.Join("\n- ", plan) + "\n\nContinue?",
                                "Set up everything", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            if (tools.Count > 0) await setup.InstallTools(tools);
            if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME")))
            {
                if (!await ChooseIso() && !await ChooseFolder()) { Log("No disc chosen."); return; }
            }
            if (needElf)
            {
                if (setup.Rpcs3() == null && !ChooseRpcs3()) { Log("No RPCS3 chosen."); return; }
                await setup.Decrypt();
            }
            if (setup.FfmpegBin() == null || setup.FfmpegInclude() == null) await setup.DownloadFfmpeg();
            if (!File.Exists(Workspace.GameExe)) await setup.Build();
            Log("Setup finished. Press Play.");
        }

        async Task InstallTools()
        {
            var tools = setup.MissingTools();
            if (tools.Count == 0) { Log("All build tools are installed."); return; }
            if (MessageBox.Show(this, "Install with winget:\n\n- " + string.Join("\n- ", tools.Select(t => $"{t.Name} ({t.WingetId})")) +
                                "\n\nWindows may ask for permission for each one.", "Install build tools",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            await setup.InstallTools(tools);
        }

        async Task<bool> ChooseIso()
        {
            using (var d = new OpenFileDialog { Title = "Your BLES00028 disc image", Filter = "Disc image (*.iso)|*.iso" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                await setup.FromIso(d.FileName);
                return true;
            }
        }

        async Task<bool> ChooseFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "The folder of your BLES00028 disc (the one holding PS3_GAME)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                await setup.FromFolder(d.SelectedPath);
                return true;
            }
        }

        bool ChooseRpcs3()
        {
            using (var d = new OpenFileDialog { Title = "rpcs3.exe", Filter = "RPCS3 (rpcs3.exe)|rpcs3.exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                cfg.Set("rpcs3", d.FileName);
                Log("RPCS3: " + d.FileName);
                return true;
            }
        }

        void ChooseFfmpeg()
        {
            using (var d = new FolderBrowserDialog { Description = "An FFmpeg 8 shared build: the folder holding bin\\ and include\\" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                cfg.Set("ffmpeg", Path.Combine(d.SelectedPath, "bin"));
                cfg.Set("ffmpegInclude", Path.Combine(d.SelectedPath, "include"));
                if (setup.FfmpegBin() == null) Log("No avcodec-62.dll in that folder's bin\\: it is not FFmpeg 8.");
            }
        }

        void SaveSettings()
        {
            settings = new GameSettings
            {
                Fullscreen = display.SelectedIndex == 1,
                WindowSize = (string)size.SelectedItem,
                VSync = vsync.Checked,
                Language = GameSettings.Languages[language.SelectedIndex].Code,
                Mute = mute.Checked,
                Keyboard = keyboard.Checked,
            };
            settings.Save(cfg);
        }

        void Play()
        {
            if (!setup.ReadyToPlay) { Log("Finish the setup first."); return; }
            playButton.Enabled = false;
            game = Game.Start(setup, settings, Log, code => BeginInvoke(new Action(() =>
            {
                Log($"The game closed (exit code {code}).");
                RefreshStatus();
            })));
        }

        void MakeReport()
        {
            var zip = Support.CreateReport(setup, settings, logLines);
            Log("Bug report: " + zip);
            Process.Start("explorer.exe", "/select," + Runner.Quote(zip));
            if (Support.IssuesUrl.Length > 0) Process.Start(Support.IssuesUrl);
        }
    }
}
