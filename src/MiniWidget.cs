using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Veylix
{
    // Мини-виджет поверх окон: состояние обхода, стратегия, задержка до Discord и YouTube и кнопка питания
    public static class WidgetLogic
    {
        public const double Width = 268;

        // Сохранённая позиция, если окно на ней видно хотя бы частично; иначе — правый нижний угол рабочей области
        public static Point Place(double x, double y, double w, double h, Rect screen, Rect work)
        {
            if (!double.IsNaN(x) && !double.IsNaN(y))
            {
                Rect r = new Rect(x, y, w, h);
                Rect vis = Rect.Intersect(r, screen);
                if (!vis.IsEmpty && vis.Width >= 60 && vis.Height >= 30) return new Point(x, y);
            }
            return new Point(work.Right - w - 16, work.Bottom - h - 16);
        }

        public static string Latency(ProbeResult p)
        {
            if (p == null) return "…";
            if (p.Passed || p.Status == ProbeStatus.Partial)
            {
                int ms = Math.Max(0, p.TcpMs) + Math.Max(0, p.TlsMs);
                return ms > 0 ? ms + " мс" : "есть";
            }
            return "нет";
        }

        public static string Level(ProbeResult p)
        {
            if (p == null) return "idle";
            if (p.Passed) return "ok";
            if (p.Status == ProbeStatus.Partial) return "warn";
            return "err";
        }
    }

    public partial class MainWindow
    {
        Window widget;
        Ellipse wDot;
        TextBlock wTitle, wSub, wDiscord, wYoutube;
        Ellipse wDiscordDot, wYoutubeDot;
        VButton wPower;
        TextBlock wPowerIcon;
        DispatcherTimer widgetTimer;
        bool widgetProbing;
        ProbeResult wLastDiscord, wLastYoutube;

        void ToggleWidget()
        {
            cfg.WidgetOn = widget == null;
            SaveSettings();
            if (cfg.WidgetOn) ShowWidget(); else CloseWidget();
            RefreshSettingsState();
        }

        void ShowWidget()
        {
            if (widget != null || selfTest) return;
            Window w = new Window();
            w.Title = "Veylix — мини-виджет";
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.ShowActivated = false;
            w.SizeToContent = SizeToContent.Height;
            w.Width = WidgetLogic.Width;
            w.FontFamily = Theme.Ui;
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            bool blur = Backdrop.BlurUnavailableReason(cfg.GlassReduced) == null;
            WindowChrome wc = new WindowChrome();
            wc.CaptionHeight = 0;
            wc.ResizeBorderThickness = new Thickness(0);
            wc.GlassFrameThickness = blur ? new Thickness(-1) : new Thickness(0, 0, 0, 1);
            wc.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(w, wc);
            w.SetResourceReference(Window.BackgroundProperty, "Popup");
            w.SourceInitialized += delegate
            {
                bool dark = Theme.Current != null && Theme.Current.Dark;
                if (Backdrop.Apply(w, blur, dark))
                {
                    SolidColorBrush pb = Theme.Res("Popup") as SolidColorBrush;
                    Color c = pb != null ? pb.Color : (dark ? Color.FromRgb(32, 32, 36) : Color.FromRgb(248, 248, 250));
                    w.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)0xB8 : (byte)0xC8, c.R, c.G, c.B));
                }
            };
            w.Content = BuildWidgetContent();
            w.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { ShowFromTray(); return; }
                try { w.DragMove(); } catch { }
                cfg.WidgetX = w.Left;
                cfg.WidgetY = w.Top;
                SaveSettings();
            };
            w.Loaded += delegate
            {
                Point p = WidgetLogic.Place(cfg.WidgetX, cfg.WidgetY, w.ActualWidth, w.ActualHeight,
                    new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight),
                    SystemParameters.WorkArea);
                w.Left = p.X;
                w.Top = p.Y;
            };
            widget = w;
            RefreshWidget();
            w.Show();

            widgetTimer = new DispatcherTimer();
            widgetTimer.Interval = TimeSpan.FromSeconds(20);
            widgetTimer.Tick += delegate { ProbeForWidget(); };
            widgetTimer.Start();
            ProbeForWidget();
        }

        void CloseWidget()
        {
            if (widgetTimer != null) { widgetTimer.Stop(); widgetTimer = null; }
            if (widget == null) return;
            Window w = widget;
            widget = null;
            try { w.Close(); } catch { }
        }

        FrameworkElement BuildWidgetContent()
        {
            Border frame = new Border();
            frame.BorderThickness = new Thickness(1);
            frame.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
            frame.Padding = new Thickness(14, 12, 10, 12);
            frame.Cursor = Cursors.SizeAll;
            frame.ToolTip = "Перетащите, чтобы переместить. Двойной щелчок — открыть Veylix.";

            StackPanel sp = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(Theme.Col(-1));
            top.ColumnDefinitions.Add(Theme.Col(0));
            top.ColumnDefinitions.Add(Theme.Col(-1));
            top.ColumnDefinitions.Add(Theme.Col(-1));
            wDot = new Ellipse();
            wDot.Width = 10; wDot.Height = 10;
            wDot.VerticalAlignment = VerticalAlignment.Top;
            wDot.Margin = new Thickness(0, 5, 10, 0);
            top.Children.Add(wDot);
            StackPanel tx = new StackPanel();
            wTitle = Theme.T("", 13.5, "Text", true);
            wSub = Theme.T("", 11.5, "Text2", false);
            wSub.TextTrimming = TextTrimming.CharacterEllipsis;
            tx.Children.Add(wTitle);
            tx.Children.Add(wSub);
            Grid.SetColumn(tx, 1);
            top.Children.Add(tx);

            VButton power = new VButton("Surface2", "Stroke", 15);
            power.Width = 30; power.Height = 30;
            power.Margin = new Thickness(8, 0, 2, 0);
            power.Cursor = Cursors.Hand;
            wPowerIcon = Theme.Icon(0xE7E8, 12, "Accent");
            power.Content = wPowerIcon;
            power.ToolTip = "Включить / выключить обход";
            power.Click += delegate { OnPower(); };
            wPower = power;
            Grid.SetColumn(power, 2);
            top.Children.Add(power);
            VButton close = Ui.IconButton(0xE8BB, "Скрыть мини-виджет (вернуть — в настройках или панели трея)");
            close.Width = 26; close.Height = 26;
            close.Cursor = Cursors.Hand;
            close.Click += delegate { ToggleWidget(); };
            Grid.SetColumn(close, 3);
            top.Children.Add(close);
            sp.Children.Add(top);

            Grid pills = new Grid();
            pills.Margin = new Thickness(0, 10, 4, 0);
            pills.ColumnDefinitions.Add(Theme.Col(0));
            pills.ColumnDefinitions.Add(Theme.Col(8));
            pills.ColumnDefinitions.Add(Theme.Col(0));
            pills.Children.Add(WidgetPill("Discord", out wDiscordDot, out wDiscord));
            FrameworkElement yt = WidgetPill("YouTube", out wYoutubeDot, out wYoutube);
            Grid.SetColumn(yt, 2);
            pills.Children.Add(yt);
            sp.Children.Add(pills);
            frame.Child = sp;
            return frame;
        }

        static Border WidgetPill(string name, out Ellipse dot, out TextBlock value)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(9, 5, 9, 5);
            b.SetResourceReference(Border.BackgroundProperty, "Surface2");
            Grid g = new Grid();
            g.ColumnDefinitions.Add(Theme.Col(-1));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            dot = new Ellipse();
            dot.Width = 7; dot.Height = 7;
            dot.Margin = new Thickness(0, 1, 7, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.SetResourceReference(Shape.FillProperty, "Text3");
            g.Children.Add(dot);
            TextBlock n = Theme.T(name, 11.5, "Text2", false);
            n.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(n, 1);
            g.Children.Add(n);
            value = Theme.T("…", 11.5, "Text", true);
            value.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(value, 2);
            g.Children.Add(value);
            b.Child = g;
            return b;
        }

        // Вызывается из RefreshUi и после замеров
        void RefreshWidget()
        {
            if (wTitle == null) return;
            bool run = engine.IsRunning;
            string ts = CurrentTrayState();
            string name = engine.Current != null ? engine.Current.Display : (selected != null ? selected.Display : "—");
            string title, dotKey;
            switch (ts)
            {
                case "run": title = "Обход работает"; dotKey = "Ok"; break;
                case "busy": title = tuning ? "Автоподбор…" : "Подождите…"; dotKey = "Warn"; break;
                case "pause": title = "Пауза для игры"; dotKey = "Warn"; break;
                case "err": title = "Обход остановился"; dotKey = "Err"; break;
                default: title = "Обход выключен"; dotKey = "Text3"; break;
            }
            wTitle.Text = title;
            wDot.SetResourceReference(Shape.FillProperty, dotKey);
            string sub = ts == "pause" && gamePausedFor != null ? GameWatch.Pretty(gamePausedFor) + " · " + name : name;
            if (run && engine.StartedAt != DateTime.MinValue)
            {
                TimeSpan up = DateTime.Now - engine.StartedAt;
                sub += " · " + (up.TotalHours >= 1 ? (int)up.TotalHours + " ч " + up.Minutes + " мин" : Math.Max(1, (int)up.TotalMinutes) + " мин");
            }
            wSub.Text = sub;
            wPower.SetKeys(run ? "Accent" : "Surface2", run ? null : "Stroke");
            wPowerIcon.SetResourceReference(TextBlock.ForegroundProperty, run ? "OnAccent" : "Accent");
            wPower.IsEnabled = !tuning && !updating;
            SetWidgetPill(wDiscordDot, wDiscord, wLastDiscord);
            SetWidgetPill(wYoutubeDot, wYoutube, wLastYoutube);
        }

        static void SetWidgetPill(Ellipse dot, TextBlock value, ProbeResult p)
        {
            string lv = WidgetLogic.Level(p);
            dot.SetResourceReference(Shape.FillProperty, Ui.FgKey(lv));
            value.Text = WidgetLogic.Latency(p);
            value.ToolTip = p == null ? null : p.Passed ? p.Timing() : p.Stage + ": " + p.Detail;
        }

        // Лёгкий замер раз в 20 секунд, только пока виджет открыт: соединение и заголовки ответа, без загрузки страниц
        async void ProbeForWidget()
        {
            if (widget == null || widgetProbing) return;
            widgetProbing = true;
            try
            {
                Task<ProbeResult> d = NetCheck.Probe(new Target("discord.com", "/", true), true);
                Task<ProbeResult> y = NetCheck.Probe(new Target("www.youtube.com", "/", true), true);
                await Task.WhenAll(d, y);
                wLastDiscord = d.Result;
                wLastYoutube = y.Result;
                RefreshWidget();
            }
            catch { }
            finally { widgetProbing = false; }
        }
    }
}
