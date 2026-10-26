using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Veylix
{
    // ===================== события сеанса =====================

    public class AppEvent
    {
        public DateTime Time, First;
        public string Kind, Level, Title, Detail, Page;
        public int Count = 1;
    }

    // Только реальные события этого сеанса; хранятся в памяти и никуда не отправляются.
    // Повтор того же события в течение минуты не создаёт новую запись, а увеличивает счётчик.
    public class EventStore
    {
        const int Capacity = 1000;
        public static readonly TimeSpan GroupWindow = TimeSpan.FromSeconds(60);
        readonly List<AppEvent> items = new List<AppEvent>();
        readonly object sync = new object();
        public event Action<AppEvent> Changed;

        public AppEvent Add(string kind, string level, string title, string detail, string page)
        {
            AppEvent e;
            lock (sync)
            {
                AppEvent last = null;
                for (int i = items.Count - 1; i >= 0 && i >= items.Count - 5; i--)
                    if (items[i].Kind == kind && items[i].Title == title) { last = items[i]; break; }
                if (last != null && DateTime.Now - last.Time < GroupWindow)
                {
                    last.Count++;
                    last.Time = DateTime.Now;
                    last.Detail = LogStore.Redact(detail ?? "");
                    e = last;
                }
                else
                {
                    e = new AppEvent();
                    e.Time = e.First = DateTime.Now;
                    e.Kind = kind; e.Level = level; e.Title = title; e.Detail = LogStore.Redact(detail ?? ""); e.Page = page;
                    items.Add(e);
                    if (items.Count > Capacity) items.RemoveRange(0, items.Count - Capacity);
                }
            }
            Action<AppEvent> h = Changed;
            if (h != null) h(e);
            return e;
        }

        public List<AppEvent> Snapshot()
        {
            lock (sync) { return new List<AppEvent>(items); }
        }

        public static string KindTitle(string k)
        {
            switch (k)
            {
                case "engine": return "Обход";
                case "strategy": return "Стратегия";
                case "connection": return "Соединение";
                case "check": return "Проверка";
                case "diag": return "Диагностика";
                case "update": return "Обновление";
                case "dns": return "DNS";
                case "error": return "Ошибка";
                default: return "Событие";
            }
        }

        public static int KindGlyph(string k)
        {
            switch (k)
            {
                case "engine": return 0xE7E8;
                case "strategy": return 0xE8FD;
                case "connection": return 0xE774;
                case "check": return 0xE72C;
                case "diag": return 0xE9D9;
                case "update": return 0xE895;
                case "dns": return 0xE72E;
                case "error": return 0xE783;
                default: return 0xE946;
            }
        }

        public static bool Important(AppEvent e)
        {
            return e.Level == "err" || e.Kind == "connection" || e.Kind == "update";
        }
    }

    // Статистика сеанса: всё считается из фактических событий, без оценок и процентов «надёжности»
    public class SessionStats
    {
        public readonly DateTime AppStart = DateTime.Now;
        TimeSpan engineTotal = TimeSpan.Zero;
        DateTime engineSince = DateTime.MinValue;
        public int EngineStarts, StrategyChanges, Errors, CheckRuns, ServiceChecks, ServiceChecksOk, StrategiesTested;

        public void EngineChanged(bool running)
        {
            if (running && engineSince == DateTime.MinValue) { engineSince = DateTime.Now; EngineStarts++; }
            else if (!running && engineSince != DateTime.MinValue) { engineTotal += DateTime.Now - engineSince; engineSince = DateTime.MinValue; }
        }

        public TimeSpan EngineUptime
        {
            get { return engineTotal + (engineSince != DateTime.MinValue ? DateTime.Now - engineSince : TimeSpan.Zero); }
        }

        public void CountServices(IEnumerable<ServiceResult> results)
        {
            foreach (ServiceResult r in results)
            {
                ServiceChecks++;
                if (r.Status == ServiceStatus.Ok) ServiceChecksOk++;
            }
        }
    }

    public partial class MainWindow
    {
        readonly EventStore events = new EventStore();
        readonly SessionStats session = new SessionStats();
        bool lastRunning;
        Strategy lastStrategy;

        // Вызывается из RefreshUi: фиксирует запуск/остановку движка и смену стратегии
        void TrackEngine()
        {
            bool run = engine.IsRunning;
            Strategy cur = engine.Current;
            if (run != lastRunning)
            {
                session.EngineChanged(run);
                if (!tuning && !labRunning)
                    events.Add("engine", run ? "ok" : "info", run ? "Обход запущен" : "Обход остановлен",
                        run ? "Стратегия " + (cur != null ? cur.Display : "?") : "Время работы за сеанс: " + Duration(session.EngineUptime), "home");
            }
            if (run && cur != null && lastStrategy != null && !object.ReferenceEquals(cur, lastStrategy))
            {
                session.StrategyChanges++;
                if (!tuning && !labRunning) events.Add("strategy", "info", "Стратегия: " + cur.Display, "Предыдущая: " + lastStrategy.Display, "strategies");
            }
            if (run && cur != null) lastStrategy = cur;
            lastRunning = run;
        }

        // Единая точка обновления состояния сервиса: схема, переходы «потеря/восстановление», уведомления
        void UpdateNet(string name, ServiceStatus status, int latency, string detail, string source)
        {
            ServiceSnapshot s = SnapOf(name);
            ServiceStatus prev = s.Status;
            s.Status = status;
            s.LatencyMs = latency;
            s.When = DateTime.Now;
            s.Detail = detail ?? "";
            s.Source = source;
            bool up = status == ServiceStatus.Ok || status == ServiceStatus.Partial;
            if (up) s.LastOk = DateTime.Now;
            bool wasUp = prev == ServiceStatus.Ok || prev == ServiceStatus.Partial;
            bool wasDown = prev == ServiceStatus.Unavailable || prev == ServiceStatus.NoNetwork;
            bool inetDown = name != "Интернет" && SnapOf("Интернет").Status == ServiceStatus.Unavailable;
            if (wasUp && status == ServiceStatus.Unavailable && !inetDown)
            {
                events.Add("connection", "err", name + ": соединение потеряно", detail, "diag");
                Toast("net-" + name, "err", name + ": соединение потеряно", detail, "diag", true);
            }
            else if (wasDown && up)
            {
                restoredUntil = DateTime.Now.AddSeconds(8);
                restoredWhat = name;
                events.Add("connection", "ok", name + ": соединение восстановлено", detail, "diag");
                Toast("net-" + name, "ok", name + ": соединение восстановлено", detail, "diag", false);
            }
            RefreshMap();
            UpdateCompanion();
        }

        void ApplyCheckResults(ServiceResult[] r, string source)
        {
            session.CheckRuns++;
            session.CountServices(r);
            foreach (ServiceResult s in r)
            {
                int sum = 0, n = 0;
                foreach (ProbeResult p in s.Probes)
                    if (p.Passed && p.TlsMs >= 0 && p.TcpMs >= 0) { sum += p.TcpMs + p.TlsMs; n++; }
                UpdateNet(s.Name, s.Status, n > 0 ? sum / n : -1, s.Summary, source);
            }
        }

        // ====================== уведомления ======================

        StackPanel toastHost;

        class ToastItem
        {
            public string Key;
            public Card Card;
            public TextBlock Title, Text;
            public DispatcherTimer Timer;
            public int Count = 1;
            public string BaseTitle;
        }
        readonly List<ToastItem> toasts = new List<ToastItem>();

        FrameworkElement BuildToasts()
        {
            toastHost = new StackPanel();
            toastHost.Width = 340;
            toastHost.HorizontalAlignment = HorizontalAlignment.Right;
            toastHost.VerticalAlignment = VerticalAlignment.Bottom;
            toastHost.Margin = new Thickness(0, 0, 24, 22);
            return toastHost;
        }

        void ToggleToasts()
        {
            cfg.Toasts = !cfg.Toasts;
            SaveSettings();
            RefreshSettingsState();
        }

        // important = true — показывается всегда (критические ошибки, потеря соединения);
        // остальные можно отключить в настройках. Повтор с тем же ключом объединяется в одно уведомление.
        void Toast(string key, string level, string title, string text, string page, bool important)
        {
            if (toastHost == null) return;
            if (!important && !cfg.Toasts) return;
            if (!IsVisible)
            {
                if (important) TrayBalloon(title, text ?? "", level == "err" ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
                return;
            }
            foreach (ToastItem t in toasts)
            {
                if (t.Key != key) continue;
                t.Count++;
                t.BaseTitle = title;
                t.Title.Text = title + "  ×" + t.Count;
                t.Text.Text = text ?? "";
                t.Timer.Stop();
                t.Timer.Start();
                return;
            }
            ToastItem it = new ToastItem();
            it.Key = key;
            it.BaseTitle = title;
            it.Card = new Card(14, "Popup", "StrokeStrong");
            it.Card.Margin = new Thickness(0, 10, 0, 0);
            it.Card.Inner = new Thickness(14, 12, 10, 12);
            it.Card.Cursor = page != null ? Cursors.Hand : Cursors.Arrow;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(Theme.Col(40));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(30));
            Border ic = Ui.StatusIcon(30);
            Ui.SetStatus(ic, level);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(ic);
            StackPanel sp = new StackPanel();
            it.Title = Theme.T(title, 13, "Text", true);
            it.Title.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(it.Title);
            it.Text = Theme.Wrap(text ?? "", 12, "Text2");
            it.Text.MaxHeight = 52;
            it.Text.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(it.Text);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            VButton close = Ui.IconButton(0xE711, "Закрыть");
            close.Width = 26; close.Height = 26;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Click += delegate { CloseToast(it); };
            Grid.SetColumn(close, 2);
            g.Children.Add(close);
            it.Card.Content = g;
            if (page != null)
                it.Card.MouseLeftButtonUp += delegate { ShowPage(page); CloseToast(it); };
            it.Timer = new DispatcherTimer();
            it.Timer.Interval = TimeSpan.FromSeconds(important ? 12 : 7);
            it.Timer.Tick += delegate { CloseToast(it); };
            it.Card.MouseEnter += delegate { it.Timer.Stop(); };
            it.Card.MouseLeave += delegate { it.Timer.Start(); };
            toasts.Add(it);
            toastHost.Children.Add(it.Card);
            while (toasts.Count > 3) CloseToast(toasts[0]);
            TranslateTransform tt = new TranslateTransform(24, 0);
            it.Card.RenderTransform = tt;
            it.Card.Opacity = 0;
            Theme.Animate(it.Card, UIElement.OpacityProperty, 1, 180);
            Theme.Animate(tt, TranslateTransform.XProperty, 0, 220);
            it.Timer.Start();
        }

        void CloseToast(ToastItem it)
        {
            it.Timer.Stop();
            toasts.Remove(it);
            toastHost.Children.Remove(it.Card);
        }

        // ====================== текущие операции (шапка) ======================

        StackPanel activityPanel;
        string activityKey = "";

        void RefreshActivity()
        {
            if (activityPanel == null) return;
            List<string[]> ops = new List<string[]>();   // текст, команда отмены (или "")
            if (tuning) ops.Add(new string[] { "Автоподбор" + (tuneStatus != null && tuneStatus.Text.Length > 0 ? ": " + tuneStatus.Text.TrimEnd('…', '.') : ""), "tune" });
            else if (labRunning) ops.Add(new string[] { "Тест стратегии…", "" });
            else if (busy) ops.Add(new string[] { "Применяю…", "" });
            if (checking) ops.Add(new string[] { "Проверка сервисов…", "" });
            if (diagRunning) ops.Add(new string[] { "Диагностика…", "" });
            if (updating) ops.Add(new string[] { "Обновление Flowseal…", "" });
            if (pulseMon.Running)
                ops.Add(new string[] { pulseMon.Measuring ? "Pulse: измеряю" : "Pulse: через " + Math.Max(0, (int)(pulseMon.NextAt - DateTime.Now).TotalSeconds) + " с", "pulse" });
            string key = "";
            foreach (string[] o in ops) key += o[0] + "|" + o[1] + ";";
            if (key == activityKey) return;
            activityKey = key;
            activityPanel.Children.Clear();
            foreach (string[] o in ops)
            {
                Border chip = new Border();
                chip.CornerRadius = new CornerRadius(9);
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.Padding = new Thickness(10, 0, o[1].Length > 0 ? 2 : 10, 0);
                chip.Height = 28;
                chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                StackPanel sp = Theme.Row();
                sp.VerticalAlignment = VerticalAlignment.Center;
                Ellipse dot = new Ellipse();
                dot.Width = 6; dot.Height = 6;
                dot.Margin = new Thickness(0, 0, 7, 0);
                dot.SetResourceReference(Shape.FillProperty, "Accent");
                sp.Children.Add(dot);
                TextBlock t = Theme.T(o[0], 12, "Accent", true);
                t.MaxWidth = 230;
                t.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(t);
                if (o[1].Length > 0)
                {
                    string what = o[1];
                    VButton x = new VButton(null, null, 7);
                    x.Width = 22; x.Height = 22;
                    x.Margin = new Thickness(4, 0, 0, 0);
                    x.Content = Theme.Icon(0xE711, 9, "Accent");
                    x.ToolTip = what == "tune" ? "Остановить автоподбор" : "Остановить Network Pulse";
                    x.Click += delegate { if (what == "tune") { if (tuning) OnTune(); } else if (pulseMon.Running) TogglePulse(); };
                    sp.Children.Add(x);
                }
                chip.Child = sp;
                chip.ToolTip = o[0];
                activityPanel.Children.Add(chip);
            }
        }

        // ====================== страница «События» ======================

        StackPanel eventList;
        UniformGrid statGrid;
        readonly Dictionary<string, TextBlock[]> statCells = new Dictionary<string, TextBlock[]>();
        VButton[] eventSeg = new VButton[8];
        static readonly string[] EventFilters = { "all", "important", "engine", "strategy", "connection", "check", "error", "update" };
        string eventFilter = "all";
        readonly HashSet<AppEvent> expanded = new HashSet<AppEvent>();

        FrameworkElement BuildEvents()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(14));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(12));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Card st = new Card(22);
            st.Inner = new Thickness(20, 16, 20, 14);
            StackPanel sp = new StackPanel();
            sp.Children.Add(Theme.T("Статистика сеанса", 15, "Text", true));
            statGrid = new UniformGrid();
            statGrid.Columns = 6;
            statGrid.Margin = new Thickness(0, 10, 0, 0);
            AddStat("app", "Veylix работает");
            AddStat("engine", "Обход работал");
            AddStat("checks", "Проверок сервисов");
            AddStat("pulse", "Измерений Pulse");
            AddStat("strategy", "Запусков / смен");
            AddStat("errors", "Ошибок в журнале");
            sp.Children.Add(statGrid);
            st.Content = sp;
            g.Children.Add(st);

            Border seg = Ui.Segmented(new string[] { "Все", "Важные", "Обход", "Стратегии", "Соединение", "Проверки", "Ошибки", "Обновления" }, eventSeg, delegate(int i)
            {
                eventFilter = EventFilters[i];
                Ui.SelectSegment(eventSeg, i);
                RefreshEvents();
            });
            Ui.SelectSegment(eventSeg, 0);
            Grid.SetRow(seg, 2);
            g.Children.Add(seg);

            Card lc = new Card(20);
            lc.Inner = new Thickness(6, 6, 6, 6);
            eventList = new StackPanel();
            lc.Content = Theme.Scroll(eventList);
            Grid.SetRow(lc, 4);
            g.Children.Add(lc);
            events.Changed += delegate { Ui_(delegate { if (page == "events") RefreshEvents(); }); };
            return g;
        }

        void AddStat(string id, string caption)
        {
            StackPanel s = new StackPanel();
            s.Margin = new Thickness(0, 0, 10, 0);
            TextBlock v = Theme.T("—", 19, "Text", true);
            TextBlock sub = Theme.T("", 11, "Text3", false);
            TextBlock c = Theme.T(caption, 11.5, "Text2", false);
            s.Children.Add(v);
            s.Children.Add(c);
            s.Children.Add(sub);
            statGrid.Children.Add(s);
            statCells[id] = new TextBlock[] { v, sub };
        }

        void SetStat(string id, string value, string sub)
        {
            TextBlock[] t;
            if (!statCells.TryGetValue(id, out t)) return;
            t[0].Text = value;
            t[1].Text = sub;
        }

        void RefreshStats()
        {
            if (statGrid == null) return;
            SetStat("app", Duration(DateTime.Now - session.AppStart), "с " + session.AppStart.ToString("HH:mm"));
            SetStat("engine", Duration(session.EngineUptime), engine.IsRunning ? "сейчас включён" : "сейчас выключен");
            SetStat("checks", session.ServiceChecks.ToString(), session.ServiceChecks > 0 ? "доступно: " + session.ServiceChecksOk + " · запусков: " + session.CheckRuns : "ещё не было");
            SetStat("pulse", pulseMon.TotalSamples.ToString(), pulseMon.TotalSamples > 0 ? "успешных: " + pulseMon.OkSamples : (pulseMon.Running ? "мониторинг идёт" : "мониторинг выключен"));
            SetStat("strategy", session.EngineStarts + " / " + session.StrategyChanges, session.StrategiesTested > 0 ? "протестировано: " + session.StrategiesTested : "");
            SetStat("errors", session.Errors.ToString(), session.Errors > 0 ? "подробности — фильтр «Ошибки»" : "");
        }

        bool EventMatches(AppEvent e)
        {
            if (eventFilter == "all") return true;
            if (eventFilter == "important") return EventStore.Important(e);
            if (eventFilter == "check") return e.Kind == "check" || e.Kind == "diag";
            return e.Kind == eventFilter;
        }

        void RefreshEvents()
        {
            RefreshStats();
            if (eventList == null) return;
            eventList.Children.Clear();
            List<AppEvent> all = events.Snapshot();
            int shown = 0;
            for (int i = all.Count - 1; i >= 0 && shown < 300; i--)
            {
                if (!EventMatches(all[i])) continue;
                eventList.Children.Add(EventRow(all[i]));
                shown++;
            }
            if (shown == 0)
            {
                TextBlock none = Theme.Wrap(all.Count == 0 ? "Событий пока нет. Здесь появятся запуск и остановка обхода, смена стратегии, потеря и восстановление соединения, проверки, обновления и ошибки — только то, что произошло в этом сеансе."
                    : "Нет событий этого типа.", 12.5, "Text3");
                none.Margin = new Thickness(14, 12, 14, 12);
                eventList.Children.Add(none);
            }
        }

        FrameworkElement EventRow(AppEvent e)
        {
            VButton b = new VButton(null, null, 12);
            Grid g = new Grid();
            g.Margin = new Thickness(10, 8 * Theme.Density, 10, 8 * Theme.Density);
            g.ColumnDefinitions.Add(Theme.Col(70));
            g.ColumnDefinitions.Add(Theme.Col(42));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            TextBlock tm = Theme.T(e.Time.ToString("HH:mm:ss"), 12, "Text3", false);
            tm.FontFamily = Theme.Mono;
            tm.Margin = new Thickness(0, 6, 0, 0);
            tm.ToolTip = e.Count > 1 ? "Впервые: " + e.First.ToString("HH:mm:ss") + ", последний раз: " + e.Time.ToString("HH:mm:ss") : e.Time.ToString("dd.MM.yyyy HH:mm:ss");
            g.Children.Add(tm);
            Border ic = new Border();
            ic.Width = 30; ic.Height = 30;
            ic.CornerRadius = new CornerRadius(10);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.SetResourceReference(Border.BackgroundProperty, Ui.SoftKey(e.Level));
            ic.Child = Theme.Icon(EventStore.KindGlyph(e.Kind), 13, Ui.FgKey(e.Level == "info" ? "accent" : e.Level));
            Grid.SetColumn(ic, 1);
            g.Children.Add(ic);
            StackPanel sp = new StackPanel();
            StackPanel tl = Theme.Row();
            tl.Children.Add(Theme.T(e.Title, 13.5, "Text", true));
            TextBlock kind = Theme.T("  · " + EventStore.KindTitle(e.Kind), 11.5, "Text3", false);
            kind.VerticalAlignment = VerticalAlignment.Center;
            tl.Children.Add(kind);
            sp.Children.Add(tl);
            bool open = expanded.Contains(e);
            if (!string.IsNullOrEmpty(e.Detail))
            {
                TextBlock d = Theme.T(e.Detail, 12, "Text2", false);
                if (open) { d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None; }
                d.Margin = new Thickness(0, 2, 0, 0);
                sp.Children.Add(d);
            }
            Grid.SetColumn(sp, 2);
            g.Children.Add(sp);
            StackPanel right = Theme.Row();
            right.VerticalAlignment = VerticalAlignment.Top;
            if (e.Count > 1)
            {
                Border cnt = Ui.Chip("×" + e.Count, e.Level == "err" ? "err" : "idle");
                cnt.Margin = new Thickness(8, 2, 4, 0);
                right.Children.Add(cnt);
            }
            if (e.Level == "err" || e.Level == "warn")
            {
                string anchor = e.Kind == "connection" ? "svc:" + e.Title.Split(':')[0]
                    : e.Kind == "strategy" ? "tune" : e.Kind == "update" ? "update" : "exit";
                right.Children.Add(FixButton(anchor));
            }
            if (e.Page != null)
            {
                VButton go = LinkButton("Открыть");
                string pg = e.Page;
                go.ToolTip = "Перейти в раздел";
                go.Click += delegate { ShowPage(pg); };
                right.Children.Add(go);
            }
            Grid.SetColumn(right, 3);
            g.Children.Add(right);
            b.Content = g;
            b.ToolTip = string.IsNullOrEmpty(e.Detail) ? null : "Щелчок — показать подробности целиком";
            b.Click += delegate
            {
                if (expanded.Contains(e)) expanded.Remove(e); else expanded.Add(e);
                RefreshEvents();
            };
            return b;
        }
    }
}
