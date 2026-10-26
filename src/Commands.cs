using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Veylix
{
    // Команда приложения. Палитра (Ctrl+K), горячие клавиши и закреплённые быстрые действия
    // вызывают один и тот же Run — отдельных обработчиков-дублей нет.
    public class AppCommand
    {
        public string Id, Title, Short, Group, Keywords = "", Shortcut = "";
        public int Glyph;
        public Func<string> DynamicTitle;   // заголовок, зависящий от состояния («Включить/Выключить обход»)
        public Func<string> Unavailable;    // причина, по которой команда сейчас недоступна, или null
        public Action Run;

        public string CurrentTitle { get { return DynamicTitle != null ? DynamicTitle() : Title; } }
        public string ShortTitle { get { return Short ?? CurrentTitle; } }

        public string Reason()
        {
            return Unavailable != null ? Unavailable() : null;
        }

        static string Norm(string s)
        {
            return (s ?? "").ToLowerInvariant().Replace('ё', 'е');
        }

        // Оценка совпадения с запросом (0 — не подходит). Все слова запроса должны найтись
        // в названии, группе или ключевых словах; совпадение в начале названия ценится выше.
        public static int Score(AppCommand c, string query)
        {
            string q = Norm(query).Trim();
            if (q.Length == 0) return 1;
            string title = Norm(c.CurrentTitle);
            string all = title + " " + Norm(c.Group) + " " + Norm(c.Keywords) + " " + Norm(c.Shortcut);
            int score = 0;
            foreach (string w in q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (title.StartsWith(w)) score += 30;
                else if (title.Contains(" " + w)) score += 20;
                else if (title.Contains(w)) score += 12;
                else if (all.Contains(w)) score += 4;
                else return 0;
            }
            if (title.StartsWith(q)) score += 50;
            else if (title.Contains(q)) score += 20;
            return score;
        }

        public static List<AppCommand> Filter(IList<AppCommand> list, string query)
        {
            List<KeyValuePair<int, int>> ranked = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < list.Count; i++)
            {
                int s = Score(list[i], query);
                if (s > 0) ranked.Add(new KeyValuePair<int, int>(i, s));
            }
            // стабильная сортировка: по убыванию оценки, при равенстве — порядок регистрации
            ranked.Sort(delegate(KeyValuePair<int, int> a, KeyValuePair<int, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            List<AppCommand> r = new List<AppCommand>();
            foreach (KeyValuePair<int, int> kv in ranked) r.Add(list[kv.Key]);
            return r;
        }

        // Текст сочетания клавиш для нажатия, например «Ctrl+Shift+E», «F5», «Ctrl+1»
        public static string Gesture(ModifierKeys mods, Key key)
        {
            string k;
            if (key >= Key.D0 && key <= Key.D9) k = ((int)(key - Key.D0)).ToString();
            else if (key >= Key.NumPad0 && key <= Key.NumPad9) k = ((int)(key - Key.NumPad0)).ToString();
            else if (key >= Key.A && key <= Key.Z) k = key.ToString();
            else if (key >= Key.F1 && key <= Key.F12) k = key.ToString();
            else if (key == Key.OemComma) k = ",";
            else return null;
            string m = "";
            if ((mods & ModifierKeys.Control) != 0) m += "Ctrl+";
            if ((mods & ModifierKeys.Shift) != 0) m += "Shift+";
            if ((mods & ModifierKeys.Alt) != 0) m += "Alt+";
            // одиночные буквы и цифры без модификаторов не перехватываем — ими печатают
            if (m.Length == 0 && !(key >= Key.F1 && key <= Key.F12)) return null;
            return m + k;
        }
    }

    public partial class MainWindow
    {
        readonly List<AppCommand> commands = new List<AppCommand>();
        readonly Dictionary<string, AppCommand> commandById = new Dictionary<string, AppCommand>();

        AppCommand Cmd(string id, string group, int glyph, string title, string shortcut, Action run)
        {
            AppCommand c = new AppCommand();
            c.Id = id; c.Group = group; c.Glyph = glyph; c.Title = title; c.Shortcut = shortcut ?? ""; c.Run = run;
            commands.Add(c);
            commandById[id] = c;
            return c;
        }

        string BusyReason()
        {
            if (tuning) return "идёт автоподбор стратегии";
            if (updating) return "идёт обновление Flowseal";
            if (busy) return "выполняется другая операция";
            return null;
        }

        // Открыть страницу параметров Windows (только просмотр — Veylix сам ничего не меняет)
        void OpenSettings(string uri)
        {
            if (!uri.StartsWith("ms-settings:network")) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { log.Add("err", "Не удалось открыть параметры Windows: " + ex.Message); }
        }

        void Page(string name)
        {
            if (!IsVisible) ShowFromTray();
            ShowPage(name);
        }

        void BuildCommands()
        {
            commands.Clear();
            commandById.Clear();
            const string nav = "Разделы", net = "Обход", chk = "Проверки", files = "Файлы и списки", look = "Оформление", app = "Приложение";

            Cmd("page.home", nav, 0xE80F, "Открыть главную", "Ctrl+1", delegate { Page("home"); }).Keywords = "главная домой";
            Cmd("page.strategies", nav, 0xE8FD, "Открыть стратегии", "Ctrl+2", delegate { Page("strategies"); }).Keywords = "лаборатория стратегий lab";
            Cmd("page.pulse", nav, 0xE9D2, "Открыть Network Pulse", "Ctrl+3", delegate { Page("pulse"); }).Keywords = "пульс сети задержка монитор";
            Cmd("page.diag", nav, 0xE9D9, "Открыть диагностику", "Ctrl+4", delegate { Page("diag"); });
            Cmd("page.sites", nav, 0xE774, "Открыть «Сайты и DNS»", "Ctrl+9", delegate { Page("sites"); }).Keywords = "сайт не открывается домен dns днс";
            Cmd("page.journal", nav, 0xE7C3, "Открыть журнал", "Ctrl+5", delegate { Page("journal"); }).Keywords = "лог логи log";
            Cmd("page.events", nav, 0xE81C, "Открыть историю событий", "Ctrl+6", delegate { Page("events"); }).Keywords = "события сеанс статистика таймлайн";
            Cmd("page.updates", nav, 0xE895, "Открыть обновления", "Ctrl+7", delegate { Page("updates"); });
            Cmd("page.settings", nav, 0xE713, "Открыть настройки", "Ctrl+,", delegate { Page("settings"); }).Keywords = "параметры опции";

            AppCommand power = Cmd("engine.toggle", net, 0xE7E8, "Включить обход", "Ctrl+Shift+E", OnPower);
            power.DynamicTitle = delegate { return engine.IsRunning ? "Выключить обход" : "Включить обход"; };
            power.Unavailable = BusyReason;
            power.Keywords = "питание старт стоп запустить остановить winws движок";
            power.Short = "Вкл/выкл обход";
            AppCommand next = Cmd("strategy.next", net, 0xE893, "Следующая стратегия", null, OnNextStrategy);
            next.Unavailable = BusyReason;
            Cmd("strategy.pick", net, 0xE8FD, "Выбрать стратегию…", null, delegate { Page("strategies"); }).Keywords = "сменить стратегию";
            AppCommand tune = Cmd("tune.toggle", net, 0xE9F5, "Запустить автоподбор", null, delegate { if (!tuning) Page("strategies"); OnTune(); });
            tune.DynamicTitle = delegate { return tuning ? "Остановить автоподбор" : "Запустить автоподбор"; };
            tune.Unavailable = delegate { return tuning ? null : (updating ? "идёт обновление Flowseal" : busy ? "выполняется другая операция" : null); };
            tune.Short = "Автоподбор";
            AppCommand lab = Cmd("lab.test", net, 0xE9D9, "Проверить выбранную стратегию", null, TestSelectedStrategy);
            lab.Unavailable = delegate { return labRunning ? "проверка уже идёт" : BusyReason(); };
            lab.Keywords = "лаборатория тест стратегии";
            lab.Short = "Тест стратегии";
            AppCommand pref = Cmd("strategy.prefer", net, 0xE734, "Сделать текущую стратегию предпочтительной", null, MarkPreferred);
            pref.Keywords = "избранная любимая предпочтительная";
            AppCommand ips = Cmd("ipset.cycle", net, 0xE774, "Переключить режим IPSet", null, OnIpset);
            ips.Unavailable = BusyReason;
            AppCommand game = Cmd("game.cycle", net, 0xE7FC, "Переключить игровой фильтр", null, OnGame);
            game.Unavailable = BusyReason;

            AppCommand check = Cmd("check.services", chk, 0xE72C, "Проверить сервисы", "F5", delegate { RunAccessTest(); });
            check.Unavailable = delegate { return checking ? "проверка уже идёт" : null; };
            check.Keywords = "доступность discord youtube интернет соединение";
            check.Short = "Проверить сервисы";
            AppCommand gw = Cmd("games.toggle", net, 0xE7FC, "Включить игровой режим", null, delegate
            {
                cfg.GameWatch = !cfg.GameWatch;
                SaveSettings();
                log.Add("info", cfg.GameWatch ? "Игровой режим включён." : "Игровой режим выключен.");
                RefreshGameModeCard();
                if (cfg.GameWatch && GameList.Count == 0) Page("settings");
                CheckGames();
            });
            gw.DynamicTitle = delegate { return cfg.GameWatch ? "Выключить игровой режим" : "Включить игровой режим"; };
            gw.Keywords = "игры античит vanguard valorant faceit пауза";
            AppCommand site = Cmd("site.check", chk, 0xE721, "Проверить сайт…", null, delegate { Page("sites"); siteInput.Focus(); });
            site.Keywords = "не открывается сайт домен добавить в обход список";
            site.Short = "Проверить сайт";
            AppCommand dns = Cmd("dns.check", chk, 0xE72E, "Проверить DNS на подмену", null, delegate { Page("sites"); RunDnsCheck(); });
            dns.Unavailable = delegate { return dnsBusy ? "проверка DNS уже идёт" : null; };
            dns.Keywords = "днс doh https защищённый cloudflare google блокировка";
            AppCommand diag = Cmd("diag.run", chk, 0xE9D9, "Запустить диагностику", "Ctrl+D", delegate { Page("diag"); RunDiagnostics(); });
            diag.Unavailable = delegate { return diagRunning ? "диагностика уже идёт" : null; };
            diag.Short = "Диагностика";
            AppCommand pulse = Cmd("pulse.toggle", chk, 0xE9D2, "Запустить Network Pulse", "Ctrl+Shift+P", TogglePulse);
            pulse.DynamicTitle = delegate { return pulseMon.Running ? "Остановить Network Pulse" : "Запустить Network Pulse"; };
            pulse.Keywords = "мониторинг задержка пульс";
            pulse.Short = "Network Pulse";
            AppCommand appUpd = Cmd("app.update.check", chk, 0xE895, "Проверить обновления Veylix", null, delegate { Page("updates"); CheckAppUpdate(true); });
            appUpd.Unavailable = delegate { return AppBusy ? "проверка или установка уже идёт" : null; };
            appUpd.Keywords = "обновить версия релиз github";
            AppCommand appInst = Cmd("app.update.install", chk, 0xE896, "Обновить Veylix", null, delegate { Page("updates"); AppUpdateNow(); });
            appInst.DynamicTitle = delegate { return appLatest != null && AppNewer ? "Обновить Veylix до " + appLatest.Version : "Обновить Veylix"; };
            appInst.Unavailable = delegate
            {
                if (AppBusy) return "обновление уже идёт";
                if (appLatest == null || !AppNewer) return "новая версия не найдена — сначала проверьте обновления";
                if (!appLatest.CanInstall) return appLatest.Problem;
                return null;
            };
            Cmd("app.whatsnew", chk, 0xE789, "Что нового в Veylix", null, delegate
            {
                if (!IsVisible) ShowFromTray();
                ShowWhatsNew(WhatsNew.Recent(Program.Version, 3), false);
            }).Keywords = "изменения changelog версия новое обновление";
            AppCommand upd = Cmd("updates.check", chk, 0xE895, "Проверить обновления Flowseal", "Ctrl+U", delegate { Page("updates"); CheckUpdatesCommand(); });
            upd.Unavailable = delegate { return updating ? "идёт обновление" : null; };
            upd.Short = "Обновления";
            AppCommand rep = Cmd("report.copy", chk, 0xE8C8, "Скопировать отчёт диагностики", null, CopyReport);
            rep.Unavailable = delegate { return lastDiag == null ? "сначала запустите диагностику" : null; };

            Cmd("lists.user", files, 0xE8A5, "Открыть мои домены", null, delegate { OnOpenUserList("list-general-user.txt"); }).Short = "Мои домены";
            Cmd("lists.exclude", files, 0xE711, "Открыть исключения", null, delegate { OnOpenUserList("list-exclude-user.txt"); }).Short = "Исключения";
            AppCommand ipu = Cmd("ipset.update", files, 0xE896, "Обновить список IPSet", null, OnUpdateIpset);
            ipu.Unavailable = BusyReason;
            ipu.Short = "Обновить IPSet";
            Cmd("folder.open", files, 0xE838, "Открыть папку обхода", null, delegate { OpenPath("explorer.exe", engine.Root); }).Short = "Папка обхода";
            Cmd("data.open", files, 0xE838, "Открыть папку данных Veylix", null, delegate { OpenPath("explorer.exe", Settings.Dir); });
            Cmd("log.export", files, 0xE74E, "Экспортировать журнал", null, ExportLog).Keywords = "сохранить лог";

            foreach (string fam in Theme.Families)
            {
                string f = fam;
                Cmd("theme." + f, look, 0xE790, "Тема: " + Theme.FamilyTitle(f), null, delegate { PickTheme(f); }).Keywords = "тема оформление цвет";
            }
            Cmd("theme.mode.manual", look, 0xE706, "Светлая/тёмная: вручную", null, delegate { PickMode("manual"); });
            Cmd("theme.mode.system", look, 0xE706, "Светлая/тёмная: как в Windows", null, delegate { PickMode("system"); });
            Cmd("theme.mode.schedule", look, 0xE823, "Светлая/тёмная: по расписанию", null, delegate { PickMode("schedule"); });
            AppCommand anim = Cmd("anim.toggle", look, 0xE7B5, "Выключить анимации", null, ToggleAnimations);
            anim.DynamicTitle = delegate { return cfg.Animations ? "Выключить анимации" : "Включить анимации"; };
            anim.Unavailable = delegate { return SystemParameters.ClientAreaAnimation ? null : "анимации отключены в Windows"; };

            AppCommand toasts = Cmd("toasts.toggle", app, 0xEA8F, "Выключить необязательные уведомления", null, ToggleToasts);
            toasts.DynamicTitle = delegate { return cfg.Toasts ? "Выключить необязательные уведомления" : "Включить необязательные уведомления"; };
            Cmd("tray.hide", app, 0xE8A0, "Скрыть в трей", null, HideToTray);
            Cmd("page.assistant", nav, 0xEA80, "Открыть помощника", "Ctrl+8", delegate { if (!IsVisible) ShowFromTray(); OpenAssistant(null); }).Keywords = "помощь как исправить проблема причина";
            AppCommand bee = Cmd("companion.toggle", look, 0xE76E, "Скрыть пчёлку", null, delegate { cfg.CompanionVisible = !cfg.CompanionVisible; ApplyStudio(); });
            bee.DynamicTitle = delegate { return cfg.CompanionVisible ? "Скрыть пчёлку" : "Показать пчёлку"; };
            bee.Keywords = "компаньон талисман персонаж";
            Cmd("studio.reset", look, 0xE777, "Сбросить Theme Studio", null, delegate { cfg.Studio = new ThemeOptions(); ApplyStudio(); }).Keywords = "акцент свечение углы плотность скорость";
            Cmd("win.proxy", "Windows", 0xE774, "Открыть настройки прокси Windows", null, delegate { OpenSettings("ms-settings:network-proxy"); }).Keywords = "прокси proxy";
            Cmd("win.network", "Windows", 0xE839, "Открыть сетевые параметры Windows", null, delegate { OpenSettings("ms-settings:network-status"); }).Keywords = "dns сеть адаптер";
            Cmd("palette.open", app, 0xE721, "Палитра команд", "Ctrl+K", OpenPalette).Unavailable = delegate { return "уже открыта"; };
            Cmd("app.exit", app, 0xE8BB, "Выйти из Veylix", null, ExitApp).Keywords = "закрыть выход";
        }

        // Глобальные горячие клавиши окна
        void OnGlobalKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (paletteLayer != null && paletteLayer.Visibility == Visibility.Visible) return;   // палитра обрабатывает клавиши сама
            if (PickerOpen) return;                                                               // и плашка выбора стратегии тоже
            string g = AppCommand.Gesture(Keyboard.Modifiers, key);
            if (g == null) return;
            if (g == "Ctrl+K") { OpenPalette(); e.Handled = true; return; }
            foreach (AppCommand c in commands)
            {
                if (c.Shortcut != g) continue;
                e.Handled = true;
                string why = c.Reason();
                if (why != null) Toast("cmd-" + c.Id, "info", c.CurrentTitle, "Сейчас недоступно: " + why + ".", null, false);
                else c.Run();
                return;
            }
        }

        // ====================== палитра команд ======================

        Grid paletteLayer;
        TextBox paletteBox;
        StackPanel paletteList;
        ScrollViewer paletteScroll;
        TextBlock paletteHint;
        List<AppCommand> paletteItems = new List<AppCommand>();
        List<VButton> paletteRows = new List<VButton>();
        int paletteIndex;
        IInputElement focusBeforePalette;

        FrameworkElement BuildPalette()
        {
            paletteLayer = new Grid();
            paletteLayer.Visibility = Visibility.Collapsed;
            Border scrim = new Border();
            scrim.SetResourceReference(Border.BackgroundProperty, "Scrim");
            scrim.MouseLeftButtonDown += delegate { ClosePalette(); };
            paletteLayer.Children.Add(scrim);

            Card c = new Card(18, "Popup", "StrokeStrong");
            c.Width = 620;
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Top;
            c.Margin = new Thickness(212, 86, 0, 0);
            c.Inner = new Thickness(0);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            Grid search = new Grid();
            search.Margin = new Thickness(14, 14, 14, 10);
            paletteBox = new TextBox();
            paletteBox.Height = 42;
            paletteBox.FontSize = 15;
            paletteBox.Padding = new Thickness(34, 0, 60, 0);
            search.Children.Add(paletteBox);
            TextBlock ic = Theme.Icon(0xE721, 14, "Text3");
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.Margin = new Thickness(13, 0, 0, 0);
            ic.IsHitTestVisible = false;
            search.Children.Add(ic);
            TextBlock ph = Theme.T("Найти команду: «проверить», «тема», «журнал»…", 14, "Text3", false);
            ph.VerticalAlignment = VerticalAlignment.Center;
            ph.Margin = new Thickness(40, 0, 70, 0);
            ph.IsHitTestVisible = false;
            search.Children.Add(ph);
            Border esc = Ui.Chip("Esc", "idle");
            esc.HorizontalAlignment = HorizontalAlignment.Right;
            esc.Margin = new Thickness(0, 0, 10, 0);
            esc.IsHitTestVisible = false;
            search.Children.Add(esc);
            g.Children.Add(search);

            paletteList = new StackPanel();
            paletteList.Margin = new Thickness(8, 0, 8, 0);
            paletteScroll = Theme.Scroll(paletteList);
            paletteScroll.MaxHeight = 392;
            Grid.SetRow(paletteScroll, 1);
            g.Children.Add(paletteScroll);

            Border foot = new Border();
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            foot.Margin = new Thickness(0, 8, 0, 0);
            paletteHint = Theme.T("", 11.5, "Text3", false);
            paletteHint.Margin = new Thickness(18, 9, 18, 10);
            foot.Child = paletteHint;
            Grid.SetRow(foot, 2);
            g.Children.Add(foot);
            c.Content = g;
            paletteLayer.Children.Add(c);

            paletteBox.TextChanged += delegate
            {
                ph.Visibility = paletteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                FillPalette(0);
            };
            paletteBox.PreviewKeyDown += OnPaletteKey;
            return paletteLayer;
        }

        const string PaletteHelp = "↑ ↓ — выбор · Enter — выполнить · Ctrl+P — закрепить на главной · Esc — закрыть";

        void OpenPalette()
        {
            if (paletteLayer == null) return;
            if (!IsVisible) ShowFromTray();
            focusBeforePalette = Keyboard.FocusedElement;
            paletteBox.Text = "";
            FillPalette(0);
            paletteHint.Text = PaletteHelp;
            paletteLayer.Visibility = Visibility.Visible;
            paletteLayer.Opacity = 0;
            Theme.Animate(paletteLayer, UIElement.OpacityProperty, 1, 140);
            paletteBox.Focus();
            Keyboard.Focus(paletteBox);
        }

        void ClosePalette()
        {
            if (paletteLayer == null || paletteLayer.Visibility != Visibility.Visible) return;
            paletteLayer.BeginAnimation(UIElement.OpacityProperty, null);
            paletteLayer.Visibility = Visibility.Collapsed;
            if (focusBeforePalette != null) Keyboard.Focus(focusBeforePalette);
        }

        bool IsPinned(string id)
        {
            return Array.IndexOf(cfg.PinnedActions.Split(','), id) >= 0;
        }

        void TogglePin(AppCommand c)
        {
            if (c.Id.StartsWith("palette.") || c.Id == "app.exit") return;
            List<string> ids = new List<string>(cfg.PinnedActions.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            if (ids.Contains(c.Id)) ids.Remove(c.Id);
            else ids.Add(c.Id);
            cfg.PinnedActions = string.Join(",", ids);
            SaveSettings();
            RebuildQuickActions();
        }

        void FillPalette(int select)
        {
            paletteItems = AppCommand.Filter(commands, paletteBox.Text);
            paletteList.Children.Clear();
            paletteRows.Clear();
            if (paletteItems.Count == 0)
            {
                TextBlock none = Theme.T("Ничего не найдено", 13, "Text3", false);
                none.Margin = new Thickness(14, 10, 14, 10);
                paletteList.Children.Add(none);
            }
            string group = null;
            bool grouped = paletteBox.Text.Trim().Length == 0;
            for (int i = 0; i < paletteItems.Count; i++)
            {
                AppCommand c = paletteItems[i];
                if (grouped && c.Group != group)
                {
                    group = c.Group;
                    TextBlock gh = Theme.T(group.ToUpperInvariant(), 10.5, "Text3", true);
                    gh.Margin = new Thickness(12, i == 0 ? 2 : 10, 0, 4);
                    paletteList.Children.Add(gh);
                }
                paletteRows.Add(PaletteRow(c, i));
                paletteList.Children.Add(paletteRows[i]);
            }
            SelectPalette(select);
        }

        VButton PaletteRow(AppCommand c, int index)
        {
            VButton b = new VButton(null, null, 10);
            b.Height = 40;
            b.Focusable = false;
            string why = c.Reason();
            Grid g = new Grid();
            g.ColumnDefinitions.Add(Theme.Col(38));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            g.ColumnDefinitions.Add(Theme.Col(36));
            g.Children.Add(Theme.Icon(c.Glyph, 14, why == null ? "Accent" : "Text3"));
            StackPanel t = Theme.Row();
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Children.Add(Theme.T(c.CurrentTitle, 13.5, why == null ? "Text" : "Text3", false));
            TextBlock grp = Theme.T(why != null ? "  · недоступно: " + why : "  · " + c.Group, 11.5, "Text3", false);
            grp.VerticalAlignment = VerticalAlignment.Center;
            t.Children.Add(grp);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            if (c.Shortcut.Length > 0)
            {
                Border sc = Ui.Chip(c.Shortcut, "idle");
                sc.Margin = new Thickness(8, 0, 6, 0);
                Grid.SetColumn(sc, 2);
                g.Children.Add(sc);
            }
            if (!c.Id.StartsWith("palette.") && c.Id != "app.exit")
            {
                bool pinned = IsPinned(c.Id);
                VButton pin = new VButton(null, null, 8);
                pin.Width = 30; pin.Height = 30;
                pin.Focusable = false;
                pin.Content = Theme.Icon(pinned ? 0xE735 : 0xE734, 12, pinned ? "Accent" : "Text3");
                pin.ToolTip = pinned ? "Открепить с главной" : "Закрепить на главной (быстрые действия)";
                pin.Click += delegate { TogglePin(c); FillPalette(index); };
                Grid.SetColumn(pin, 3);
                g.Children.Add(pin);
            }
            b.Content = g;
            b.Click += delegate { RunFromPalette(index); };
            b.MouseEnter += delegate { if (paletteIndex != index) SelectPalette(index); };
            return b;
        }

        void SelectPalette(int i)
        {
            if (paletteRows.Count == 0) { paletteIndex = 0; return; }
            paletteIndex = Math.Max(0, Math.Min(paletteRows.Count - 1, i));
            for (int k = 0; k < paletteRows.Count; k++) paletteRows[k].SetKeys(k == paletteIndex ? "AccentSoft" : null, null);
            paletteRows[paletteIndex].BringIntoView();
        }

        void RunFromPalette(int i)
        {
            if (i < 0 || i >= paletteItems.Count) return;
            AppCommand c = paletteItems[i];
            string why = c.Reason();
            if (why != null)
            {
                paletteHint.Text = "«" + c.CurrentTitle + "» сейчас недоступна: " + why + ".";
                return;
            }
            ClosePalette();
            c.Run();
        }

        void OnPaletteKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: SelectPalette(paletteIndex + 1); e.Handled = true; break;
                case Key.Up: SelectPalette(paletteIndex - 1); e.Handled = true; break;
                case Key.PageDown: SelectPalette(paletteIndex + 8); e.Handled = true; break;
                case Key.PageUp: SelectPalette(paletteIndex - 8); e.Handled = true; break;
                case Key.Enter: RunFromPalette(paletteIndex); e.Handled = true; break;
                case Key.Escape: ClosePalette(); e.Handled = true; break;
                case Key.P:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && paletteIndex < paletteItems.Count)
                    {
                        AppCommand c = paletteItems[paletteIndex];
                        TogglePin(c);
                        int keep = paletteIndex;
                        FillPalette(keep);
                        paletteHint.Text = (IsPinned(c.Id) ? "Закреплено на главной: " : "Откреплено: ") + c.CurrentTitle;
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}
