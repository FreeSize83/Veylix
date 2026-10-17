using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Veylix
{
    // ===================== Network Pulse: логика =====================

    public class PulseSample
    {
        public DateTime Time;
        public int Ms = -1;             // время установки TCP+TLS-соединения; -1 — соединение не установлено
        public ProbeStatus Status;
        public string Stage = "", Detail = "";
        public bool Ok { get { return Status == ProbeStatus.Ok; } }
    }

    public class PulseSeries
    {
        public string Id, Name;
        public Target Target;
        public readonly List<PulseSample> Samples = new List<PulseSample>();
        public DateTime LastOk = DateTime.MinValue;
        public int Fails;   // неудач подряд

        public PulseSample Last { get { return Samples.Count > 0 ? Samples[Samples.Count - 1] : null; } }

        public void Add(PulseSample s, int limit)
        {
            Samples.Add(s);
            if (Samples.Count > limit) Samples.RemoveRange(0, Samples.Count - limit);
            if (s.Ok) { LastOk = s.Time; Fails = 0; }
            else Fails++;
        }

        public void Trim(int limit)
        {
            if (Samples.Count > limit) Samples.RemoveRange(0, Samples.Count - limit);
        }

        // Изменение к предыдущему измерению; null, если одно из двух последних не удалось
        public int? Delta()
        {
            if (Samples.Count < 2) return null;
            PulseSample a = Samples[Samples.Count - 2], b = Samples[Samples.Count - 1];
            if (a.Ms < 0 || b.Ms < 0) return null;
            return b.Ms - a.Ms;
        }

        public void Stats(out int min, out int avg, out int max, out int okCount)
        {
            min = int.MaxValue; max = 0; okCount = 0;
            long sum = 0;
            foreach (PulseSample s in Samples)
            {
                if (s.Ms < 0) continue;
                okCount++;
                sum += s.Ms;
                if (s.Ms < min) min = s.Ms;
                if (s.Ms > max) max = s.Ms;
            }
            avg = okCount > 0 ? (int)(sum / okCount) : -1;
            if (okCount == 0) { min = -1; max = -1; }
        }
    }

    public class PulseMonitor
    {
        public static readonly string[] Ids = { "internet", "discord", "youtube", "google" };
        public const int MinInterval = 30;   // чаще не измеряем — это лишняя нагрузка на сеть

        public readonly Dictionary<string, PulseSeries> All = new Dictionary<string, PulseSeries>();
        public readonly List<string> Enabled = new List<string>();
        public int IntervalSec = 60, HistoryLimit = 60;
        public bool Running { get; private set; }
        public bool Measuring { get; private set; }
        public DateTime NextAt = DateTime.MinValue, LastRun = DateTime.MinValue;
        public int TotalSamples, OkSamples;

        public event Action<PulseSeries, PulseSample> Sampled;   // вызывается из фонового потока
        public event Action Changed;
        CancellationTokenSource cts;

        public PulseMonitor()
        {
            foreach (string id in Ids)
            {
                PulseSeries s = new PulseSeries();
                s.Id = id;
                s.Name = NameOf(id);
                s.Target = TargetOf(id);
                All[id] = s;
            }
        }

        public static string NameOf(string id)
        {
            switch (id)
            {
                case "internet": return "Интернет";
                case "discord": return "Discord";
                case "youtube": return "YouTube";
                default: return "Google";
            }
        }

        // Имя сервиса в общих проверках (для схемы сети); у Google отдельной проверки нет
        public static string ServiceOf(string id)
        {
            return id == "google" ? null : NameOf(id);
        }

        public static Target TargetOf(string id)
        {
            switch (id)
            {
                case "internet": return new Target("ya.ru", "/", true);
                case "discord": return new Target("discord.com", "/", true);
                case "youtube": return new Target("www.youtube.com", "/", true);
                default: return new Target("www.google.com", "/", true);
            }
        }

        public static int ClampInterval(int sec)
        {
            return Math.Max(MinInterval, sec);
        }

        public void SetEnabled(string csv)
        {
            Enabled.Clear();
            foreach (string id in (csv ?? "").Split(','))
                if (All.ContainsKey(id.Trim()) && !Enabled.Contains(id.Trim())) Enabled.Add(id.Trim());
        }

        public string EnabledCsv()
        {
            return string.Join(",", Enabled);
        }

        void Raise()
        {
            Action h = Changed;
            if (h != null) h();
        }

        public void Start()
        {
            if (Running) return;
            Running = true;
            cts = new CancellationTokenSource();
            Loop(cts.Token);
            Raise();
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            if (cts != null) cts.Cancel();
            NextAt = DateTime.MinValue;
            Raise();
        }

        // Смена интервала на ходу: следующий замер отсчитывается от последнего, без внеочередного запроса
        public void Restart()
        {
            if (!Running) return;
            if (cts != null) cts.Cancel();
            cts = new CancellationTokenSource();
            Loop(cts.Token);
        }

        async void Loop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TimeSpan wait = LastRun + TimeSpan.FromSeconds(ClampInterval(IntervalSec)) - DateTime.Now;
                    if (wait > TimeSpan.Zero)
                    {
                        NextAt = DateTime.Now + wait;
                        Raise();
                        await Task.Delay(wait, ct);
                    }
                    if (ct.IsCancellationRequested) break;
                    await MeasureOnce();
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception) { }
        }

        public async Task MeasureOnce()
        {
            if (Measuring) return;
            Measuring = true;
            LastRun = DateTime.Now;
            Raise();
            try
            {
                List<Task> tasks = new List<Task>();
                foreach (string id in Enabled.ToArray())
                {
                    PulseSeries s = All[id];
                    tasks.Add(MeasureSeries(s));
                }
                await Task.WhenAll(tasks);
            }
            finally
            {
                Measuring = false;
                NextAt = Running ? DateTime.Now + TimeSpan.FromSeconds(ClampInterval(IntervalSec)) : DateTime.MinValue;
                Raise();
            }
        }

        async Task MeasureSeries(PulseSeries s)
        {
            ProbeResult r = await NetCheck.Probe(s.Target, true).ConfigureAwait(false);
            PulseSample x = FromProbe(r);
            lock (s) { s.Add(x, HistoryLimit); }
            Interlocked.Increment(ref TotalSamples);
            if (x.Ok) Interlocked.Increment(ref OkSamples);
            Action<PulseSeries, PulseSample> h = Sampled;
            if (h != null) h(s, x);
        }

        public static PulseSample FromProbe(ProbeResult r)
        {
            PulseSample x = new PulseSample();
            x.Time = DateTime.Now;
            x.Status = r.Status;
            x.Stage = r.Stage;
            x.Detail = r.Detail;
            // измеряем только то, что действительно измерено: TCP + TLS рукопожатие
            if (r.TcpMs >= 0 && r.TlsMs >= 0) x.Ms = r.TcpMs + r.TlsMs;
            else if (r.TcpMs >= 0 && !r.Target.Tls) x.Ms = r.TcpMs;
            return x;
        }
    }

    // ===================== Network Pulse: страница =====================

    public partial class MainWindow
    {
        readonly PulseMonitor pulseMon = new PulseMonitor();
        TextBlock pulseStatus;
        VButton pulseBtn;
        VButton[] pulseIntSeg = new VButton[4], pulseHistSeg = new VButton[3];
        WrapPanel pulseTargetsPanel;
        StackPanel pulseList;
        readonly Dictionary<string, VButton> pulseTargetBtns = new Dictionary<string, VButton>();

        class PulseCard
        {
            public Card Card;
            public TextBlock Value, Delta, Last, Stats;
            public Border Chip;
            public Canvas Chart;
        }
        readonly Dictionary<string, PulseCard> pulseCards = new Dictionary<string, PulseCard>();

        void InitPulse()
        {
            pulseMon.IntervalSec = cfg.PulseInterval;
            pulseMon.HistoryLimit = cfg.PulseHistory;
            pulseMon.SetEnabled(cfg.PulseTargets);
            if (pulseMon.Enabled.Count == 0) pulseMon.SetEnabled("internet,discord,youtube");
            pulseMon.Changed += delegate { Ui_(RefreshPulseHeader); };
            pulseMon.Sampled += delegate(PulseSeries s, PulseSample x) { Ui_(delegate { OnPulseSample(s, x); }); };
        }

        void TogglePulse()
        {
            if (pulseMon.Running)
            {
                pulseMon.Stop();
                events.Add("check", "info", "Network Pulse остановлен", "Измерений за сеанс: " + pulseMon.TotalSamples + ".", "pulse");
            }
            else
            {
                pulseMon.Start();
                events.Add("check", "info", "Network Pulse запущен",
                    "Интервал " + IntervalText(pulseMon.IntervalSec) + ", адреса: " + TargetsText() + ".", "pulse");
            }
            RefreshPulseHeader();
        }

        static string IntervalText(int sec)
        {
            return sec < 60 ? sec + " с" : (sec / 60) + " мин";
        }

        string TargetsText()
        {
            List<string> n = new List<string>();
            foreach (string id in pulseMon.Enabled) n.Add(pulseMon.All[id].Target.Host);
            return string.Join(", ", n);
        }

        void OnPulseSample(PulseSeries s, PulseSample x)
        {
            DrawPulseCard(s);
            string svc = PulseMonitor.ServiceOf(s.Id);
            if (svc == null) return;
            // одна неудача — ещё не «недоступен»: подтверждаем двумя неудачами подряд
            if (x.Ok) UpdateNet(svc, ServiceStatus.Ok, x.Ms, "Network Pulse: " + s.Target.Host + " — " + x.Ms + " мс (TCP+TLS)", "pulse");
            else if (s.Fails >= 2) UpdateNet(svc, ServiceStatus.Unavailable, -1, "Network Pulse: " + s.Target.Host + " — " + x.Detail + " (2 раза подряд)", "pulse");
        }

        FrameworkElement BuildPulse()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(16));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Card c = new Card(22);
            c.Inner = new Thickness(22, 18, 22, 16);
            StackPanel sp = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel h = Ui.Heading("Network Pulse",
                "Раз в заданный интервал измеряет время установки TCP+TLS-соединения с контрольными адресами. " +
                "Это не ICMP ping: в значение входят TCP-рукопожатие и TLS, путь проходит через обход. Запускается только вручную.");
            h.Margin = new Thickness(0, 0, 24, 0);
            head.Children.Add(h);
            StackPanel btns = Theme.Row();
            btns.VerticalAlignment = VerticalAlignment.Top;
            VButton once = Ui.Button("Измерить сейчас", 0xE72C, false);
            once.Margin = new Thickness(0, 0, 8, 0);
            once.Click += async delegate
            {
                if (pulseMon.Measuring) return;
                if ((DateTime.Now - pulseMon.LastRun).TotalSeconds < 10) { pulseStatus.Text = "Слишком часто: подождите несколько секунд между измерениями."; return; }
                await pulseMon.MeasureOnce();
            };
            pulseBtn = Ui.Button("Запустить", 0xE768, true);
            pulseBtn.Click += delegate { TogglePulse(); };
            btns.Children.Add(once);
            btns.Children.Add(pulseBtn);
            Grid.SetColumn(btns, 1);
            head.Children.Add(btns);
            sp.Children.Add(head);

            Grid opts = new Grid();
            opts.Margin = new Thickness(0, 14, 0, 0);
            opts.ColumnDefinitions.Add(Theme.Col(-1));
            opts.ColumnDefinitions.Add(Theme.Col(24));
            opts.ColumnDefinitions.Add(Theme.Col(-1));
            opts.ColumnDefinitions.Add(Theme.Col(0));
            StackPanel o1 = new StackPanel();
            o1.Children.Add(Theme.T("Интервал", 11.5, "Text3", true));
            Border iseg = Ui.Segmented(new string[] { "30 с", "1 мин", "2 мин", "5 мин" }, pulseIntSeg, delegate(int i)
            {
                cfg.PulseInterval = Settings.PulseIntervals[i];
                pulseMon.IntervalSec = cfg.PulseInterval;
                SaveSettings();
                pulseMon.Restart();
                RefreshPulseHeader();
            });
            iseg.Margin = new Thickness(0, 4, 0, 0);
            o1.Children.Add(iseg);
            opts.Children.Add(o1);
            StackPanel o2 = new StackPanel();
            o2.Children.Add(Theme.T("Хранить измерений", 11.5, "Text3", true));
            Border hseg = Ui.Segmented(new string[] { "30", "60", "120" }, pulseHistSeg, delegate(int i)
            {
                cfg.PulseHistory = Settings.PulseHistories[i];
                pulseMon.HistoryLimit = cfg.PulseHistory;
                foreach (PulseSeries s in pulseMon.All.Values) lock (s) { s.Trim(cfg.PulseHistory); }
                SaveSettings();
                RefreshPulseHeader();
                foreach (PulseSeries s in pulseMon.All.Values) DrawPulseCard(s);
            });
            hseg.Margin = new Thickness(0, 4, 0, 0);
            o2.Children.Add(hseg);
            Grid.SetColumn(o2, 2);
            opts.Children.Add(o2);
            pulseStatus = Theme.Wrap("", 12, "Text2");
            pulseStatus.VerticalAlignment = VerticalAlignment.Bottom;
            pulseStatus.TextAlignment = TextAlignment.Right;
            pulseStatus.Margin = new Thickness(16, 0, 0, 6);
            Grid.SetColumn(pulseStatus, 3);
            opts.Children.Add(pulseStatus);
            sp.Children.Add(opts);

            TextBlock tl = Theme.T("Контрольные адреса", 11.5, "Text3", true);
            tl.Margin = new Thickness(0, 12, 0, 4);
            sp.Children.Add(tl);
            pulseTargetsPanel = new WrapPanel();
            foreach (string id in PulseMonitor.Ids)
            {
                string tid = id;
                VButton b = new VButton(null, "Stroke", 9);
                b.Height = 30;
                b.Margin = new Thickness(0, 0, 8, 0);
                TextBlock t = Theme.T(PulseMonitor.NameOf(id) + " · " + PulseMonitor.TargetOf(id).Host, 12.5, "Text", true);
                t.Margin = new Thickness(12, 0, 12, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                b.Content = t;
                b.Click += delegate { TogglePulseTarget(tid); };
                pulseTargetBtns[id] = b;
                pulseTargetsPanel.Children.Add(b);
            }
            sp.Children.Add(pulseTargetsPanel);
            c.Content = sp;
            g.Children.Add(c);

            pulseList = new StackPanel();
            pulseList.Margin = new Thickness(0, 0, 6, 0);
            ScrollViewer sv = Theme.Scroll(pulseList);
            Grid.SetRow(sv, 2);
            g.Children.Add(sv);
            RebuildPulseCards();
            RefreshPulseHeader();
            return g;
        }

        void TogglePulseTarget(string id)
        {
            if (pulseMon.Enabled.Contains(id))
            {
                if (pulseMon.Enabled.Count == 1) { pulseStatus.Text = "Нужен хотя бы один контрольный адрес."; return; }
                pulseMon.Enabled.Remove(id);
            }
            else pulseMon.Enabled.Add(id);
            cfg.PulseTargets = pulseMon.EnabledCsv();
            SaveSettings();
            RebuildPulseCards();
            RefreshPulseHeader();
        }

        void RefreshPulseHeader()
        {
            if (pulseBtn == null) return;
            Ui.SetButtonText(pulseBtn, pulseMon.Running ? "Остановить" : "Запустить");
            Ui.SelectSegment(pulseIntSeg, Array.IndexOf(Settings.PulseIntervals, cfg.PulseInterval));
            Ui.SelectSegment(pulseHistSeg, Array.IndexOf(Settings.PulseHistories, cfg.PulseHistory));
            foreach (KeyValuePair<string, VButton> kv in pulseTargetBtns)
            {
                bool on = pulseMon.Enabled.Contains(kv.Key);
                kv.Value.SetKeys(on ? "AccentSoft" : null, on ? "Accent" : "Stroke");
            }
            UpdatePulseCountdown();
            RefreshActivity();
        }

        void UpdatePulseCountdown()
        {
            if (pulseStatus == null) return;
            if (pulseMon.Measuring) pulseStatus.Text = "Измеряю…";
            else if (pulseMon.Running && pulseMon.NextAt > DateTime.Now)
                pulseStatus.Text = "Работает · следующее измерение через " + Math.Max(1, (int)(pulseMon.NextAt - DateTime.Now).TotalSeconds) + " с";
            else if (pulseMon.Running) pulseStatus.Text = "Работает";
            else pulseStatus.Text = "Остановлен" + (pulseMon.TotalSamples > 0 ? " · измерений за сеанс: " + pulseMon.TotalSamples : "");
        }

        void RebuildPulseCards()
        {
            if (pulseList == null) return;
            pulseList.Children.Clear();
            pulseCards.Clear();
            foreach (string id in pulseMon.Enabled)
            {
                PulseSeries s = pulseMon.All[id];
                PulseCard pc = new PulseCard();
                pc.Card = new Card(20);
                pc.Card.Margin = new Thickness(0, 0, 0, Math.Round(12 * Theme.Density));
                pc.Card.Inner = new Thickness(20, 14, 20, 14);
                Grid gg = new Grid();
                gg.ColumnDefinitions.Add(Theme.Col(250));
                gg.ColumnDefinitions.Add(Theme.Col(0));
                StackPanel left = new StackPanel();
                StackPanel nm = Theme.Row();
                nm.Children.Add(Theme.T(s.Name, 15, "Text", true));
                pc.Chip = Ui.Chip("нет данных", "idle");
                pc.Chip.Margin = new Thickness(10, 0, 0, 0);
                nm.Children.Add(pc.Chip);
                left.Children.Add(nm);
                left.Children.Add(Theme.T(s.Target.Host + " · TCP+TLS", 11.5, "Text3", false));
                pc.Value = Theme.T("—", 24, "Text", true);
                pc.Value.Margin = new Thickness(0, 6, 0, 0);
                left.Children.Add(pc.Value);
                pc.Delta = Theme.T("", 12, "Text2", false);
                left.Children.Add(pc.Delta);
                pc.Last = Theme.T("", 11.5, "Text3", false);
                pc.Last.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(pc.Last);
                gg.Children.Add(left);
                StackPanel right = new StackPanel();
                pc.Chart = new Canvas();
                pc.Chart.Height = 86;
                pc.Chart.ClipToBounds = true;
                pc.Chart.Background = Brushes.Transparent;
                right.Children.Add(pc.Chart);
                pc.Stats = Theme.T("", 11.5, "Text3", false);
                pc.Stats.Margin = new Thickness(0, 4, 0, 0);
                right.Children.Add(pc.Stats);
                Grid.SetColumn(right, 1);
                gg.Children.Add(right);
                pc.Card.Content = gg;
                pc.Chart.SizeChanged += delegate { DrawPulseCard(s); };
                pulseCards[id] = pc;
                pulseList.Children.Add(pc.Card);
                DrawPulseCard(s);
            }
        }

        void DrawPulseCard(PulseSeries s)
        {
            PulseCard pc;
            if (!pulseCards.TryGetValue(s.Id, out pc)) return;
            List<PulseSample> samples;
            lock (s) { samples = new List<PulseSample>(s.Samples); }
            PulseSample last = samples.Count > 0 ? samples[samples.Count - 1] : null;
            if (last == null)
            {
                pc.Value.Text = "—";
                pc.Delta.Text = "Измерений ещё не было";
                pc.Last.Text = "";
                Ui.SetChip(pc.Chip, "нет данных", "idle");
            }
            else
            {
                pc.Value.Text = last.Ms >= 0 ? last.Ms + " мс" : "нет ответа";
                pc.Value.SetResourceReference(TextBlock.ForegroundProperty, last.Ok ? "Text" : "Err");
                int? d = s.Delta();
                if (d.HasValue)
                {
                    int v = d.Value;
                    pc.Delta.Text = v == 0 ? "= без изменений к прошлому замеру" : (v > 0 ? "▲ +" : "▼ ") + v + " мс к прошлому замеру";
                    pc.Delta.SetResourceReference(TextBlock.ForegroundProperty, v > 30 ? "Warn" : v < -10 ? "Ok" : "Text2");
                }
                else
                {
                    pc.Delta.Text = last.Ok ? "сравнивать пока не с чем" : "[" + last.Stage + "] " + last.Detail;
                    pc.Delta.SetResourceReference(TextBlock.ForegroundProperty, last.Ok ? "Text2" : "Err");
                }
                pc.Delta.ToolTip = last.Detail;
                pc.Last.Text = s.LastOk == DateTime.MinValue ? "Успешных измерений не было" : "Последний успех: " + s.LastOk.ToString("HH:mm:ss");
                string lvl = last.Ok ? "ok" : (s.Fails >= 2 ? "err" : "warn");
                Ui.SetChip(pc.Chip, last.Ok ? "отвечает" : (s.Fails >= 2 ? "нет ответа ×" + s.Fails : "сбой"), lvl);
            }
            int min, avg, max, ok;
            s.Stats(out min, out avg, out max, out ok);
            pc.Stats.Text = samples.Count == 0 ? "График появится после первых измерений"
                : "Измерений: " + samples.Count + " из " + pulseMon.HistoryLimit + " · успешных: " + ok +
                  (ok > 0 ? " · мин " + min + " / сред " + avg + " / макс " + max + " мс" : "");
            DrawChart(pc.Chart, samples, pulseMon.HistoryLimit);
        }

        // График истории: линия по успешным замерам, красные отметки — замеры без соединения
        static void DrawChart(Canvas c, List<PulseSample> samples, int slots)
        {
            c.Children.Clear();
            double w = c.ActualWidth, h = c.Height;
            if (w <= 0) return;
            Rectangle baseLine = new Rectangle();
            baseLine.Width = w;
            baseLine.Height = 1;
            baseLine.SetResourceReference(Shape.FillProperty, "Stroke");
            Canvas.SetTop(baseLine, h - 1);
            c.Children.Add(baseLine);
            if (samples.Count == 0) return;
            int max = 1;
            foreach (PulseSample s in samples) if (s.Ms > max) max = s.Ms;
            max = (int)(Math.Ceiling(max * 1.15 / 50.0) * 50);
            Rectangle mid = new Rectangle();
            mid.Width = w; mid.Height = 1; mid.Opacity = 0.5;
            mid.SetResourceReference(Shape.FillProperty, "Stroke");
            Canvas.SetTop(mid, (h - 4) / 2);
            c.Children.Add(mid);
            TextBlock lbl = Theme.T(max + " мс", 10, "Text3", false);
            c.Children.Add(lbl);
            TextBlock lbl2 = Theme.T(max / 2 + " мс", 10, "Text3", false);
            Canvas.SetTop(lbl2, (h - 4) / 2 - 13);
            c.Children.Add(lbl2);

            double usable = w - 8;   // отступ справа, чтобы последняя точка не обрезалась
            double step = slots > 1 ? usable / (slots - 1) : usable;
            double x0 = usable - step * (samples.Count - 1);
            Polyline line = null;
            for (int i = 0; i < samples.Count; i++)
            {
                double x = x0 + step * i;
                if (samples[i].Ms < 0)
                {
                    line = null;
                    Rectangle f = new Rectangle();
                    f.Width = 3; f.Height = 10;
                    f.RadiusX = 1.5; f.RadiusY = 1.5;
                    f.SetResourceReference(Shape.FillProperty, "Err");
                    Canvas.SetLeft(f, x - 1.5);
                    Canvas.SetTop(f, h - 11);
                    f.ToolTip = samples[i].Time.ToString("HH:mm:ss") + " — " + samples[i].Detail;
                    c.Children.Add(f);
                    continue;
                }
                double y = h - 3 - samples[i].Ms / (double)max * (h - 8);
                if (line == null)
                {
                    line = new Polyline();
                    line.StrokeThickness = 2;
                    line.StrokeLineJoin = PenLineJoin.Round;
                    line.SetResourceReference(Shape.StrokeProperty, "Accent");
                    c.Children.Add(line);
                }
                line.Points.Add(new Point(x, y));
                Ellipse d = new Ellipse();
                d.Width = 5; d.Height = 5;
                d.SetResourceReference(Shape.FillProperty, "Accent");
                Canvas.SetLeft(d, x - 2.5);
                Canvas.SetTop(d, y - 2.5);
                d.ToolTip = samples[i].Time.ToString("HH:mm:ss") + " — " + samples[i].Ms + " мс";
                c.Children.Add(d);
            }
        }
    }
}
