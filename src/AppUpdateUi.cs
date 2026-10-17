using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Veylix
{
    // Обновление самого Veylix: интерфейс на странице «Обновления»
    public partial class MainWindow
    {
        AppRelease appLatest;
        string appState = "idle";      // idle | checking | latest | available | nopackage | error | downloading | verifying | installing
        string appMessage = "";
        bool appLater;                 // «Позже»: не напоминать до следующей проверки
        CancellationTokenSource appCts;
        TextBlock appCur, appNew, appStatus, appNotesTitle, appNotes;
        Border appNotesBox, appTrack, appFill;
        VButton appCheckBtn, appUpdateBtn, appLaterBtn, appReleaseBtn, appCancelBtn, appRetryBtn;
        VToggle tgAppCheck;

        bool AppBusy { get { return appState == "checking" || appState == "downloading" || appState == "verifying" || appState == "installing"; } }

        FrameworkElement BuildAppUpdateCard()
        {
            Card c = new Card(22);
            c.Margin = new Thickness(0, 0, 0, 16);
            c.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s = new StackPanel();
            s.Children.Add(Ui.Heading("Обновление Veylix",
                "Источник — релизы " + AppUpdate.RepoUrl.Replace("https://", "") + ". Загрузка и установка — только по вашей кнопке: " +
                "пакет проверяется по SHA-256 из данных GitHub, прежняя версия сохраняется для отката, настройки и журналы не затрагиваются."));
            Grid info = new Grid();
            info.Margin = new Thickness(0, 14, 0, 4);
            info.ColumnDefinitions.Add(Theme.Col(170));
            info.ColumnDefinitions.Add(Theme.Col(0));
            info.RowDefinitions.Add(Theme.RowDef(-1));
            info.RowDefinitions.Add(Theme.RowDef(-1));
            appCur = InfoLine(info, 0, "Установлено");
            appNew = InfoLine(info, 1, "Последний релиз");
            appCur.Text = Program.Version + " (тег v" + Program.Version + ")";
            s.Children.Add(info);

            appStatus = Theme.Wrap("", 12.5, "Text2");
            appStatus.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(appStatus);

            appTrack = new Border();
            appTrack.Height = 6;
            appTrack.CornerRadius = new CornerRadius(3);
            appTrack.Margin = new Thickness(0, 10, 0, 0);
            appTrack.SetResourceReference(Border.BackgroundProperty, "Surface2");
            appFill = new Border();
            appFill.CornerRadius = new CornerRadius(3);
            appFill.HorizontalAlignment = HorizontalAlignment.Left;
            appFill.Width = 0;
            appFill.SetResourceReference(Border.BackgroundProperty, "Accent");
            appTrack.Child = appFill;
            appTrack.Visibility = Visibility.Collapsed;
            s.Children.Add(appTrack);

            appNotesTitle = Theme.T("", 13.5, "Text", true);
            appNotesTitle.Margin = new Thickness(0, 12, 0, 0);
            s.Children.Add(appNotesTitle);
            appNotesBox = new Border();
            appNotesBox.CornerRadius = new CornerRadius(12);
            appNotesBox.BorderThickness = new Thickness(1);
            appNotesBox.SetResourceReference(Border.BackgroundProperty, "Surface2");
            appNotesBox.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            appNotesBox.Margin = new Thickness(0, 8, 0, 0);
            appNotesBox.MaxHeight = 170;
            appNotes = Theme.Wrap("", 12, "Text2");
            appNotes.Margin = new Thickness(14, 10, 14, 10);
            appNotesBox.Child = Theme.Scroll(appNotes);
            s.Children.Add(appNotesBox);

            WrapPanel bw = new WrapPanel();
            bw.Margin = new Thickness(0, 14, 0, 0);
            appCheckBtn = Ui.Button("Проверить обновления", 0xE72C, false);
            appCheckBtn.Click += delegate { CheckAppUpdate(true); };
            appUpdateBtn = Ui.Button("Обновить", 0xE896, true);
            appUpdateBtn.Click += delegate { AppUpdateNow(); };
            appLaterBtn = Ui.Button("Позже", 0, false);
            appLaterBtn.ToolTip = "Не напоминать до следующей проверки";
            appLaterBtn.Click += delegate { appLater = true; RefreshAppUpdateCard(); };
            appReleaseBtn = Ui.Button("Открыть релиз", 0xE8A7, false);
            appReleaseBtn.Click += delegate { OpenAppReleasePage(); };
            appCancelBtn = Ui.Button("Отменить", 0xE711, false);
            appCancelBtn.Click += delegate { if (appCts != null) appCts.Cancel(); };
            appRetryBtn = Ui.Button("Повторить", 0xE72C, true);
            appRetryBtn.Click += delegate { if (appLatest != null && appLatest.CanInstall) AppUpdateNow(); else CheckAppUpdate(true); };
            foreach (VButton b in new VButton[] { appCheckBtn, appUpdateBtn, appRetryBtn, appLaterBtn, appReleaseBtn, appCancelBtn })
            {
                b.Margin = new Thickness(0, 0, 8, 8);
                bw.Children.Add(b);
            }
            s.Children.Add(bw);
            s.Children.Add(Ui.Divider());
            tgAppCheck = new VToggle();
            tgAppCheck.IsOn = cfg.AppUpdateCheck;
            tgAppCheck.Clicked += delegate
            {
                cfg.AppUpdateCheck = !cfg.AppUpdateCheck;
                tgAppCheck.IsOn = cfg.AppUpdateCheck;
                SaveSettings();
            };
            TextBlock d;
            s.Children.Add(Ui.SettingRow("Проверять при запуске", "Только сверка версии и уведомление. Скачивание и установка — никогда без вашего подтверждения.", tgAppCheck, out d));
            c.Content = s;
            RefreshAppUpdateCard();
            return c;
        }

        static string Size(long bytes)
        {
            if (bytes <= 0) return "размер неизвестен";
            return bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0") + " МБ" : (bytes / 1024) + " КБ";
        }

        void OpenAppReleasePage()
        {
            string url = appLatest != null && appLatest.HtmlUrl != null ? appLatest.HtmlUrl : AppUpdate.ReleasesUrl;
            if (!url.StartsWith(AppUpdate.RepoUrl, StringComparison.OrdinalIgnoreCase)) url = AppUpdate.ReleasesUrl;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { log.Add("err", "Не удалось открыть браузер: " + ex.Message); }
        }

        bool AppNewer { get { return appLatest != null && AppUpdate.Compare(appLatest.Version, Program.Version) > 0; } }

        void RefreshAppUpdateCard()
        {
            if (appStatus == null) return;
            bool newer = AppNewer;
            if (appLatest == null) appNew.Text = appState == "checking" ? "проверяю…" : "ещё не проверялся";
            else
            {
                appNew.Text = appLatest.Version + (appLatest.Published != DateTime.MinValue ? " · " + appLatest.Published.ToString("dd.MM.yyyy") : "") +
                    (appLatest.AssetSize > 0 ? " · " + Size(appLatest.AssetSize) : "") + (newer ? " · доступно обновление" : "");
                appNew.SetResourceReference(TextBlock.ForegroundProperty, newer ? "Accent" : "Text");
            }
            string status;
            string key = "Text2";
            switch (appState)
            {
                case "checking": status = "Запрашиваю список релизов на GitHub…"; break;
                case "latest": status = "✓ У вас последняя версия Veylix."; key = "Ok"; break;
                case "available": status = appLater ? "Обновление отложено. Оно будет предложено снова при следующей проверке." : "Доступна новая версия " + appLatest.Version + ". Нажмите «Обновить» — Veylix скачает пакет, проверит его, перезапустится и при неудаче вернёт прежнюю версию."; key = appLater ? "Text2" : "Accent"; break;
                case "nopackage": status = "Найден релиз " + appLatest.Version + ", но установить его автоматически нельзя: " + appLatest.Problem + ". Его можно скачать вручную со страницы релиза."; key = "Warn"; break;
                case "error": status = "Не получилось: " + appMessage; key = "Err"; break;
                case "downloading": status = appMessage; break;
                case "verifying": status = "Проверяю пакет: размер, SHA-256, состав и версия…"; break;
                case "installing": status = "Пакет проверен. Veylix закрывается, помощник заменит файл и запустит новую версию…"; break;
                default: status = appMessage.Length > 0 ? appMessage : "Нажмите «Проверить обновления»."; break;
            }
            appStatus.Text = status;
            appStatus.SetResourceReference(TextBlock.ForegroundProperty, key);
            bool showNotes = appLatest != null && (newer || appState == "nopackage");
            appNotesTitle.Visibility = showNotes ? Visibility.Visible : Visibility.Collapsed;
            appNotesBox.Visibility = showNotes ? Visibility.Visible : Visibility.Collapsed;
            if (showNotes)
            {
                appNotesTitle.Text = "Что нового в " + (appLatest.Name.Length > 0 ? appLatest.Name : "v" + appLatest.Version);
                appNotes.Text = appLatest.Body.Trim().Length > 0 ? appLatest.Body.Replace("\r", "").Trim() : "Описание изменений не указано.";
            }
            appTrack.Visibility = appState == "downloading" || appState == "verifying" || appState == "installing" ? Visibility.Visible : Visibility.Collapsed;
            appCheckBtn.IsEnabled = !AppBusy;
            appUpdateBtn.Visibility = newer && appLatest.CanInstall && appState != "error" && !appLater && !AppBusy ? Visibility.Visible : Visibility.Collapsed;
            if (newer) Ui.SetButtonText(appUpdateBtn, "Обновить до " + appLatest.Version);
            appUpdateBtn.IsEnabled = !updating && !tuning && !labRunning;
            appLaterBtn.Visibility = appUpdateBtn.Visibility;
            appRetryBtn.Visibility = appState == "error" ? Visibility.Visible : Visibility.Collapsed;
            appCancelBtn.Visibility = appState == "downloading" ? Visibility.Visible : Visibility.Collapsed;
            appReleaseBtn.Visibility = appLatest != null || appState == "error" ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetAppProgress(double fraction)
        {
            double w = Math.Max(0, Math.Min(1, fraction)) * (appTrack.ActualWidth > 0 ? appTrack.ActualWidth : 800);
            appFill.Width = w;
        }

        async void CheckAppUpdate(bool userInitiated)
        {
            if (AppBusy) return;
            appState = "checking";
            appLater = false;
            RefreshAppUpdateCard();
            string why = null;
            AppRelease r = null;
            Exception fail = null;
            try { r = await Task.Run(() => AppUpdate.FetchLatest(out why)); }
            catch (Exception ex) { fail = ex; }
            if (fail != null)
            {
                appState = "error";
                appMessage = "не удалось проверить обновления — " + Http.Describe(fail) + ". Проверьте подключение и повторите.";
                if (userInitiated) log.Add("err", "Обновление Veylix: " + appMessage);
                else log.Add("warn", "Обновление Veylix: " + appMessage);
                RefreshAppUpdateCard();
                return;
            }
            appLatest = r;
            if (r == null)
            {
                appState = "idle";
                appMessage = "Обновлений нет: " + why + ".";
                if (userInitiated) log.Add("info", "Обновление Veylix: " + why + ".");
            }
            else if (AppUpdate.Compare(r.Version, Program.Version) <= 0)
            {
                appState = "latest";
                if (userInitiated) log.Add("ok", "Установлена последняя версия Veylix (" + Program.Version + ").");
            }
            else if (!r.CanInstall)
            {
                appState = "nopackage";
                log.Add("warn", "Вышел Veylix " + r.Version + ", но автоматическая установка невозможна: " + r.Problem + ".");
            }
            else
            {
                appState = "available";
                log.Add("info", "Доступна новая версия Veylix " + r.Version + " (сейчас " + Program.Version + ").");
                events.Add("update", "info", "Доступна Veylix " + r.Version, "Установлена " + Program.Version + ". Обновление — в разделе «Обновления».", "updates");
                Toast("app-update-" + r.Version, "info", "Доступна Veylix " + r.Version, "Нажмите, чтобы посмотреть изменения и обновить.", "updates", false);
            }
            RefreshAppUpdateCard();
        }

        async void AppUpdateNow()
        {
            if (AppBusy || appLatest == null || !appLatest.CanInstall || !AppNewer) return;
            AppRelease r = appLatest;
            bool running = engine.IsRunning;
            if (!VDialog.Ask(this, "Обновить Veylix до " + r.Version + "?",
                "Сейчас установлена " + Program.Version + ". Размер загрузки: " + Size(r.AssetSize) + ". " +
                "Veylix скачает пакет, проверит его и перезапустится" + (running ? " — обход ненадолго остановится и включится снова" : "") + ". " +
                "Настройки, темы и журналы сохраняются; прежняя версия останется рядом и вернётся автоматически, если новая не запустится.",
                r.Body.Trim().Length > 0 ? r.Body.Replace("\r", "").Trim() : null, "Обновить", "Позже", "info"))
            {
                appLater = true;
                RefreshAppUpdateCard();
                return;
            }
            string target = Assembly.GetExecutingAssembly().Location;
            string err = AppUpdate.CheckWritable(target);
            if (err != null) { AppUpdateFailed(err + ". Запустите Veylix из папки, куда есть доступ на запись, или обновите вручную."); return; }

            appState = "downloading";
            appMessage = "Получаю контрольную сумму…";
            appCts = new CancellationTokenSource();
            CancellationToken ct = appCts.Token;
            RefreshAppUpdateCard();
            SetAppProgress(0);
            string sha = null;
            try { sha = await Task.Run(() => AppUpdate.ResolveSha(r)); }
            catch (Exception ex) { err = "не удалось получить контрольную сумму — " + Http.Describe(ex); }
            if (err == null && sha == null) err = "в релизе нет корректной контрольной суммы SHA-256 — установка отменена";
            if (err != null) { AppUpdateFailed(err); return; }

            appMessage = "Скачиваю " + r.AssetName + "…";
            RefreshAppUpdateCard();
            err = await Task.Run(() => AppUpdate.Download(r, delegate(long done, long total)
            {
                Ui_(delegate
                {
                    long t = total > 0 ? total : r.AssetSize;
                    appMessage = "Скачиваю " + r.AssetName + ": " + Size(done) + (t > 0 ? " из " + Size(t) : "");
                    appStatus.Text = appMessage;
                    if (t > 0) SetAppProgress((double)done / t);
                });
            }, ct));
            if (err != null) { AppUpdateFailed(err); return; }

            appState = "verifying";
            RefreshAppUpdateCard();
            SetAppProgress(1);
            string exe = null, exeSha = null;
            err = await Task.Run(delegate { string e, x; string res = AppUpdate.Stage(AppUpdate.PackagePath(r.Version), sha, r.AssetSize, r.Version, out e, out x); exe = e; exeSha = x; return res; });
            if (err != null) { AppUpdateFailed(err); return; }

            appState = "installing";
            RefreshAppUpdateCard();
            UpdatePlan plan = AppUpdate.MakePlan(exe, exeSha, target, r.Version, running);
            err = AppUpdate.LaunchHelper(plan);
            if (err != null) { AppUpdateFailed(err); return; }
            log.Add("info", "Обновление Veylix " + Program.Version + " → " + r.Version + ": пакет проверен, перезапуск.");
            events.Add("update", "info", "Установка Veylix " + r.Version, "Пакет проверен по SHA-256, запущен помощник обновления", "updates");
            await Task.Delay(400);
            ExitApp();
        }

        void AppUpdateFailed(string msg)
        {
            appState = "error";
            appMessage = msg;
            log.Add("err", "Обновление Veylix не выполнено: " + msg);
            events.Add("update", "err", "Обновление Veylix не выполнено", msg, "updates");
            RefreshAppUpdateCard();
        }

        // Вызывается при запуске: подтверждение после обновления или сообщение о неудаче/откате
        void HandlePostUpdate()
        {
            if (Program.PostUpdatePlan != null)
            {
                string msg;
                UpdatePlan p = AppUpdate.ConfirmStarted(Program.PostUpdatePlan, out msg);
                if (p != null)
                {
                    log.Add("ok", msg);
                    events.Add("update", "ok", "Veylix обновлён до " + Program.Version, msg, "updates");
                    appState = "latest";
                    appMessage = msg;
                    RefreshAppUpdateCard();
                    Toast("app-updated", "ok", "Veylix обновлён до " + Program.Version, "Настройки и журналы на месте.", "updates", true);
                    if (p.RestartEngine && selected != null && !selfTest)
                    {
                        log.Add("info", "Обход был включён до обновления — включаю снова.");
                        StartAfterUpdate();
                    }
                }
            }
            string failure = AppUpdate.TakeFailure();
            if (failure != null)
            {
                log.Add("err", "Обновление Veylix: " + failure);
                events.Add("update", "err", "Обновление Veylix не установлено", failure, "updates");
                appState = "error";
                appMessage = failure;
                RefreshAppUpdateCard();
                VDialog.Show(this, "Обновление не установлено", failure + "\n\nМожно повторить попытку в разделе «Обновления».", "err");
            }
        }

        async void StartAfterUpdate()
        {
            await StartAsync(selected);
        }
    }
}
