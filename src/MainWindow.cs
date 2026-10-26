using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Veylix
{
    public partial class MainWindow : Window
    {
        const double DW = 1180, DH = 740;

        Engine engine;
        Settings cfg;
        readonly LogStore log = new LogStore();
        List<Strategy> strategies;
        Strategy selected;

        bool busy, tuning, checking, diagRunning, updating, reallyExit, autorunOn, autorunBusy, trayHintShown;
        readonly bool autoMode, selfTest;
        CancellationTokenSource tuneCts;
        ServiceResult[] lastChecks;
        List<DiagItem> lastDiag;
        ReleaseInfo latest;
        string paletteId;
        bool sourceReady;

        Grid design;
        DispatcherTimer timer;
        WF.NotifyIcon tray;

        public MainWindow(bool auto, bool selfTest)
        {
            autoMode = auto;
            this.selfTest = selfTest;
            Title = "Veylix";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Ui;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // Без AllowsTransparency: окно рисуется аппаратно, а скругление и тень даёт Windows (DWM)
            WindowChrome wc = new WindowChrome();
            wc.CaptionHeight = 0;
            wc.ResizeBorderThickness = new Thickness(0);
            wc.GlassFrameThickness = new Thickness(-1);
            wc.CornerRadius = new CornerRadius(0);
            wc.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, wc);

            Rect wa = SystemParameters.WorkArea;
            double k = Math.Min(1.0, Math.Min((wa.Width - 24) / DW, (wa.Height - 24) / DH));
            Width = DW * k;
            Height = DH * k;

            cfg = Settings.Load();
            Theme.Animations = cfg.Animations && SystemParameters.ClientAreaAnimation && !selfTest;
            Theme.Options = cfg.Studio;
            ApplyTheme();

            engine = new Engine(Engine.Locate());   // ошибки обрабатывает Program
            strategies = engine.LoadStrategies();
            if (strategies.Count == 0)
                throw new InvalidOperationException("Не найдено ни одного файла general*.bat в " + engine.Root);
            selected = strategies[0];
            foreach (Strategy st in strategies)
                if (st.Display == cfg.LastStrategy) selected = st;
            // предпочтительная стратегия — осознанный выбор пользователя, она важнее последней нажатой
            foreach (Strategy st in strategies)
                if (cfg.PreferredStrategy.Length > 0 && st.Display == cfg.PreferredStrategy) selected = st;

            BuildCommands();
            InitPulse();
            PreviewKeyDown += OnGlobalKey;

            // в режиме самопроверки окно не показывается, но дерево остаётся в нём:
            // WPF рассылает изменения ресурсов темы только по деревьям окон
            design = BuildRoot();
            Viewbox vb = new Viewbox();
            vb.Stretch = Stretch.Uniform;
            vb.Child = design;
            Content = vb;

            engine.Log += delegate(string level, string text) { log.Add(level, text); };
            log.Added += delegate(LogEntry e) { Ui_(delegate { OnLogAdded(e); }); };
            log.Cleared += delegate { Ui_(RefreshJournal); };
            log.Added += delegate(LogEntry e)
            {
                if (e.Level != "err") return;
                Ui_(delegate
                {
                    session.Errors++;
                    string t = e.Text.Length > 80 ? e.Text.Substring(0, 80) + "…" : e.Text;
                    events.Add("error", "err", t, e.Text, "journal");
                });
            };
            engine.Exited += delegate(int code)
            {
                Ui_(delegate
                {
                    lastExitAt = DateTime.Now;
                    lastExitCode = code;
                    log.Add("err", "winws.exe неожиданно завершился" + (code != -1 ? " (код " + code + ")" : "") +
                        ". Частые причины: антивирус, другой обход/VPN, отсутствие прав администратора.");
                    ResetChecks();
                    RefreshUi();
                    Toast("engine-exit", "err", "Обход остановился", "winws.exe завершился сам. Подробности — в журнале.", "journal", true);
                    if (!IsVisible) TrayBalloon("Обход остановился", "winws.exe завершился. Откройте Veylix, чтобы узнать подробности.", WF.ToolTipIcon.Warning);
                });
            };

            if (selfTest) return;

            BuildTray();
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            int ticks = 0;
            timer.Tick += delegate
            {
                UpdateUptime();
                UpdatePulseCountdown();
                RefreshActivity();
                if (page == "events") RefreshStats();
                UpdateCompanion();
                if (++ticks % 15 == 0) RefreshMap();
                if (ticks % 2 == 0) CheckGames();
                if (ticks % 30 == 0) CheckThemeSchedule();
            };
            timer.Start();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Application.Current.SessionEnding += delegate { try { engine.Shutdown(); } catch { } };
            Application.Current.Exit += delegate
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                try { engine.Shutdown(); } catch { }
                try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            };

            SourceInitialized += delegate
            {
                sourceReady = true;
                ApplyTheme();
            };
            Loaded += delegate { OnFirstShow(); };
        }

        // Выполнить на UI-потоке
        void Ui_(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        // ====================== тема ======================

        void ApplyTheme()
        {
            Palette p = Theme.Resolve(cfg.Theme, cfg.ThemeMode, Theme.SystemPrefersLight(), DateTime.Now);
            bool wantBlur = p.Glass && !selfTest && Backdrop.BlurUnavailableReason(cfg.GlassReduced) == null;
            bool blurOn = false;
            if (sourceReady) blurOn = Backdrop.Apply(this, wantBlur, p.Dark);
            Theme.Apply(p, blurOn);
            if (blurOn) Background = Brushes.Transparent;
            else SetResourceReference(BackgroundProperty, "WindowBg");
            paletteId = p.Id + (blurOn ? "+blur" : "");
            UpdateBlobs();
            UpdateScenes();
            if (netMap != null) netMap.Restyle();
            RefreshFlyout();
            RefreshAppearanceTexts();
        }

        void CheckThemeSchedule()
        {
            Palette p = Theme.Resolve(cfg.Theme, cfg.ThemeMode, Theme.SystemPrefersLight(), DateTime.Now);
            string cur = paletteId ?? "";
            if (!cur.StartsWith(p.Id + "+") && cur != p.Id) ApplyTheme();
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle)
                Ui_(ApplyTheme);   // системная тема или прозрачность могли измениться — сохранённые настройки не трогаем
        }

        void SaveSettings()
        {
            string err = cfg.Save();
            if (err != null) log.Add("err", "Не удалось сохранить настройки: " + err);
        }

        // ====================== трей ======================

        void BuildTray()
        {
            tray = new WF.NotifyIcon();
            try { tray.Icon = SD.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { tray.Icon = SD.SystemIcons.Application; }
            tray.Text = "Veylix";
            // вместо стандартного меню — своя панель в стиле приложения (TrayFlyout.cs)
            tray.MouseClick += delegate(object s, WF.MouseEventArgs e)
            {
                if (e.Button == WF.MouseButtons.Right) ToggleFlyout();                    // ПКМ — панель трея
                else if (e.Button == WF.MouseButtons.Left) { CloseFlyout(); ShowFromTray(); }   // ЛКМ — окно Veylix
            };
            tray.Visible = true;
        }

        void TrayBalloon(string title, string text, WF.ToolTipIcon icon)
        {
            try { if (tray != null) tray.ShowBalloonTip(3000, title, text, icon); } catch { }
        }

        public void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (started) MaybeShowWhatsNew();
        }

        void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                TrayBalloon("Veylix", "Работает в трее. Щелчок по значку — открыть окно, правый щелчок — панель.", WF.ToolTipIcon.Info);
            }
        }

        void OnCloseClick()
        {
            if (engine.IsRunning && cfg.CloseToTray && !tuning) HideToTray();
            else ExitApp();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!reallyExit)
            {
                e.Cancel = true;
                OnCloseClick();
                return;
            }
            base.OnClosing(e);
        }

        // Выход без зависания окна: остановка обхода (до ~20 с на выгрузку драйвера) идёт в фоне
        async void ExitApp()
        {
            if (reallyExit) return;
            reallyExit = true;
            if (tuneCts != null) tuneCts.Cancel();
            pulseMon.Stop();
            try { Hide(); } catch { }
            try { if (tray != null) tray.Visible = false; } catch { }
            Task stop = Task.Run(delegate { try { engine.Shutdown(); } catch { } });
            await Task.WhenAny(stop, Task.Delay(30000));
            Application.Current.Shutdown();
        }

        // ====================== запуск ======================

        // Автозапуск с Windows: окно не показываем, сразу работаем в трее
        public void StartInBackground()
        {
            OnFirstShow();
        }

        bool started;
        bool autoStartPending;   // автозапуск ещё пытается включить обход; ручное нажатие кнопки его отменяет

        // Сразу после входа в Windows сеть, служба BFE и драйвер WinDivert бывают ещё не готовы,
        // и winws.exe завершается. Поэтому ждём сеть и повторяем запуск несколько раз.
        async Task AutoStartBypass()
        {
            Strategy s = selected;
            if (s == null) { log.Add("err", "Автозапуск: стратегия не выбрана — обход не включён."); return; }
            autoStartPending = true;
            try
            {
                if (!NetworkUp())
                {
                    log.Add("info", "Автозапуск: жду подключения к сети…");
                    DateTime until = DateTime.Now.AddSeconds(90);
                    while (!NetworkUp() && DateTime.Now < until && autoStartPending && !reallyExit) await Task.Delay(2000);
                }
                int[] pauses = { 0, 5, 15, 30 };
                for (int i = 0; i < pauses.Length; i++)
                {
                    if (pauses[i] > 0)
                    {
                        log.Add("warn", "Автозапуск: обход не включился, повтор через " + pauses[i] + " с (попытка " + (i + 1) + " из " + pauses.Length + ").");
                        await Task.Delay(pauses[i] * 1000);
                    }
                    while ((busy || tuning || updating) && autoStartPending && !reallyExit) await Task.Delay(1000);
                    if (!autoStartPending || reallyExit || engine.IsRunning || gamePausedFor != null) return;
                    s = selected ?? s;   // стратегию могли сменить, пока ждали
                    log.Add("info", "Автозапуск: включаю последнюю стратегию " + s.Display + ".");
                    if (await StartAsync(s)) return;
                }
                log.Add("err", "Автозапуск: обход так и не включился. Включите его вручную или выберите другую стратегию.");
                Toast("autostart-err", "err", "Обход не включился при автозапуске", "Veylix запущен, но winws.exe не стартовал. Подробности — в журнале.", "journal", true);
                if (!IsVisible) TrayBalloon("Обход не включился", "Veylix запущен, но обход не стартовал. Откройте Veylix, чтобы включить его.", WF.ToolTipIcon.Warning);
            }
            finally { autoStartPending = false; }
        }

        static bool NetworkUp()
        {
            try { return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
            catch { return true; }
        }

        async void OnFirstShow()
        {
            if (started) return;
            started = true;
            try
            {
                log.Add("info", "Veylix " + Program.Version + " · Flowseal " + (engine.InstalledVersion() ?? "?") + " · файлы: " + engine.Root);
                if (autoMode) log.Add("info", "Запуск при входе в Windows (автозапуск).");
                if (Settings.MigrationNote != null) log.Add("info", Settings.MigrationNote);
                HandlePostUpdate();
                MaybeShowWhatsNew();
                if (!Diagnostics.IsAdmin()) log.Add("err", "Нет прав администратора — winws.exe не сможет загрузить драйвер WinDivert.");
                List<int> ext = engine.ExternalPids();
                if (ext.Count > 0)
                    log.Add("warn", "Уже запущен другой winws.exe (PID " + string.Join(", ", ext) + ") — из .bat или службы zapret. При запуске обхода он будет остановлен.");
                string proxy = NetCheck.SystemProxyDescription();
                if (proxy != null) log.Add("warn", "В Windows включён системный " + proxy + ". Veylix его не меняет; если сайты не открываются в браузере — проверьте прокси.");
                RefreshUi();

                autorunBusy = true;
                RefreshSettingsState();
                autorunOn = await Task.Run(() => Engine.IsAutorun());
                if (autorunOn && !selfTest)
                {
                    string exePath = Assembly.GetExecutingAssembly().Location;
                    string fix = await Task.Run(() => Engine.RepairAutorun(exePath));
                    if (fix == "ok") log.Add("info", "Автозапуск: задание в Планировщике обновлено (новый путь к Veylix или настройки прежней версии).");
                    else if (fix != null) log.Add("err", "Автозапуск: не удалось обновить задание — " + fix + ". Выключите и снова включите автозапуск в настройках.");
                }
                autorunBusy = false;
                RefreshSettingsState();

                if (autoMode && !DeferStartForGame(selected)) await AutoStartBypass();
                if (engine.CheckUpdatesEnabled) await CheckUpdates(false);
                if (cfg.AppUpdateCheck && Program.PostUpdatePlan == null) CheckAppUpdate(false);
            }
            catch (Exception ex)
            {
                log.Add("err", ex.Message);
            }
        }

        // ====================== состояние ======================

        void Select(Strategy s)
        {
            selected = s;
            if (cfg.LastStrategy != s.Display)
            {
                cfg.LastStrategy = s.Display;
                SaveSettings();
            }
            RefreshStrategyCards();
        }

        static string IpsetLabel(string m)
        {
            if (m == "none") return "Выкл";
            if (m == "loaded") return "Список";
            return "Все IP";
        }

        static string GameLabel(string m)
        {
            if (m == "all") return "TCP+UDP";
            if (m == "tcp") return "TCP";
            if (m == "udp") return "UDP";
            return "Выкл";
        }

        void RefreshUi()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshUi)); return; }
            bool run = engine.IsRunning;
            Strategy cur = engine.Current;
            string name = cur != null ? cur.Display : (selected != null ? selected.Display : "—");

            string state;
            if (tuning) state = "tune";
            else if (busy) state = "busy";
            else if (run) state = "run";
            else state = "off";
            ApplyPowerState(state, name);
            ApplyGamePauseState();
            RefreshGameStatus();

            tileStrat.Text = selected != null ? selected.Display : "—";
            tileIpset.Text = IpsetLabel(engine.GetIpsetMode());
            tileGame.Text = GameLabel(engine.GameMode);
            powerBtn.IsEnabled = !tuning && !updating;

            RefreshStrategyCards();
            if (tray != null)
            {
                try
                {
                    string tt = run ? "Veylix — работает: " + name : gamePausedFor != null ? "Veylix — пауза для игры: " + GameWatch.Pretty(gamePausedFor) : "Veylix — обход выключен";
                    tray.Text = tt.Length > 63 ? tt.Substring(0, 63) : tt;
                }
                catch { }
            }
            UpdateUptime();
            RefreshFlyout();
            TrackEngine();
            RefreshMap();
            RefreshActivity();
            RefreshLab();
            UpdateCompanion();
            if (page == "assistant" && !selfTest) { Analyze(); }
        }

        void UpdateUptime()
        {
            if (uptimeText == null) return;
            if (engine != null && engine.IsRunning)
            {
                TimeSpan t = DateTime.Now - engine.StartedAt;
                uptimeText.Text = "в работе " + string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
            }
            else uptimeText.Text = "";
        }

        void OpenPath(string exe, string arg)
        {
            try { Process.Start(exe, "\"" + arg + "\""); }
            catch (Exception ex) { log.Add("err", "Не удалось открыть " + arg + ": " + ex.Message); }
        }

        void OpenUrl(string url)
        {
            // открываем только https-адреса официального репозитория
            if (!url.StartsWith(Updater.RepoUrl, StringComparison.OrdinalIgnoreCase)) url = Updater.ReleasesUrl;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { log.Add("err", "Не удалось открыть браузер: " + ex.Message); }
        }

        // ====================== действия: обход ======================

        async void OnPower()
        {
            try
            {
                if (busy || tuning || updating) return;
                autoStartPending = false;
                if (engine.IsRunning) await StopAsync();
                else
                {
                    ClearGamePauseByUser();
                    await StartAsync(selected);
                }
            }
            catch (Exception ex)
            {
                busy = false;
                log.Add("err", ex.Message);
                RefreshUi();
            }
        }

        async Task<bool> StartAsync(Strategy s)
        {
            busy = true;
            RefreshUi();
            log.Add("info", "Запускаю стратегию " + s.Display + "…");
            bool ok = await Task.Run(() => engine.Start(s));
            if (!ok) lastStartFailAt = DateTime.Now;
            if (ok)
            {
                await Task.Delay(1500);
                if (!engine.IsRunning)
                {
                    ok = false;
                    lastStartFailAt = DateTime.Now;
                    log.Add("err", "winws.exe сразу завершился. Проверьте: запуск от администратора, нет ли другого обхода/VPN, не удалил ли антивирус файлы.");
                }
            }
            busy = false;
            if (ok)
            {
                log.Add("ok", "Обход работает: " + s.Display);
                lastStartFailAt = DateTime.MinValue;
                lastExitAt = DateTime.MinValue;
            }
            RefreshUi();
            if (ok) RunAccessTest();
            return ok;
        }

        async Task StopAsync()
        {
            busy = true;
            RefreshUi();
            log.Add("info", "Останавливаю обход и выгружаю драйвер WinDivert…");
            await Task.Run(() => engine.Stop());
            busy = false;
            log.Add("info", "Обход остановлен.");
            ResetChecks();
            RefreshUi();
        }

        async void OnPickStrategy(Strategy s)
        {
            try
            {
                if (busy || tuning || updating) return;
                Select(s);
                if (engine.IsRunning) await StartAsync(s);
                else RefreshUi();
            }
            catch (Exception ex)
            {
                busy = false;
                log.Add("err", ex.Message);
                RefreshUi();
            }
        }

        void OnNextStrategy()
        {
            if (busy || tuning || updating) return;
            int i = strategies.IndexOf(selected);
            OnPickStrategy(strategies[(i + 1) % strategies.Count]);
        }

        async Task RestartIfRunning()
        {
            if (!engine.IsRunning) return;
            Strategy cur = engine.Current ?? selected;
            log.Add("info", "Перезапускаю обход, чтобы применить изменение…");
            await StartAsync(cur);
        }

        async void OnIpset()
        {
            try
            {
                if (busy || tuning || updating) return;
                string err = engine.CycleIpset();
                if (err != null) { log.Add("err", err); return; }
                log.Add("info", "IPSet: " + IpsetLabel(engine.GetIpsetMode()));
                RefreshUi();
                await RestartIfRunning();
            }
            catch (Exception ex) { busy = false; log.Add("err", ex.Message); RefreshUi(); }
        }

        async void OnGame()
        {
            try
            {
                if (busy || tuning || updating) return;
                engine.SetGameMode(engine.NextGameMode());
                log.Add("info", "Игровой фильтр: " + GameLabel(engine.GameMode));
                RefreshUi();
                await RestartIfRunning();
            }
            catch (Exception ex) { busy = false; log.Add("err", ex.Message); RefreshUi(); }
        }

        // Выбор режима из плашки на главной: сразу нужный режим, без перебора по кругу
        async void OnSetIpset(string mode)
        {
            try
            {
                if (busy || tuning || updating || mode == engine.GetIpsetMode()) return;
                string err = engine.SetIpsetMode(mode);
                if (err != null) { log.Add("err", err); return; }
                log.Add("info", "IPSet: " + IpsetLabel(engine.GetIpsetMode()));
                RefreshUi();
                await RestartIfRunning();
            }
            catch (Exception ex) { busy = false; log.Add("err", ex.Message); RefreshUi(); }
        }

        async void OnSetGame(string mode)
        {
            try
            {
                if (busy || tuning || updating || mode == engine.GameMode) return;
                engine.SetGameMode(mode);
                log.Add("info", "Игровой фильтр: " + GameLabel(engine.GameMode));
                RefreshUi();
                await RestartIfRunning();
            }
            catch (Exception ex) { busy = false; log.Add("err", ex.Message); RefreshUi(); }
        }

        async void OnUpdateIpset()
        {
            try
            {
                if (busy || tuning || updating) return;
                busy = true;
                RefreshUi();
                log.Add("info", "Скачиваю свежий список IPSet с GitHub Flowseal…");
                string err = await Task.Run(() => engine.UpdateIpset());
                busy = false;
                if (err != null)
                {
                    log.Add("err", "Не удалось обновить IPSet: " + err);
                    RefreshUi();
                    return;
                }
                log.Add("ok", "IPSet обновлён (режим «Список»).");
                RefreshUi();
                await RestartIfRunning();
            }
            catch (Exception ex) { busy = false; log.Add("err", ex.Message); RefreshUi(); }
        }

        void OnOpenUserList(string fileName)
        {
            try
            {
                engine.EnsureUserLists();
                OpenPath("notepad.exe", Path.Combine(engine.Lists, fileName));
                log.Add("info", "После сохранения " + fileName + " перезапустите обход (кнопка питания), чтобы изменения вступили в силу.");
            }
            catch (Exception ex) { log.Add("err", ex.Message); }
        }

        // ====================== проверка доступности ======================

        async void RunAccessTest()
        {
            if (checking) return;
            checking = true;
            try
            {
                checkBtn.IsEnabled = false;
                foreach (CheckRow r in checkRows) r.Set("busy", "Проверяю…", "проверка");
                checkWhen.Text = "Проверка: DNS → TCP → TLS → HTTP…";
                if (engine.IsRunning) await Task.Delay(800);   // даём winws подхватить фильтры
                Strategy during = engine.Current;
                ServiceResult[] r2 = await NetCheck.CheckAll();
                lastChecks = r2;
                ShowChecks(r2);
                ApplyCheckResults(r2, "check");
                string line = "Проверка: интернет — " + NetCheck.StatusWord(r2[0].Status) +
                              ", Discord — " + NetCheck.StatusWord(r2[1].Status) +
                              ", YouTube — " + NetCheck.StatusWord(r2[2].Status);
                bool good = r2[1].Status == ServiceStatus.Ok && r2[2].Status == ServiceStatus.Ok;
                log.Add(good ? "ok" : (r2[0].Status == ServiceStatus.Unavailable ? "err" : "warn"), line);
                events.Add("check", good ? "ok" : "warn", "Проверка сервисов завершена", line.Replace("Проверка: ", ""), "diag");
                foreach (ServiceResult s in r2)
                    if (s.Status != ServiceStatus.Ok) log.Add("info", s.Name + ": " + s.Summary);
                if (during != null && object.ReferenceEquals(during, engine.Current))
                {
                    RecordResult(during, r2[1], r2[2]);
                }
                bool dnsBlock = false;
                foreach (ServiceResult s in r2)
                    foreach (ProbeResult p in s.Probes)
                        if (p.Stage == "DNS" && p.Status == ProbeStatus.Blocked) dnsBlock = true;
                if (dnsBlock)
                    log.Add("info", "Похоже на DNS-блокировку: провайдер скрывает адрес сайта, а обход это не лечит. Включите защищённый DNS в разделе «Сайты и DNS».");
                if (!good && engine.IsRunning && r2[0].Status != ServiceStatus.NoNetwork && r2[0].Status != ServiceStatus.Unavailable)
                    log.Add("info", "Если что-то не открывается — выберите другую стратегию или запустите автоподбор.");
                if (diagPage != null && lastDiag != null) RenderDiagnostics();
            }
            catch (Exception ex)
            {
                log.Add("err", "Проверка не выполнена: " + ex.Message);
                checkWhen.Text = "Проверка не выполнена";
            }
            finally
            {
                checking = false;
                checkBtn.IsEnabled = true;
            }
        }

        static string ShortResult(ServiceResult d, ServiceResult y)
        {
            return "Discord " + d.Passed + "/" + d.Total + " · YouTube " + y.Passed + "/" + y.Total;
        }

        void Remember(Strategy s, int score, int max, string summary)
        {
            s.LastScore = score;
            s.LastMax = max;
            s.LastSummary = summary;
            s.LastTested = DateTime.Now;
            RefreshStrategyCards();
        }

        void ResetChecks()
        {
            if (checkRows == null) return;
            foreach (CheckRow r in checkRows) r.Set("idle", "Нажмите «Проверить»", "не проверено");
            checkWhen.Text = "Запускается сама после включения обхода";
        }

        // ====================== автоподбор ======================

        async void OnTune()
        {
            if (tuning)
            {
                if (tuneCts != null) tuneCts.Cancel();
                Ui.SetButtonText(tuneBtn, "Останавливаю…");
                return;
            }
            if (busy || updating) return;

            tuning = true;
            tuneCts = new CancellationTokenSource();
            CancellationToken ct = tuneCts.Token;
            Ui.SetButtonText(tuneBtn, "Остановить подбор");
            tuneExplain.Text = "";
            SetTuneProgress(0, "Проверяю подключение к интернету…");
            ResetChecks();
            RefreshUi();

            Strategy before = engine.IsRunning ? engine.Current : null;
            Strategy prevSelected = selected;
            Strategy winner = null;
            string explanation = "";
            int oldTimeout = NetCheck.StageTimeoutMs;
            NetCheck.StageTimeoutMs = 5000;
            try
            {
                // 1. Без интернета подбирать бессмысленно — иначе все стратегии «не работают»
                ServiceResult inet = await NetCheck.CheckService(NetCheck.Internet);
                if (inet.Status != ServiceStatus.Ok && inet.Status != ServiceStatus.Partial)
                    throw new TuneAbort("Подбор не запускался: нет доступа даже к нейтральным сайтам (" + inet.Summary + "). Проверьте подключение к интернету.");

                // 2. Порядок: текущая/выбранная стратегия первой (не меняем то, что уже работает),
                //    затем остальные по списку; помеченные «не рекомендуется» — в конце
                List<Strategy> order = new List<Strategy>();
                Strategy first = before ?? selected;
                if (first != null) order.Add(first);
                Strategy prefS = strategies.Find(delegate(Strategy x) { return x.Display == cfg.PreferredStrategy; });
                if (prefS != null && !order.Contains(prefS)) order.Add(prefS);
                foreach (Strategy s in strategies) if (!s.Warn && !order.Contains(s)) order.Add(s);
                foreach (Strategy s in strategies) if (s.Warn && !order.Contains(s)) order.Add(s);

                log.Add("info", "Автоподбор: " + order.Count + " стратегий, проверка Discord и YouTube (" +
                    (NetCheck.Discord.Targets.Length + NetCheck.YouTube.Targets.Length) + " адресов) с повтором неудачных попыток.");

                Strategy best = null;
                int bestScore = -1, maxScore = 0, tested = 0;
                string bestSummary = "";
                for (int i = 0; i < order.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    Strategy s = order[i];
                    SetTuneProgress((double)i / order.Count, "Проверяю " + s.Display + " (" + (i + 1) + " из " + order.Count + ")…");
                    selected = s;
                    RefreshStrategyCards();

                    bool ok = await Task.Run(() => engine.Start(s));
                    if (ok) { await Task.Delay(2500); ok = engine.IsRunning; }
                    if (ct.IsCancellationRequested) break;
                    tested++;
                    if (!ok)
                    {
                        log.Add("warn", s.Display + ": winws.exe не запустился, пропускаю.");
                        Remember(s, 0, 1, "не запустилась");
                        continue;
                    }

                    Task<ServiceResult> td = NetCheck.CheckService(NetCheck.Discord);
                    Task<ServiceResult> ty = NetCheck.CheckService(NetCheck.YouTube);
                    await Task.WhenAll(td, ty);
                    ServiceResult d = td.Result, y = ty.Result;
                    int score = d.Score + y.Score;
                    maxScore = d.MaxScore + y.MaxScore;
                    string summary = ShortResult(d, y);
                    session.CountServices(new ServiceResult[] { d, y });
                    session.StrategiesTested++;
                    RecordResult(s, d, y);
                    log.Add(score == maxScore ? "ok" : (score == 0 ? "warn" : "info"), s.Display + ": " + summary + " (оценка " + score + "/" + maxScore + ")");

                    if (score > bestScore) { best = s; bestScore = score; bestSummary = summary; }

                    if (score == maxScore)
                    {
                        // 3. Контрольная проверка, чтобы не выбрать стратегию из-за случайной удачи
                        SetTuneProgress((double)(i + 1) / order.Count, "Контрольная проверка " + s.Display + "…");
                        ServiceResult d2 = await NetCheck.CheckService(NetCheck.Discord);
                        ServiceResult y2 = await NetCheck.CheckService(NetCheck.YouTube);
                        int score2 = d2.Score + y2.Score;
                        if (score2 == maxScore)
                        {
                            winner = s;
                            break;
                        }
                        log.Add("warn", s.Display + ": контрольная проверка хуже (" + ShortResult(d2, y2) + ") — результат нестабилен, продолжаю.");
                        Remember(s, Math.Min(score, score2), maxScore, ShortResult(d2, y2) + " · нестабильно");
                        if (best == s) bestScore = Math.Min(score, score2);
                    }
                }

                if (winner != null)
                {
                    explanation = object.ReferenceEquals(winner, first) && before != null
                        ? "Текущая стратегия " + winner.Display + " прошла все проверки дважды — переключение не требуется."
                        : "Выбрана " + winner.Display + ": все адреса Discord и YouTube ответили, контрольная проверка подтвердила результат. Проверено стратегий: " + tested + ".";
                }
                else if (ct.IsCancellationRequested)
                {
                    explanation = "Подбор остановлен пользователем после " + tested + " стратегий.";
                    winner = null;
                }
                else if (best != null && bestScore > 0)
                {
                    // Идеальной нет — оставляем лучшую, но не меняем работающую стратегию на такую же по качеству
                    Strategy keep = best;
                    if (before != null && before.LastScore >= bestScore) keep = before;
                    winner = keep;
                    explanation = "Ни одна стратегия не прошла все проверки. Оставлена " + keep.Display + " как лучшая: " +
                        (keep.LastSummary ?? bestSummary) + ". Попробуйте режим IPSet «Список» или обновить Flowseal.";
                }
                else
                {
                    lastTuneNoWinner = true;
                    explanation = "Ни одна стратегия не открыла Discord и YouTube. Интернет при этом работает — возможно, мешает VPN/антивирус или нужна более новая версия Flowseal (см. «Диагностика»).";
                }
            }
            catch (TuneAbort ex)
            {
                explanation = ex.Message;
            }
            catch (Exception ex)
            {
                explanation = "Автоподбор прерван ошибкой: " + ex.Message;
            }
            finally
            {
                NetCheck.StageTimeoutMs = oldTimeout;
            }

            try
            {
                if (reallyExit) return;
                if (winner != null)
                {
                    if (!object.ReferenceEquals(engine.Current, winner) || !engine.IsRunning)
                    {
                        SetTuneProgress(1, "Запускаю " + winner.Display + "…");
                        await Task.Run(() => engine.Start(winner));
                    }
                    Select(winner);
                    log.Add("ok", explanation);
                }
                else if (before != null)
                {
                    // ничего лучше не нашли — возвращаем то, что работало до подбора
                    if (!object.ReferenceEquals(engine.Current, before) || !engine.IsRunning)
                    {
                        await Task.Run(() => engine.Start(before));
                        explanation += " Возвращена прежняя стратегия " + before.Display + ".";
                    }
                    Select(before);
                    log.Add(ct.IsCancellationRequested ? "info" : "warn", explanation);
                }
                else
                {
                    selected = prevSelected;
                    await Task.Run(() => engine.Stop());
                    log.Add(ct.IsCancellationRequested ? "info" : "warn", explanation);
                }
            }
            catch (Exception ex) { log.Add("err", ex.Message); }
            finally
            {
                tuning = false;
                Ui.SetButtonText(tuneBtn, "Запустить автоподбор");
                SetTuneProgress(winner != null ? 1 : 0, winner != null ? "Готово: " + winner.Display : "Готово");
                tuneExplain.Text = explanation;
                lastTuneTime = DateTime.Now;
                if (winner != null) lastTuneNoWinner = false;
                events.Add("strategy", winner != null ? "ok" : "warn", winner != null ? "Автоподбор: " + winner.Display : "Автоподбор завершён без победителя", explanation, "strategies");
                tileTune.Text = winner != null ? winner.Display : "—";
                RefreshUi();
                if (winner != null && engine.IsRunning) RunAccessTest();
            }
        }

        // ====================== диагностика ======================

        async void RunDiagnostics()
        {
            if (diagRunning) return;
            diagRunning = true;
            diagBtn.IsEnabled = false;
            diagStatus.Text = "Собираю сведения о системе и проверяю сеть…";
            try
            {
                int count = strategies.Count;
                Task<List<DiagItem>> info = Task.Run(() => Diagnostics.Collect(engine, count));
                Task<ServiceResult[]> net = NetCheck.CheckAll();
                await Task.WhenAll(info, net);
                lastDiag = info.Result;
                lastDiagTime = DateTime.Now;
                lastChecks = net.Result;
                ShowChecks(lastChecks);
                ApplyCheckResults(lastChecks, "diag");
                RenderDiagnostics();
                int errs = 0, warns = 0;
                foreach (DiagItem d in lastDiag) { if (d.Level == "err") errs++; else if (d.Level == "warn") warns++; }
                diagStatus.Text = "Обновлено в " + DateTime.Now.ToString("HH:mm:ss") + " · проблем: " + errs + ", предупреждений: " + warns;
                log.Add(errs > 0 ? "warn" : "info", "Диагностика: проблем " + errs + ", предупреждений " + warns + ".");
                events.Add("diag", errs > 0 ? "warn" : "ok", "Диагностика завершена", "Проблем: " + errs + ", предупреждений: " + warns, "diag");
                if (page != "diag") Toast("diag-done", errs > 0 ? "warn" : "ok", "Диагностика завершена", "Проблем: " + errs + ", предупреждений: " + warns + ". Нажмите, чтобы открыть.", "diag", false);
            }
            catch (Exception ex)
            {
                diagStatus.Text = "Диагностика не завершена: " + ex.Message;
                log.Add("err", diagStatus.Text);
            }
            finally
            {
                diagRunning = false;
                diagBtn.IsEnabled = true;
            }
        }

        string DiagReport()
        {
            return Diagnostics.Report(lastDiag ?? new List<DiagItem>(), lastChecks);
        }

        void CopyReport()
        {
            if (lastDiag == null) { log.Add("info", "Сначала запустите диагностику."); return; }
            try
            {
                Clipboard.SetText(DiagReport());
                diagStatus.Text = "Отчёт скопирован (имя пользователя, компьютера и полные IP-адреса скрыты).";
            }
            catch (Exception ex) { log.Add("err", "Буфер обмена недоступен: " + ex.Message); }
        }

        void SaveReport()
        {
            if (lastDiag == null) { log.Add("info", "Сначала запустите диагностику."); return; }
            SaveFileDialog d = new SaveFileDialog();
            d.FileName = "veylix-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
            d.Filter = "Текст (*.txt)|*.txt";
            if (d.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(d.FileName, DiagReport(), new System.Text.UTF8Encoding(true));
                diagStatus.Text = "Отчёт сохранён: " + Path.GetFileName(d.FileName);
            }
            catch (Exception ex) { log.Add("err", "Не удалось сохранить отчёт: " + ex.Message); }
        }

        // ====================== журнал ======================

        void ExportLog()
        {
            List<LogEntry> items = FilteredLog();
            SaveFileDialog d = new SaveFileDialog();
            d.FileName = "veylix-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
            d.Filter = "Текст (*.txt)|*.txt";
            if (d.ShowDialog(this) != true) return;
            string header = "Журнал Veylix " + Program.Version + " · экспорт " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                            " · записей: " + items.Count + (logFilter != "all" || logQuery.Length > 0 ? " (с учётом фильтра)" : "") +
                            "\r\nЛичные данные (имя пользователя, компьютера, полные IP, токены) скрыты.";
            string err = LogStore.Export(d.FileName, items, header);
            if (err != null) log.Add("err", "Экспорт журнала не удался: " + err);
            else log.Add("ok", "Журнал экспортирован: " + Path.GetFileName(d.FileName) + " (" + items.Count + " записей).");
        }

        // ====================== настройки ======================

        void PickTheme(string family)
        {
            if (cfg.Theme == family) return;
            cfg.Theme = family;
            SaveSettings();
            ApplyTheme();
        }

        void PickMode(string mode)
        {
            if (cfg.ThemeMode == mode) return;
            cfg.ThemeMode = mode;
            SaveSettings();
            ApplyTheme();
        }

        void ToggleGlassReduced()
        {
            cfg.GlassReduced = !cfg.GlassReduced;
            SaveSettings();
            ApplyTheme();
        }

        void ToggleAnimations()
        {
            cfg.Animations = !cfg.Animations;
            SaveSettings();
            Theme.Animations = cfg.Animations && SystemParameters.ClientAreaAnimation;
            ApplyTheme();
        }

        void ToggleCloseToTray()
        {
            cfg.CloseToTray = !cfg.CloseToTray;
            SaveSettings();
            RefreshSettingsState();
        }

        async void OnAutorun()
        {
            if (autorunBusy) return;
            autorunBusy = true;
            RefreshSettingsState();
            try
            {
                bool turnOn = !autorunOn;
                string exe = Assembly.GetExecutingAssembly().Location;
                string err = await Task.Run(() => Engine.SetAutorun(turnOn, exe));
                if (err != null)
                {
                    log.Add("err", "Автозапуск: " + err);
                    Toast("autorun-err", "err", "Автозапуск не изменён", err, "journal", true);
                    autorunOn = await Task.Run(() => Engine.IsAutorun());
                }
                else
                {
                    autorunOn = turnOn;
                    log.Add("ok", turnOn
                        ? "Автозапуск включён: при входе в Windows Veylix стартует в трее и включит последнюю стратегию."
                        : "Автозапуск выключен.");
                }
            }
            catch (Exception ex) { log.Add("err", ex.Message); }
            finally
            {
                autorunBusy = false;
                RefreshSettingsState();
            }
        }

        void ToggleCheckUpdates()
        {
            string err = engine.SetCheckUpdates(!engine.CheckUpdatesEnabled);
            if (err != null) log.Add("err", "Не удалось изменить проверку обновлений: " + err);
            RefreshSettingsState();
        }

        // ====================== обновление Flowseal ======================

        async Task CheckUpdates(bool userInitiated)
        {
            if (updating) return;
            updating = true;
            RefreshUpdateCard("Запрашиваю последний релиз на GitHub…");
            try
            {
                latest = await Task.Run(() => Updater.FetchLatest());
                string inst = engine.InstalledVersion();
                int cmp = Updater.CompareVersions(latest.Version, inst);
                if (cmp > 0)
                {
                    log.Add("info", "Доступна новая версия Flowseal " + latest.Version + " (установлена " + (inst ?? "?") + ").");
                    events.Add("update", "info", "Доступна версия Flowseal " + latest.Version, "Установлена " + (inst ?? "?") + ". Описание — в разделе «Обновления».", "updates");
                    Toast("update-" + latest.Version, "info", "Доступен Flowseal " + latest.Version, "Описание релиза и установка — в разделе «Обновления».", "updates", false);
                    if (!userInitiated && cfg.SkippedVersion != latest.Version)
                        TrayBalloon("Обновление Flowseal " + latest.Version, "Откройте Veylix → Настройки → Обновления, чтобы посмотреть изменения.", WF.ToolTipIcon.Info);
                }
                else if (userInitiated)
                {
                    log.Add("ok", "Установлена последняя версия Flowseal (" + (inst ?? "?") + ").");
                }
                updating = false;
                RefreshUpdateCard(cmp > 0 ? "Доступна новая версия." : "Установлена актуальная версия.");
            }
            catch (Exception ex)
            {
                updating = false;
                string msg = "Не удалось проверить обновления: " + Http.Describe(ex);
                if (userInitiated) log.Add("err", msg);
                else log.Add("warn", msg);
                RefreshUpdateCard(msg);
            }
        }

        async void CheckUpdatesCommand()
        {
            await CheckUpdates(true);
        }

        async void InstallUpdate()
        {
            if (latest == null || updating || busy || tuning) return;
            if (!latest.CanInstall)
            {
                OpenUrl(latest.HtmlUrl);
                return;
            }
            string inst = engine.InstalledVersion() ?? "?";
            bool go = VDialog.Ask(this, "Установить Flowseal " + latest.Version + "?",
                "Сейчас установлена " + inst + ". Архив скачивается с GitHub Flowseal, проверяется по SHA-256 из данных релиза, " +
                "распаковывается во временную папку и только потом заменяет текущую версию. Текущая версия сохранится как резервная копия, " +
                "ваши списки доменов, IPSet и игровой фильтр будут перенесены. Обход будет перезапущен.",
                string.IsNullOrEmpty(latest.Body) ? null : latest.Body,
                "Установить", "Отмена", "info");
            if (!go) return;

            updating = true;
            bool wasRunning = engine.IsRunning;
            string strategyName = (engine.Current ?? selected).Display;
            RefreshUi();
            RefreshUpdateCard("Подготовка…");
            string root = engine.Root;
            ReleaseInfo rel = latest;
            string err = await Task.Run(() => Updater.Install(rel, root,
                delegate(string m) { Ui_(delegate { RefreshUpdateCard(m); }); },
                delegate { engine.Stop(); }));
            updating = false;
            if (err != null)
            {
                log.Add("err", "Обновление Flowseal не установлено: " + err);
                events.Add("update", "err", "Обновление не установлено", err, "updates");
                RefreshUpdateCard("Ошибка: " + err);
                VDialog.Show(this, "Обновление не установлено", err, "err");
            }
            else
            {
                log.Add("ok", "Flowseal обновлён до " + rel.Version + ". Предыдущая версия сохранена в резервной копии.");
                events.Add("update", "ok", "Flowseal обновлён до " + rel.Version, "Предыдущая версия — в резервной копии", "updates");
                RefreshUpdateCard("Установлена версия " + rel.Version + ".");
            }
            ReloadStrategies(strategyName);
            RefreshUi();
            if (wasRunning) await StartAsync(selected);
        }

        async void RollbackUpdate()
        {
            if (updating || busy || tuning) return;
            string backup = Updater.LatestBackup(engine.Root);
            if (backup == null) { log.Add("info", "Резервных копий Flowseal нет."); return; }
            if (!VDialog.Ask(this, "Вернуть предыдущую версию?", "Текущая папка будет сохранена как резервная копия, а на её место вернётся " +
                Path.GetFileName(backup) + ". Обход будет остановлен.", null, "Вернуть", "Отмена", "warn")) return;
            updating = true;
            bool wasRunning = engine.IsRunning;
            string strategyName = (engine.Current ?? selected).Display;
            string root = engine.Root;
            string err = await Task.Run(() => Updater.Rollback(root, delegate { engine.Stop(); }));
            updating = false;
            if (err != null) log.Add("err", "Откат не выполнен: " + err);
            else log.Add("ok", "Возвращена предыдущая версия Flowseal (" + (engine.InstalledVersion() ?? "?") + ").");
            ReloadStrategies(strategyName);
            RefreshUpdateCard(err == null ? "Откат выполнен." : "Откат не выполнен.");
            RefreshUi();
            if (wasRunning) await StartAsync(selected);
        }

        void ReloadStrategies(string keepName)
        {
            engine.SetRoot(engine.Root);
            List<Strategy> list = engine.LoadStrategies();
            if (list.Count == 0) { log.Add("err", "После обновления не найдено ни одной стратегии."); return; }
            strategies = list;
            selected = strategies[0];
            foreach (Strategy s in strategies) if (s.Display == keepName) selected = s;
            RebuildStrategyCards();
        }

        // ====================== самопроверка интерфейса (--selftest-ui) ======================

        public void RenderSnapshots(string dir)
        {
            Directory.CreateDirectory(dir);
            string[] pagesToShoot = { "home", "strategies", "pulse", "diag", "sites", "assistant", "journal", "events", "updates", "settings" };
            log.Add("ok", "Обход работает: ALT3");
            log.Add("warn", "Проверка: Discord — Частично");
            log.Add("err", "Пример ошибки для журнала");
            log.Add("proc", "winws: windivert initialized");
            // показательные результаты проверки, чтобы увидеть все состояния значков
            ServiceResult a = new ServiceResult(); a.Name = "Интернет"; a.Status = ServiceStatus.Ok; a.Summary = "Доступен · 2/2";
            ServiceResult b = new ServiceResult(); b.Name = "Discord"; b.Status = ServiceStatus.Partial; b.Summary = "Частично: cdn.discordapp.com — TLS-рукопожатие зависло";
            ServiceResult c = new ServiceResult(); c.Name = "YouTube"; c.Status = ServiceStatus.Unavailable; c.Summary = "Недоступен (проверено дважды): соединение сбрасывается";
            // демонстрационная история задержек (только для снимков самопроверки)
            int[][] demo = { new int[] { 40, 38, 52, 47, 33, 61, 44, 39, 35, 42 }, new int[] { 210, 260, -1, 240, 230, 280, 250, -1, 240, 255 }, new int[] { 300, 280, -1, -1, 320, -1, -1, -1, -1, -1 } };
            string[] names = { "Интернет", "Discord", "YouTube" };
            for (int i = 0; i < 3; i++) latency[names[i]] = new List<int>(demo[i]);
            ShowChecks(new ServiceResult[] { a, b, c });
            strategies[3].LastScore = 12; strategies[3].LastMax = 12; strategies[3].LastSummary = "Discord 3/3 · YouTube 3/3";
            strategies[4].LastScore = 7; strategies[4].LastMax = 12; strategies[4].LastSummary = "Discord 3/3 · YouTube 1/3";
            strategies[5].LastScore = 0; strategies[5].LastMax = 12; strategies[5].LastSummary = "Discord 0/3 · YouTube 0/3";
            RefreshStrategyCards();

            // «Сайты и DNS»: пример вывода — DNS-подмена плюс DPI, и результат проверки DNS
            DnsAnswer dSys = new DnsAnswer(); dSys.Ok = true; dSys.Rcode = 3;
            DnsAnswer dSec = new DnsAnswer(); dSec.Ok = true; dSec.Rcode = 0; dSec.Ms = 41;
            dSec.Addresses.Add(IPAddress.Parse("104.21.32.39")); dSec.Addresses.Add(IPAddress.Parse("172.67.182.196"));
            ProbeResult dPr = new ProbeResult();
            dPr.Target = new Target("rutracker.org", "/", true);
            dPr.Status = ProbeStatus.Timeout; dPr.Stage = "TLS"; dPr.Address = "104.21.32.39"; dPr.DnsMs = 0; dPr.TcpMs = 38;
            dPr.Detail = "TLS-рукопожатие зависло — так обычно выглядит DPI-блокировка по SNI";
            siteInput.Text = "https://rutracker.org/forum/index.php";
            RenderSiteReport(SiteCheck.Diagnose("rutracker.org", dSys, dSec, dPr, true, null, null), null);
            Ui.SetChip(dnsChip, "Подмена DNS", "err");
            dnsVerdict.Text = "Провайдер подменяет DNS у 2 из 4 проверенных сайтов. Включите защищённый DNS — это уберёт блокировки такого типа.";
            foreach (string h in DnsControlHosts)
            {
                bool bad = h == "rutracker.org" || h == "www.instagram.com";
                dnsRows.Children.Add(DetailLine(h, "провайдер: " + (bad ? "домен не найден (NXDOMAIN)" : "142.250.74.14") + " · через HTTPS: 142.250.74.14", bad ? "Err" : "Text2"));
            }
            foreach (DohProvider dp in Doh.Providers)
            {
                DnsAnswer sa = new DnsAnswer(); sa.Ok = dp.Id != "quad9"; sa.Ms = dp.Id == "cloudflare" ? 34 : 52;
                dnsSpeed[dp.Id] = sa;
            }
            RefreshDnsCard();
            foreach (string fam in Theme.Families)
            {
                foreach (bool dark in new bool[] { false, true })
                {
                    if ((fam == "cream" && dark) || (fam == "graphite" && !dark)) continue;
                    if (Theme.IsIllustrated(fam) && dark != (fam != "meadow")) continue;
                    cfg.Theme = fam;
                    Theme.Apply(Theme.Build(fam, dark), false);
                    RefreshAppearanceTexts();
                    UpdateBlobs();
                    haloMode = "";
                    UpdateScenes();
                    ApplyPowerState(fam == "mint" || fam == "glass" || fam == "night" || fam == "aurora" ? "run" : "off", "ALT3");
                    foreach (string pg in pagesToShoot)
                    {
                        if (fam != "purple" && fam != "glass" && fam != "night" && pg != "home" && pg != "settings") continue;
                        ShowPage(pg);
                        Snap(Path.Combine(dir, Theme.Current.Id + "-" + pg + ".png"));
                        if (pg == "home" && (fam == "purple" || fam == "night" || fam == "cream"))
                        {
                            // панель трея отдельно: окно не показывается, рисуем её содержимое на фоне Popup
                            Border fly = new Border();
                            fly.Width = FlyoutW;
                            fly.BorderThickness = new Thickness(1);
                            fly.SetResourceReference(Border.BackgroundProperty, "Popup");
                            fly.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
                            design.Children.Add(fly);
                            fly.HorizontalAlignment = HorizontalAlignment.Left;
                            fly.VerticalAlignment = VerticalAlignment.Top;
                            foreach (string view in new string[] { "main", "strategy", "ipset" })
                            {
                                flyoutView = view;
                                fly.Child = BuildFlyoutContent();
                                design.Measure(new Size(DW, DH));
                                design.Arrange(new Rect(0, 0, DW, DH));
                                design.UpdateLayout();
                                SnapElement(fly, Path.Combine(dir, Theme.Current.Id + "-tray" + (view == "main" ? "" : "-" + view) + ".png"));
                            }
                            flyoutView = "main";
                            design.Children.Remove(fly);
                        }
                    }
                }
            }

            // Игровой режим: карточка целиком — с наборами и списком, в состоянии паузы
            foreach (string fam in new string[] { "purple", "cream" })
            {
                cfg.Theme = fam;
                Theme.Apply(Theme.Build(fam, fam != "cream"), false);
                cfg.GameWatch = true;
                cfg.GameWatchList = "vgc.exe|valorant-win64-shipping.exe|league of legends.exe|rainbowsix.exe";
                gamePausedFor = "valorant-win64-shipping.exe";
                RefreshGameModeCard();
                ShowPage("settings");
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                SnapElement(gamesCard, Path.Combine(dir, "games-" + Theme.Current.Id + ".png"));
                ShowPage("sites");
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                SnapElement(dnsCard, Path.Combine(dir, "dns-" + Theme.Current.Id + ".png"));
                ShowPage("settings");
                fbKind = "bug";
                fbText.Text = "После обновления до 2.3.6 YouTube открывается, но видео грузится рывками. Стратегия ALT13, провайдер Ростелеком.";
                fbContact.Text = "@username";
                tgFbReport.IsOn = true;
                RefreshFeedbackCard();
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                SnapElement(feedbackCard, Path.Combine(dir, "feedback-" + Theme.Current.Id + ".png"));
                fbText.Text = "";
                fbContact.Text = "";
                tgFbReport.IsOn = false;

                // «Что нового»: содержимое окна после обновления с 2.3.4
                Border wn = new Border();
                wn.Width = 520;
                wn.Padding = new Thickness(22, 20, 22, 18);
                wn.BorderThickness = new Thickness(1);
                wn.SetResourceReference(Border.BackgroundProperty, "Popup");
                wn.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
                wn.HorizontalAlignment = HorizontalAlignment.Left;
                wn.VerticalAlignment = VerticalAlignment.Top;
                StackPanel wns = new StackPanel();
                wns.Children.Add(Theme.T("Veylix обновлён до " + Program.Version, 17, "Text", true));
                TextBlock wnm = Theme.Wrap("Коротко о том, что изменилось:", 13, "Text2");
                wnm.Margin = new Thickness(0, 6, 0, 14);
                wns.Children.Add(wnm);
                wns.Children.Add(WhatsNewContent(WhatsNew.Since("2.3.4", Program.Version, 3)));
                wn.Child = wns;
                design.Children.Add(wn);
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                SnapElement(wn, Path.Combine(dir, "whatsnew-" + Theme.Current.Id + ".png"));
                design.Children.Remove(wn);
            }
            gamePausedFor = null;
            cfg.GameWatch = false;
            cfg.GameWatchList = "";
            RefreshGameModeCard();

            // Theme Studio: карточка целиком при разных настройках (поверх разных тем)
            string[][] studio = {
                new string[] { "purple", "dark", "", "100", "standard", "normal" },
                new string[] { "cream", "light", "#C2185B", "150", "round", "spacious" },
                new string[] { "glass", "dark", "#00838F", "0", "compact", "compact" },
                new string[] { "night", "dark", "#D9573C", "150", "round", "normal" } };
            foreach (string[] st in studio)
            {
                cfg.Theme = st[0];
                cfg.Studio.Accent = st[2];
                cfg.Studio.Glow = int.Parse(st[3]);
                cfg.Studio.Corners = st[4];
                cfg.Studio.Density = st[5];
                Theme.Options = cfg.Studio;
                Theme.Apply(Theme.Build(st[0], st[1] == "dark"), false);
                Theme.RescaleTree(design);
                RefreshAppearanceTexts();
                UpdateScenes();
                RefreshStudio();
                ShowPage("settings");
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                SnapElement(studioCard, Path.Combine(dir, "studio-" + st[0] + (st[2].Length > 0 ? "-custom" : "") + ".png"));
                ShowPage("home");
                Snap(Path.Combine(dir, "studio-" + st[0] + "-home.png"));
            }
            cfg.Studio = new ThemeOptions();
            Theme.Options = cfg.Studio;

            // карточка обновления Veylix в разных состояниях (данные релиза — пример только для снимков самопроверки)
            string why;
            appLatest = AppUpdate.PickLatest("[{\"tag_name\":\"v9.0.0\",\"name\":\"Veylix 9.0.0\",\"draft\":false,\"prerelease\":false," +
                "\"html_url\":\"https://github.com/FreeSize83/Veylix/releases/tag/v9.0.0\",\"published_at\":\"2026-10-15T10:00:00Z\"," +
                "\"body\":\"* Пример описания изменений\\n* Второй пункт\",\"assets\":[{\"name\":\"Veylix-9.0.0-win-x64.zip\",\"size\":1711376," +
                "\"digest\":\"sha256:" + new string('a', 64) + "\",\"browser_download_url\":\"https://github.com/FreeSize83/Veylix/releases/download/v9.0.0/Veylix-9.0.0-win-x64.zip\"}]}]", out why);
            foreach (string st in new string[] { "available", "downloading", "error" })
            {
                Theme.Apply(Theme.Build("purple", true), false);
                appState = st;
                appMessage = st == "downloading" ? "Скачиваю Veylix-9.0.0-win-x64.zip: 0.9 МБ из 1.6 МБ" : "загрузка прервана: превышено время ожидания";
                RefreshAppUpdateCard();
                ShowPage("updates");
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.Measure(new Size(DW, DH));
                design.Arrange(new Rect(0, 0, DW, DH));
                design.UpdateLayout();
                if (st == "downloading") SetAppProgress(0.55);
                Snap(Path.Combine(dir, "app-update-" + st + ".png"));
            }
            appLatest = null;
            appState = "idle";

            // все состояния пчёлки-компаньона
            StackPanel moods = new StackPanel();
            moods.Orientation = Orientation.Horizontal;
            moods.Background = Theme.B("#22263A");
            foreach (string m in new string[] { "idle", "happy", "diag", "error", "restored" })
            {
                Bee mbee = new Bee(110);
                mbee.Animated = false;
                mbee.SetMood(m);
                mbee.Margin = new Thickness(14);
                moods.Children.Add(mbee);
            }
            moods.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            moods.Arrange(new Rect(moods.DesiredSize));
            RenderTargetBitmap mb = new RenderTargetBitmap((int)moods.DesiredSize.Width, (int)moods.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
            mb.Render(moods);
            PngBitmapEncoder me = new PngBitmapEncoder();
            me.Frames.Add(BitmapFrame.Create(mb));
            using (FileStream fs = File.Create(Path.Combine(dir, "companion-moods.png"))) me.Save(fs);
        }

        // Отрисовка отдельного элемента (через VisualBrush — без смещения родителя)
        void SnapElement(FrameworkElement el, string file)
        {
            double w = el.ActualWidth, h = el.ActualHeight;
            if (w <= 0 || h <= 0) return;
            RenderTargetBitmap bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.Res("WindowBg"), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
            }
            bmp.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }

        void Snap(string file)
        {
            design.Measure(new Size(DW, DH));
            design.Arrange(new Rect(0, 0, DW, DH));
            design.Measure(new Size(DW, DH));
            design.Arrange(new Rect(0, 0, DW, DH));
            design.UpdateLayout();
            Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Background);
            RenderTargetBitmap bmp = new RenderTargetBitmap((int)DW, (int)DH, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.Res("WindowBg"), null, new Rect(0, 0, DW, DH));
            }
            bmp.Render(dv);
            bmp.Render(design);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }
    }

    class TuneAbort : Exception
    {
        public TuneAbort(string m) : base(m) { }
    }

}
