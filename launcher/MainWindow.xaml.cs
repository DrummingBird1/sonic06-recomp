using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sonic06Launcher
{
    /// <summary>
    /// The launcher window. It is its own view model: the pages bind to the
    /// properties below, and the settings save themselves as they change.
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        readonly Config cfg = Config.Load();
        readonly Setup setup;
        readonly List<string> logLines = new List<string>();
        Process game;
        bool ready, busy, progressUnknown = true;
        double progressValue, progressMax = 1;
        string busyText = "", progressText = "";

        public event PropertyChangedEventHandler PropertyChanged;
        void N([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public GameSettings Settings { get; }
        public ObservableCollection<StatusItem> Status { get; } = new ObservableCollection<StatusItem>();

        public Option[] ModeOptions { get; } = { new Option(false, "gfx_mode_window"), new Option(true, "gfx_mode_fullscreen") };
        // Between left-to-right marks, or a right-to-left page shows 1440 × 2560: an LRM before the
        // first number makes every number after it left-to-right, and the × between two of them too.
        // WPF ignores the isolates (LRI, PDI) and the embeddings (LRE, PDF): both still showed 1440 × 2560.
        public Option[] SizeOptions { get; } = GameSettings.Sizes.Select(s => new Option(s, null, "‎" + s.Replace("x", " × ") + "‎")).ToArray();
        public Option[] AspectOptions { get; } = { new Option("keep", "gfx_aspect_keep"), new Option("stretch", "gfx_aspect_stretch") };
        public Option[] ScalerOptions { get; } =
        {
            new Option("bilinear", "gfx_scaler_bilinear"), new Option("sharp", "gfx_scaler_sharp"),
            new Option("bicubic", "gfx_scaler_bicubic"), new Option("nearest", "gfx_scaler_nearest"),
        };
        public Option[] ResOptions { get; } = new[] { new Option("auto", "gfx_res_auto") }
            .Concat(GameSettings.ResScales.Select(s => new Option(s, s == "1" ? "gfx_res_native" : null,
                    "‎" + s + "× · " + GameSettings.ResSize(s) + "‎"))).ToArray();
        public Option[] AnisoOptions { get; } =
        {
            new Option(0, "gfx_aniso_off"), new Option(2, "gfx_aniso_n", "2"), new Option(4, "gfx_aniso_n", "4"),
            new Option(8, "gfx_aniso_n", "8"), new Option(16, "gfx_aniso_n", "16"),
        };
        public Option[] GameLanguageOptions { get; } = GameSettings.GameLanguages.Select(l => new Option(l.Code, l.Key)).ToArray();
        public Option[] UiLanguageOptions { get; } = Loc.Languages.Select(l => new Option(l.Code, null, l.Native)).ToArray();

        public string UiLanguage
        {
            get => Loc.I.Code;
            set { Loc.I.Set(value); cfg.Set("uiLanguage", value); N(); }
        }

        public bool Busy { get => busy; set { busy = value; N(); N(nameof(NotBusy)); N(nameof(CanPlay)); } }
        public bool NotBusy => !busy;
        public string BusyText { get => busyText; set { busyText = value; N(); } }
        public double ProgressValue { get => progressValue; set { progressValue = value; N(); } }
        public double ProgressMax { get => progressMax; set { progressMax = value; N(); } }
        public bool ProgressUnknown { get => progressUnknown; set { progressUnknown = value; N(); } }
        public string ProgressText { get => progressText; set { progressText = value; N(); } }

        public bool ReadyToPlay => ready;
        public bool SetupNeeded => !ready;
        public bool Running => game != null && !game.HasExited;
        public bool CanPlay => ready && !Running && !busy;
        public bool WindowSizeEnabled => !Settings.Fullscreen;
        public string PlayLabel => Loc.I[Running ? "play_running" : "play_button"];
        public string StateTitle => Loc.I[ready ? "play_ready" : "play_notready"];
        public string StateDetail => ready ? Loc.I["play_ready_detail"] : Loc.I.F("play_notready_detail", Status.Count(s => !s.Ok));
        public string StateGlyph => ready ? "" : "";
        public Brush StateBrush => (Brush)FindResource(ready ? "Good" : "Warn");
        public string VersionText => Loc.I.F("support_version", typeof(MainWindow).Assembly.GetName().Version.ToString(3));

        public MainWindow()
        {
            Loc.I.Set(cfg.Str("uiLanguage", Loc.SystemDefault()));
            setup = new Setup(cfg, Log, Progress);
            Settings = GameSettings.Load(cfg);
            Settings.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(GameSettings.Fullscreen)) N(nameof(WindowSizeEnabled)); };
            Loc.I.PropertyChanged += (s, e) => { RefreshStatus(); N(nameof(PlayLabel)); N(nameof(VersionText)); N(nameof(UiLanguage)); };
            InitializeComponent();
            DataContext = this;
            Loaded += (s, e) => RefreshStatus();
        }

        // ---- window chrome ---------------------------------------------------

        void OnMinimize(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void OnMaximize(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void OnClose(object s, RoutedEventArgs e) => Close();
        // The hero card's circles reach past it; clip them to its rounded corners.
        void OnHeroSize(object s, SizeChangedEventArgs e) => Hero.Clip = new RectangleGeometry(new Rect(e.NewSize), 18, 18);

        // ---- log, progress, busy --------------------------------------------

        void Log(string line)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(Log), line); return; }
            logLines.Add(line);
            if (LogBox.Text.Length > 400000) LogBox.Text = LogBox.Text.Substring(200000);
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
            if (busy && line.Trim().Length > 0) BusyText = line.Trim();
        }

        void Progress(int done, int total)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<int, int>(Progress), done, total); return; }
            ProgressUnknown = total <= 0;
            if (total > 0) { ProgressMax = total; ProgressValue = Math.Min(done, total); ProgressText = $"{done} / {total}"; }
            else ProgressText = "";
        }

        async Task RunBusy(Func<Task> work)
        {
            Busy = true;
            BusyText = Loc.I["busy_working"];
            Progress(0, 0);
            try { await work(); }
            catch (Exception ex)
            {
                Log("Stopped: " + ex.Message);
                Dialog.Show(this, Loc.I["dlg_error"], ex.Message, false);
            }
            finally { Busy = false; RefreshStatus(); }
        }

        void RefreshStatus()
        {
            Status.Clear();
            foreach (var i in setup.Check()) Status.Add(i);
            ready = setup.ReadyToPlay;
            foreach (var p in new[] { nameof(ReadyToPlay), nameof(SetupNeeded), nameof(CanPlay), nameof(PlayLabel),
                                      nameof(StateTitle), nameof(StateDetail), nameof(StateGlyph), nameof(StateBrush) })
                N(p);
        }

        // ---- steps -----------------------------------------------------------

        void OnGoSetup(object s, RoutedEventArgs e) => NavSetup.IsChecked = true;

        async void OnSetUpEverything(object s, RoutedEventArgs e) => await RunBusy(async () =>
        {
            var tools = setup.MissingTools();
            var needElf = !File.Exists(Workspace.EbootElf);
            var plan = new List<string>();
            if (tools.Count > 0) plan.Add(Loc.I.F("plan_tools", string.Join(", ", tools.Select(t => Loc.I[t.Key]))));
            if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"))) plan.Add(Loc.I["plan_disc"]);
            if (needElf) plan.Add(Loc.I["plan_decrypt"]);
            if (setup.FfmpegBin() == null || setup.FfmpegInclude() == null) plan.Add(Loc.I["plan_ffmpeg"]);
            if (!File.Exists(Workspace.GameExe)) plan.Add(Loc.I.F("plan_build", Tools.BuildJobs()));
            if (plan.Count == 0) { Log(Loc.I["msg_allset"]); return; }
            if (!Dialog.Show(this, Loc.I["setup_all"], Loc.I["dlg_plan"] + "\n\n• " + string.Join("\n• ", plan) + "\n\n" + Loc.I["dlg_continue"], true))
                return;
            if (tools.Count > 0)
            {
                if (!AcceptMicrosoftLicense(tools)) { Log(Loc.I["msg_no_license"]); return; }
                await setup.InstallTools(tools, true);
            }
            if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME")) && !await ChooseIso() && !await ChooseFolder())
            { Log(Loc.I["msg_no_disc"]); return; }
            if (needElf)
            {
                if (setup.Rpcs3() == null && !ChooseRpcs3()) { Log(Loc.I["msg_no_rpcs3"]); return; }
                await setup.Decrypt();
            }
            if (setup.FfmpegBin() == null || setup.FfmpegInclude() == null) await setup.DownloadFfmpeg();
            if (!File.Exists(Workspace.GameExe)) await setup.Build();
            Log(Loc.I["msg_setup_done"]);
        });

        async void OnInstallTools(object s, RoutedEventArgs e) => await RunBusy(async () =>
        {
            var tools = setup.MissingTools();
            if (tools.Count == 0) return;
            var list = string.Join("\n• ", tools.Select(t => t.WingetId != null ? $"{Loc.I[t.Key]} (winget {t.WingetId})" : $"{Loc.I[t.Key]}: {Loc.I[t.DetailKey]}"));
            if (!Dialog.Show(this, Loc.I["setup_tools"], Loc.I["dlg_install"] + "\n\n• " + list, true)) return;
            if (!AcceptMicrosoftLicense(tools)) { Log(Loc.I["msg_no_license"]); return; }
            await setup.InstallTools(tools, true);
        });

        /// <summary>The CRT and SDK are Microsoft's; the player accepts their license, not the launcher.</summary>
        bool AcceptMicrosoftLicense(List<ToolPackage> tools) =>
            !tools.Any(t => t.Install != null) ||
            Dialog.Show(this, Loc.I["dlg_license_title"], Loc.I.F("dlg_license", Setup.MicrosoftLicenseUrl), true, yesNo: true);

        async void OnChooseIso(object s, RoutedEventArgs e) => await RunBusy(async () => await ChooseIso());
        async void OnChooseFolder(object s, RoutedEventArgs e) => await RunBusy(async () => await ChooseFolder());
        void OnChooseRpcs3(object s, RoutedEventArgs e) { ChooseRpcs3(); RefreshStatus(); }
        async void OnDecrypt(object s, RoutedEventArgs e) => await RunBusy(() => setup.Decrypt());
        async void OnDownloadFfmpeg(object s, RoutedEventArgs e) => await RunBusy(() => setup.DownloadFfmpeg());
        async void OnBuild(object s, RoutedEventArgs e) => await RunBusy(() => setup.Build());
        async void OnUpdate(object s, RoutedEventArgs e) => await RunBusy(() => Support.Update(setup, Log));

        async Task<bool> ChooseIso()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = Loc.I["dlg_iso"], Filter = "ISO (*.iso)|*.iso" };
            if (d.ShowDialog(this) != true) return false;
            await setup.FromIso(d.FileName);
            return true;
        }

        async Task<bool> ChooseFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.I["dlg_folder"] })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
                await setup.FromFolder(d.SelectedPath);
                return true;
            }
        }

        bool ChooseRpcs3()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = Loc.I["dlg_rpcs3"], Filter = "RPCS3 (rpcs3.exe)|rpcs3.exe" };
            if (d.ShowDialog(this) != true) return false;
            cfg.Set("rpcs3", d.FileName);
            Log("RPCS3: " + d.FileName);
            return true;
        }

        void OnChooseFfmpeg(object s, RoutedEventArgs e)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.I["dlg_ffmpeg"] })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                cfg.Set("ffmpeg", Path.Combine(d.SelectedPath, "bin"));
                cfg.Set("ffmpegInclude", Path.Combine(d.SelectedPath, "include"));
                if (setup.FfmpegBin() == null) Log(Loc.I["msg_not_ffmpeg8"]);
            }
            RefreshStatus();
        }

        void OnPreset(object s, RoutedEventArgs e) => Settings.Preset((string)((Button)s).Tag);

        void OnPlay(object s, RoutedEventArgs e)
        {
            if (!setup.ReadyToPlay) return;
            game = Game.Start(setup, Settings, Log, code => Dispatcher.BeginInvoke(new Action(() =>
            {
                Log(Loc.I.F("msg_closed", code));
                RefreshStatus();
            })));
            RefreshStatus();
        }

        void OnReport(object s, RoutedEventArgs e)
        {
            var zip = Support.CreateReport(setup, Settings, logLines);
            Log(Loc.I.F("msg_report", zip));
            Process.Start("explorer.exe", "/select," + Runner.Quote(zip));
            if (Support.IssuesUrl.Length > 0) Process.Start(Support.IssuesUrl);
        }

        void OnLogs(object s, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Workspace.Logs);
            Process.Start("explorer.exe", Workspace.Logs);
        }

        // ---- tests -------------------------------------------------------------

        /// <summary>Renders the window, on the given page, into a PNG (Launcher.exe --screenshot).</summary>
        public void SaveShot(int page, string file)
        {
            new[] { NavPlay, NavSetup, NavGraphics, NavSettings, NavSupport }[Math.Max(0, Math.Min(page, 4))].IsChecked = true;
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var bmp = new RenderTargetBitmap((int)(Root.ActualWidth * dpi.DpiScaleX), (int)(Root.ActualHeight * dpi.DpiScaleY),
                                             dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // WPF mirrors a right-to-left window once, at the window, and draws
            // everything inside it flipped for that; rendering an element inside
            // has to apply the window's flip itself.
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (FlowDirection == FlowDirection.RightToLeft)
                    dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, Root.ActualWidth, 0));
                dc.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            }
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
        }

        /// <summary>
        /// Text that does not fit, on every page in every language, at the
        /// window's default size and at its smallest (Launcher.exe --layout).
        /// </summary>
        public async Task<List<string>> CheckLayout()
        {
            var found = new List<string>();
            var pages = new[] { NavPlay, NavSetup, NavGraphics, NavSettings, NavSupport };
            var start = Loc.I.Code;
            foreach (var size in new[] { new Size(Width, Height), new Size(MinWidth, MinHeight) })
            {
                Width = size.Width;
                Height = size.Height;
                foreach (var (code, _) in Loc.Languages)
                {
                    Loc.I.Set(code);
                    for (int p = 0; p < pages.Length; p++)
                    {
                        pages[p].IsChecked = true;
                        await Task.Delay(250);   // the bindings settle
                        UpdateLayout();
                        found.AddRange(Clipped().Select(c => $"{size.Width}x{size.Height} {code} page {p}: {c}"));
                    }
                }
            }
            Loc.I.Set(start);
            return found;
        }

        static IEnumerable<DependencyObject> Tree(DependencyObject d)
        {
            yield return d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                foreach (var c in Tree(VisualTreeHelper.GetChild(d, i))) yield return c;
        }

        /// <summary>
        /// Each visible TextBlock whose text is wider (or, wrapped, taller) than
        /// its box, or that a parent cuts sideways. Cuts from above and below are
        /// left out: the pages scroll.
        /// </summary>
        IEnumerable<string> Clipped()
        {
            foreach (var t in Tree(Root).OfType<TextBlock>())
            {
                if (!t.IsVisible || string.IsNullOrWhiteSpace(t.Text) || t.ActualWidth < 1) continue;
                var ft = new FormattedText(t.Text, System.Globalization.CultureInfo.CurrentCulture, t.FlowDirection,
                                           new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch),
                                           t.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(t).PixelsPerDip);
                // FormattedText and the TextBlock round apart by a pixel or two.
                const double slack = 2;
                var w = t.ActualWidth - t.Padding.Left - t.Padding.Right;
                var need = ft.WidthIncludingTrailingWhitespace;
                if (t.TextWrapping == TextWrapping.NoWrap)
                {
                    if (need > w + slack) { yield return $"\"{t.Text}\" needs {need:0} px, has {w:0}"; continue; }
                }
                else
                {
                    ft.MaxTextWidth = Math.Max(1, w + slack);
                    need = ft.Width;
                    var h = t.ActualHeight - t.Padding.Top - t.Padding.Bottom;
                    if (ft.Height > h + slack) { yield return $"\"{t.Text}\" needs {ft.Height:0} px high, has {h:0}"; continue; }
                }
                // Where the text itself sits in the box, by its alignment.
                var tw = Math.Min(need, w);
                var x = t.TextAlignment == TextAlignment.Right ? t.ActualWidth - t.Padding.Right - tw
                      : t.TextAlignment == TextAlignment.Center ? (t.ActualWidth - tw) / 2 : t.Padding.Left;
                for (var a = VisualTreeHelper.GetParent(t); a is FrameworkElement fe && a != Root; a = VisualTreeHelper.GetParent(a))
                {
                    var clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe)?.Bounds;
                    if (clip == null || clip.Value.IsEmpty) continue;
                    var box = t.TransformToAncestor(fe).TransformBounds(new Rect(x, 0, tw, t.ActualHeight));
                    var shown = Math.Max(0, Math.Min(box.Right, clip.Value.Right) - Math.Max(box.Left, clip.Value.Left));
                    if (shown < box.Width - slack)
                    {
                        yield return $"\"{t.Text}\" cut by its {fe.GetType().Name}: {shown:0} of {box.Width:0} px shown";
                        break;
                    }
                }
            }
        }
    }

    /// <summary>A message in the launcher's style, with buttons in the current language.</summary>
    static class Dialog
    {
        public static bool Show(Window owner, string title, string text, bool ask, bool yesNo = false)
        {
            var w = new Window
            {
                Owner = owner, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = (Brush)owner.FindResource("Card"),
                BorderBrush = (Brush)owner.FindResource("Line"), BorderThickness = new Thickness(1), FlowDirection = Loc.I.Flow,
                FontFamily = (FontFamily)owner.FindResource("UiFont"),
            };
            bool result = false;
            var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22), MaxWidth = 520 };
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)owner.FindResource("H2") });
            panel.Children.Add(new TextBlock { Text = text, Style = (Style)owner.FindResource("Sub"), FontSize = 13, Margin = new Thickness(0, 0, 0, 22) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (ask)
            {
                var no = new Button { Content = Loc.I[yesNo ? "common_no" : "common_cancel"], MinWidth = 96, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
                no.Click += (s, e) => w.Close();
                buttons.Children.Add(no);
            }
            var ok = new Button { Content = Loc.I[ask && yesNo ? "common_yes" : "common_ok"], MinWidth = 96, IsDefault = true,
                                  Style = (Style)owner.FindResource("PrimaryButton") };
            ok.Click += (s, e) => { result = true; w.Close(); };
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            w.Content = panel;
            w.MouseLeftButtonDown += (s, e) => { try { w.DragMove(); } catch (InvalidOperationException) { } };
            w.ShowDialog();
            return result;
        }
    }
}
