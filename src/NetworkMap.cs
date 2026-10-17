using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Veylix
{
    // Последнее известное состояние сервиса (из проверки доступности или Network Pulse)
    public class ServiceSnapshot
    {
        public string Name;
        public ServiceStatus Status = ServiceStatus.Unknown;
        public int LatencyMs = -1;
        public DateTime When = DateTime.MinValue, LastOk = DateTime.MinValue;
        public string Detail = "", Source = "";
    }

    // Живая схема: ПК → движок winws → сервисы. Цвета — результаты реальных проверок,
    // бегущая точка — признак того, что обход активен на этом участке (это не трафик и не пакеты).
    public class NetworkMap : Canvas
    {
        public const double W = 520, H = 150;
        public event Action<string> NodeClicked;

        class Node
        {
            public string Id;
            public double X, Y;
            public Grid Visual;
            public Ellipse Ring, Halo;
            public Border Disc;
            public TextBlock Glyph, Name, Metric;
            public ScaleTransform Scale;
            public string Level = "";
        }

        class Edge
        {
            public string Id;
            public Path Line;
            public PathGeometry Geo;
            public Path Dot;
            public EllipseGeometry DotGeo;
            public bool Flowing, Dim;
            public string Level = "";
        }

        readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        readonly Dictionary<string, Edge> edges = new Dictionary<string, Edge>();

        public NetworkMap()
        {
            Width = W;
            Height = H;
            ClipToBounds = false;
            AddEdge("pc-engine", new Point(36, 72), null, new Point(200, 72));
            AddEdge("engine-discord", new Point(200, 72), null, new Point(392, 72));
            AddEdge("engine-youtube", new Point(200, 72), new Point(300, 72), new Point(392, 126));
            AddEdge("pc-internet", new Point(36, 72), new Point(150, -6), new Point(392, 20));
            AddNode("pc", 36, 72, 0xE7F8, "Этот ПК", true);
            AddNode("engine", 200, 72, 0xE7E8, "Обход", true);
            AddNode("internet", 392, 20, 0xE774, "Интернет", false);
            AddNode("discord", 392, 72, 0xE8BD, "Discord", false);
            AddNode("youtube", 392, 126, 0xE714, "YouTube", false);
        }

        void AddEdge(string id, Point a, Point? ctrl, Point b)
        {
            Edge e = new Edge();
            e.Id = id;
            PathFigure f = new PathFigure();
            f.StartPoint = a;
            if (ctrl.HasValue) f.Segments.Add(new QuadraticBezierSegment(ctrl.Value, b, true));
            else f.Segments.Add(new LineSegment(b, true));
            e.Geo = new PathGeometry();
            e.Geo.Figures.Add(f);
            e.Geo.Freeze();
            e.Line = new Path();
            e.Line.Data = e.Geo;
            e.Line.StrokeThickness = 2;
            e.Line.StrokeStartLineCap = PenLineCap.Round;
            e.Line.StrokeEndLineCap = PenLineCap.Round;
            e.Line.IsHitTestVisible = false;
            Children.Add(e.Line);
            e.DotGeo = new EllipseGeometry(a, 3.5, 3.5);
            e.Dot = new Path();
            e.Dot.Data = e.DotGeo;
            e.Dot.Visibility = Visibility.Collapsed;
            e.Dot.IsHitTestVisible = false;
            Children.Add(e.Dot);
            edges[id] = e;
            SetEdge(id, "idle", false);
        }

        void AddNode(string id, double x, double y, int glyph, string name, bool labelBelow)
        {
            Node n = new Node();
            n.Id = id;
            n.X = x;
            n.Y = y;
            Grid v = new Grid();
            v.Width = 44;
            v.Height = 44;
            n.Halo = new Ellipse();
            n.Halo.Opacity = 0;
            v.Children.Add(n.Halo);
            n.Disc = new Border();
            n.Disc.Width = 36;
            n.Disc.Height = 36;
            n.Disc.CornerRadius = new CornerRadius(18);
            n.Disc.SetResourceReference(Border.BackgroundProperty, "Surface2");
            v.Children.Add(n.Disc);
            n.Ring = new Ellipse();
            n.Ring.Width = 36;
            n.Ring.Height = 36;
            n.Ring.StrokeThickness = 2;
            v.Children.Add(n.Ring);
            n.Glyph = Theme.Icon(glyph, 14, "Text2");
            v.Children.Add(n.Glyph);
            n.Scale = new ScaleTransform(1, 1, 22, 22);
            v.RenderTransform = n.Scale;
            v.Cursor = Cursors.Hand;
            v.Background = Brushes.Transparent;
            v.MouseEnter += delegate { Theme.Animate(n.Scale, ScaleTransform.ScaleXProperty, 1.1, 140); Theme.Animate(n.Scale, ScaleTransform.ScaleYProperty, 1.1, 140); };
            v.MouseLeave += delegate { Theme.Animate(n.Scale, ScaleTransform.ScaleXProperty, 1, 200); Theme.Animate(n.Scale, ScaleTransform.ScaleYProperty, 1, 200); };
            v.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                Action<string> h = NodeClicked;
                if (h != null) h(id);
                e.Handled = true;
            };
            ToolTipService.SetInitialShowDelay(v, 150);
            Canvas.SetLeft(v, x - 22);
            Canvas.SetTop(v, y - 22);
            Children.Add(v);
            n.Visual = v;

            StackPanel lbl = new StackPanel();
            n.Name = Theme.T(name, 11.5, "Text", true);
            n.Metric = Theme.T("нет данных", 10.5, "Text3", false);
            lbl.Children.Add(n.Name);
            lbl.Children.Add(n.Metric);
            lbl.IsHitTestVisible = false;
            if (labelBelow)
            {
                lbl.Width = 130;
                n.Name.HorizontalAlignment = HorizontalAlignment.Center;
                n.Metric.HorizontalAlignment = HorizontalAlignment.Center;
                Canvas.SetLeft(lbl, x - 65);
                Canvas.SetTop(lbl, y + 24);
            }
            else
            {
                lbl.Width = 104;
                Canvas.SetLeft(lbl, x + 26);
                Canvas.SetTop(lbl, y - 15);
            }
            Children.Add(lbl);
            nodes[id] = n;
            SetNode(id, "idle", "нет данных", null);
        }

        static string Fg(string level)
        {
            return level == "idle" ? "StrokeStrong" : Ui.FgKey(level);
        }

        public void SetNode(string id, string level, string metric, string tooltip)
        {
            Node n = nodes[id];
            n.Metric.Text = metric;
            n.Visual.ToolTip = tooltip;
            if (n.Level == level) return;
            n.Level = level;
            n.Ring.SetResourceReference(Shape.StrokeProperty, Fg(level));
            n.Glyph.SetResourceReference(TextBlock.ForegroundProperty, level == "idle" ? "Text3" : Ui.FgKey(level));
            n.Halo.SetResourceReference(Shape.FillProperty, Ui.SoftKey(level));
            n.Ring.StrokeDashArray = level == "idle" ? new DoubleCollection(new double[] { 2, 2 }) : null;
            n.Metric.SetResourceReference(TextBlock.ForegroundProperty, level == "err" ? "Err" : "Text3");
            Pulse(n);
        }

        public void SetEdge(string id, string level, bool flowing)
        {
            SetEdge(id, level, flowing, false);
        }

        // dim — участок сейчас не проходит через обход (движок выключен): показываем приглушённо
        public void SetEdge(string id, string level, bool flowing, bool dim)
        {
            Edge e = edges[id];
            if (e.Level == level && e.Flowing == flowing && e.Dim == dim) return;
            e.Level = level;
            e.Flowing = flowing;
            e.Dim = dim;
            e.Line.SetResourceReference(Shape.StrokeProperty, Fg(level));
            e.Line.StrokeDashArray = level == "idle" || level == "err" ? new DoubleCollection(new double[] { 2, 3 }) : null;
            e.Line.Opacity = level == "idle" ? 0.7 : (e.Dim ? 0.35 : 1);
            e.Dot.SetResourceReference(Shape.FillProperty, Ui.FgKey(level));
            Flow(e);
        }

        void Pulse(Node n)
        {
            bool on = (n.Level == "ok" || n.Level == "busy") && Theme.Animations && (n.Id == "engine");
            if (!on)
            {
                n.Halo.BeginAnimation(UIElement.OpacityProperty, null);
                n.Halo.Opacity = n.Level == "ok" && n.Id == "engine" ? 0.6 : 0;
                return;
            }
            DoubleAnimation a = new DoubleAnimation(0.15, 0.85, new Duration(Theme.Dur(n.Level == "busy" ? 0.7 : 1.8)));
            a.AutoReverse = true;
            a.RepeatBehavior = RepeatBehavior.Forever;
            n.Halo.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void Flow(Edge e)
        {
            if (!e.Flowing || !Theme.Animations)
            {
                e.DotGeo.BeginAnimation(EllipseGeometry.CenterProperty, null);
                e.Dot.Visibility = Visibility.Collapsed;
                return;
            }
            e.Dot.Visibility = Visibility.Visible;
            PointAnimationUsingPath a = new PointAnimationUsingPath();
            a.PathGeometry = e.Geo;
            a.Duration = new Duration(Theme.Dur(2.4));
            a.RepeatBehavior = RepeatBehavior.Forever;
            e.DotGeo.BeginAnimation(EllipseGeometry.CenterProperty, a);
        }

        // Пересчитать анимации (например, после выключения анимаций в настройках)
        public void Restyle()
        {
            foreach (Node n in nodes.Values) Pulse(n);
            foreach (Edge e in edges.Values) Flow(e);
        }
    }

    public partial class MainWindow
    {
        NetworkMap netMap;
        readonly Dictionary<string, ServiceSnapshot> netState = new Dictionary<string, ServiceSnapshot>();
        static readonly string[] MapServices = { "Интернет", "Discord", "YouTube" };

        ServiceSnapshot SnapOf(string name)
        {
            ServiceSnapshot s;
            if (!netState.TryGetValue(name, out s))
            {
                s = new ServiceSnapshot();
                s.Name = name;
                netState[name] = s;
            }
            return s;
        }

        FrameworkElement BuildMapCard()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(22, 16, 22, 12);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(0));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.Children.Add(Theme.T("Схема подключения", 15, "Text", true));
            VButton pulse = LinkButton("Network Pulse");
            pulse.Click += delegate { ShowPage("pulse"); };
            pulse.Margin = new Thickness(0, -4, -8, 0);
            Grid.SetColumn(pulse, 1);
            head.Children.Add(pulse);
            g.Children.Add(head);
            netMap = new NetworkMap();
            netMap.HorizontalAlignment = HorizontalAlignment.Left;
            netMap.VerticalAlignment = VerticalAlignment.Center;
            netMap.Margin = new Thickness(4, 6, 0, 0);
            netMap.NodeClicked += delegate(string id) { ShowPage(id == "engine" ? "strategies" : "diag"); };
            Grid.SetRow(netMap, 1);
            g.Children.Add(netMap);
            TextBlock legend = Theme.T("Цвет — последняя проверка · точка — обход активен (это не трафик) · щелчок — подробности", 10.5, "Text3", false);
            Grid.SetRow(legend, 2);
            g.Children.Add(legend);
            c.Content = g;
            RefreshMap();
            return c;
        }

        static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "никогда";
            TimeSpan d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return (int)Math.Max(1, d.TotalSeconds) + " с назад";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " мин назад";
            return t.ToString("HH:mm");
        }

        void RefreshMap()
        {
            if (netMap == null || engine == null) return;
            bool admin = Diagnostics.IsAdmin();
            netMap.SetNode("pc", admin ? "ok" : "err", admin ? "администратор" : "нет прав администратора",
                "Veylix " + Program.Version + "\nПрава администратора: " + (admin ? "есть" : "нет — WinDivert не запустится") +
                "\nПриложение работает: " + Duration(DateTime.Now - session.AppStart) + "\nЩелчок — диагностика");

            bool run = engine.IsRunning;
            string engLevel = tuning || busy ? "busy" : run ? "ok" : "idle";
            string strat = engine.Current != null ? engine.Current.Display : (selected != null ? selected.Display : "—");
            netMap.SetNode("engine", engLevel, run ? "winws · " + strat : tuning ? "автоподбор…" : busy ? "применяю…" : "выключен",
                "Движок winws.exe: " + (run ? "работает, PID " + engine.ProcessId + ", " + Duration(DateTime.Now - engine.StartedAt) : "не запущен") +
                "\nСтратегия: " + strat + "\nЩелчок — стратегии");
            netMap.SetEdge("pc-engine", run ? "ok" : engLevel == "busy" ? "busy" : "idle", run);

            string[] ids = { "internet", "discord", "youtube" };
            for (int i = 0; i < 3; i++)
            {
                ServiceSnapshot s = SnapOf(MapServices[i]);
                string lvl = s.Status == ServiceStatus.Unknown ? "idle" : Level(s.Status);
                string metric = s.Status == ServiceStatus.Unknown ? "нет данных"
                    : (s.LatencyMs >= 0 ? s.LatencyMs + " мс" : NetCheck.StatusWord(s.Status).ToLowerInvariant()) + " · " + Ago(s.When);
                string tip = MapServices[i] + ": " + (s.Status == ServiceStatus.Unknown ? "ещё не проверялся" : NetCheck.StatusWord(s.Status)) +
                    (s.Detail.Length > 0 ? "\n" + s.Detail : "") +
                    (s.LastOk != DateTime.MinValue ? "\nПоследний успех: " + s.LastOk.ToString("HH:mm:ss") : "") +
                    "\nЩелчок — диагностика";
                netMap.SetNode(ids[i], lvl, metric, tip);
                if (i == 0) netMap.SetEdge("pc-internet", lvl, s.Status == ServiceStatus.Ok);
                else netMap.SetEdge("engine-" + ids[i], lvl, run && s.Status == ServiceStatus.Ok, !run);
            }
        }

        static string Duration(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }
    }
}
