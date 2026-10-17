using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Veylix
{
    // Векторные иллюстрации для тем Bee Night, Sunset Hive, Aurora и Morning Meadow.
    // Всё рисуется фигурами WPF — без картинок и внешних ресурсов, поэтому чётко на любом масштабе.
    public static class Scenes
    {
        public const double HeaderW = 660, HeaderH = 112;

        static Color C(string h) { return Theme.C(h); }

        static Brush V(string top, string bottom)
        {
            LinearGradientBrush g = new LinearGradientBrush(C(top), C(bottom), 90);
            g.Freeze();
            return g;
        }

        static Brush S(string h)
        {
            SolidColorBrush b = new SolidColorBrush(C(h));
            b.Freeze();
            return b;
        }

        static void Put(Canvas c, UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }

        static Ellipse Dot(double d, Brush fill)
        {
            Ellipse e = new Ellipse();
            e.Width = d;
            e.Height = d;
            e.Fill = fill;
            return e;
        }

        static void Stars(Canvas c, int count, double w, double h, int seed, string color)
        {
            Random r = new Random(seed);
            for (int i = 0; i < count; i++)
            {
                double d = 1.2 + r.NextDouble() * 2.0;
                Ellipse e = Dot(d, S(color));
                e.Opacity = 0.35 + r.NextDouble() * 0.6;
                Put(c, e, r.NextDouble() * w, r.NextDouble() * h);
            }
        }

        static void Glow(Canvas c, double cx, double cy, double r, Color col)
        {
            Ellipse g = new Ellipse();
            g.Width = r * 2;
            g.Height = r * 2;
            RadialGradientBrush rb = new RadialGradientBrush(Color.FromArgb(120, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B));
            rb.Freeze();
            g.Fill = rb;
            Put(c, g, cx - r, cy - r);
        }

        // Облако из нескольких кругов, залитое одним градиентом
        public static Path Cloud(double w, string top, string bottom)
        {
            double h = w * 0.5;
            GeometryGroup gg = new GeometryGroup();
            gg.FillRule = FillRule.Nonzero;
            gg.Children.Add(new EllipseGeometry(new Rect(0, h * 0.42, w * 0.42, h * 0.58)));
            gg.Children.Add(new EllipseGeometry(new Rect(w * 0.18, h * 0.12, w * 0.40, h * 0.76)));
            gg.Children.Add(new EllipseGeometry(new Rect(w * 0.40, 0, w * 0.38, h * 0.82)));
            gg.Children.Add(new EllipseGeometry(new Rect(w * 0.62, h * 0.32, w * 0.38, h * 0.62)));
            gg.Children.Add(new RectangleGeometry(new Rect(w * 0.18, h * 0.6, w * 0.66, h * 0.36), h * 0.12, h * 0.12));
            Path p = new Path();
            p.Data = gg;
            p.Fill = V(top, bottom);
            return p;
        }

        // Холм: плавная кривая через заданные точки, залитая до низа
        static Path Hill(double w, double h, double[] ys, string fill)
        {
            PathFigure f = new PathFigure();
            f.StartPoint = new Point(0, h);
            f.IsClosed = true;
            f.Segments.Add(new LineSegment(new Point(0, ys[0]), false));
            double step = w / (ys.Length - 1);
            for (int i = 1; i < ys.Length; i++)
            {
                double x0 = (i - 1) * step, x1 = i * step;
                f.Segments.Add(new BezierSegment(new Point(x0 + step / 2, ys[i - 1]), new Point(x1 - step / 2, ys[i]), new Point(x1, ys[i]), false));
            }
            f.Segments.Add(new LineSegment(new Point(w, h), false));
            PathGeometry g = new PathGeometry();
            g.Figures.Add(f);
            Path p = new Path();
            p.Data = g;
            p.Fill = S(fill);
            return p;
        }

        static Path Line(string data, string stroke, double thickness)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(data);
            p.Stroke = S(stroke);
            p.StrokeThickness = thickness;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.StrokeLineJoin = PenLineJoin.Round;
            return p;
        }

        // Радиовышка с тарелкой (как на макете Bee Night)
        static void Tower(Canvas c, double x, double baseY)
        {
            string leg = "#2A2D72", hi = "#7D88FF";
            c.Children.Add(Line(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M {0},{1} L {2},{3} M {4},{1} L {5},{3}", x - 22, baseY, x - 5, 30, x + 22, x + 5), leg, 4));
            string bars = "";
            double[] ys = { 98, 78, 58, 42 };
            for (int i = 0; i < ys.Length; i++)
            {
                double t = (baseY - ys[i]) / (baseY - 30);
                double half = 22 - 17 * t;
                bars += string.Format(System.Globalization.CultureInfo.InvariantCulture, " M {0},{1} L {2},{1}", x - half, ys[i], x + half);
                if (i + 1 < ys.Length)
                {
                    double t2 = (baseY - ys[i + 1]) / (baseY - 30);
                    double half2 = 22 - 17 * t2;
                    bars += string.Format(System.Globalization.CultureInfo.InvariantCulture, " M {0},{1} L {2},{3} M {4},{1} L {5},{3}", x - half, ys[i], x + half2, ys[i + 1], x + half, x - half2);
                }
            }
            c.Children.Add(Line(bars.Trim(), leg, 2));
            c.Children.Add(Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M {0},{1} L {2},{3}", x - 4, 31, x - 20, baseY), hi, 1));
            Rectangle deck = new Rectangle();
            deck.Width = 26; deck.Height = 7;
            deck.RadiusX = 2; deck.RadiusY = 2;
            deck.Fill = S("#3A3F96");
            Put(c, deck, x - 13, 24);
            // тарелка
            Ellipse dish = new Ellipse();
            dish.Width = 28; dish.Height = 14;
            dish.Fill = V("#C9D2FF", "#6E78E0");
            dish.Stroke = S("#2A2D72");
            dish.StrokeThickness = 1.5;
            dish.RenderTransform = new RotateTransform(-35, 14, 7);
            Put(c, dish, x - 2, 4);
            c.Children.Add(Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M {0},24 L {1},14", x + 2, x + 10), leg, 2.5));
            Glow(c, x - 6, 22, 7, C("#FF6F86"));
            Put(c, Dot(4, S("#FF8FA0")), x - 8, 20);
        }

        // ---------------- шапка ----------------

        public static FrameworkElement Header(Palette p)
        {
            Canvas c = new Canvas();
            c.Width = HeaderW;
            c.Height = HeaderH;
            c.ClipToBounds = true;
            c.IsHitTestVisible = false;
            switch (p.Scene)
            {
                case "night":
                    Stars(c, 22, HeaderW, 70, 7, "#FFFFFF");
                    Glow(c, 262, 26, 22, C("#FFC86B"));
                    Put(c, Dot(13, V("#FFE2A3", "#FFB54D")), 255.5, 19.5);
                    Put(c, Cloud(190, "#6B4FCC", "#33287F"), 120, 70);
                    Put(c, Cloud(250, "#8A63E0", "#3D2F8E"), 270, 54);
                    Tower(c, 520, 112);
                    Put(c, Cloud(220, "#A57AF0", "#46349A"), 455, 72);
                    Put(c, Cloud(120, "#C59BFF", "#5A43B0"), 360, 84);
                    break;
                case "sunset":
                    Glow(c, 395, 86, 70, C("#FF9A6B"));
                    Put(c, Dot(78, V("#FFE08A", "#FF6B5A")), 356, 47);
                    Ellipse streak = new Ellipse();
                    streak.Width = 210; streak.Height = 7; streak.Fill = S("#55FFB394");
                    Put(c, streak, 300, 40);
                    Ellipse streak2 = new Ellipse();
                    streak2.Width = 140; streak2.Height = 5; streak2.Fill = S("#40FFB394");
                    Put(c, streak2, 470, 58);
                    c.Children.Add(Line("M 240,30 q 5,-5 10,0 q 5,-5 10,0 M 268,20 q 4,-4 8,0 q 4,-4 8,0 M 520,26 q 5,-5 10,0 q 5,-5 10,0", "#FFD7C2", 1.6));
                    Put(c, Hill(HeaderW, HeaderH, new double[] { 96, 74, 88, 66, 82, 70 }, "#7B3070"), 0, 0);
                    Put(c, Hill(HeaderW, HeaderH, new double[] { 104, 90, 84, 98, 80, 92 }, "#5A2457"), 0, 0);
                    Put(c, Hill(HeaderW, HeaderH, new double[] { 110, 104, 100, 106, 96, 102 }, "#3E1846"), 0, 0);
                    break;
                case "aurora":
                    Stars(c, 26, HeaderW, 80, 11, "#E9FFF8");
                    Canvas rib = new Canvas();
                    rib.Effect = new BlurEffect { Radius = 6 };
                    rib.Children.Add(Ribbon("M 60,40 C 180,0 300,70 420,24 C 520,-6 600,40 660,18 L 660,44 C 600,64 520,30 420,52 C 300,96 180,30 60,64 Z",
                        new string[] { "#003FE0B5", "#AA3FE0B5", "#996BD8FF", "#007A5CFF" }));
                    rib.Children.Add(Ribbon("M 200,58 C 300,30 400,80 520,48 C 590,30 630,52 660,44 L 660,58 C 620,70 580,56 520,66 C 400,96 300,52 200,74 Z",
                        new string[] { "#0046F0A0", "#7746F0A0", "#669C7CFF", "#009C7CFF" }));
                    c.Children.Add(rib);
                    Path m1 = new Path();
                    m1.Data = Geometry.Parse("M 0,112 L 90,62 L 150,92 L 250,46 L 340,96 L 430,58 L 520,90 L 600,52 L 660,80 L 660,112 Z");
                    m1.Fill = S("#14343F");
                    c.Children.Add(m1);
                    c.Children.Add(Line("M 238,53 L 250,46 L 262,54 M 418,65 L 430,58 L 442,66 M 588,59 L 600,52 L 612,60", "#B5E9DE", 2));
                    Path m2 = new Path();
                    m2.Data = Geometry.Parse("M 0,112 L 60,90 L 140,108 L 210,84 L 300,110 L 390,88 L 470,108 L 560,86 L 660,104 L 660,112 Z");
                    m2.Fill = S("#0B222A");
                    c.Children.Add(m2);
                    break;
                case "meadow":
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        c.Children.Add(Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M {0},{1} L {2},{3}",
                            430 + Math.Cos(a) * 18, 30 + Math.Sin(a) * 18, 430 + Math.Cos(a) * 26, 30 + Math.Sin(a) * 26), "#FFC93C", 2.5));
                    }
                    Put(c, Dot(26, V("#FFE27A", "#FFC23C")), 417, 17);
                    Put(c, Cloud(120, "#FFFFFF", "#DCE9FA"), 230, 26);
                    Put(c, Cloud(90, "#FFFFFF", "#E1EDFA"), 520, 18);
                    Put(c, Hill(HeaderW, HeaderH, new double[] { 92, 70, 84, 64, 80, 72 }, "#BFE6A3"), 0, 0);
                    Put(c, Hill(HeaderW, HeaderH, new double[] { 104, 92, 86, 98, 82, 94 }, "#93D27C"), 0, 0);
                    Random r = new Random(5);
                    string[] fl = { "#FF8FB1", "#FFD45C", "#FFFFFF", "#B79CFF" };
                    for (int i = 0; i < 18; i++)
                        Put(c, Dot(4, S(fl[i % fl.Length])), 60 + r.NextDouble() * 580, 96 + r.NextDouble() * 12);
                    break;
            }
            // левый край сцены растворяется, чтобы не спорить с заголовком страницы
            LinearGradientBrush mask = new LinearGradientBrush();
            mask.StartPoint = new Point(0, 0);
            mask.EndPoint = new Point(1, 0);
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 0.28));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1));
            c.OpacityMask = mask;
            return c;
        }

        static Path Ribbon(string data, string[] stops)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(data);
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(1, 0);
            for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new GradientStop(C(stops[i]), i / (double)(stops.Length - 1)));
            g.Freeze();
            p.Fill = g;
            return p;
        }

        static string[] CloudColors(Palette p)
        {
            switch (p.Scene)
            {
                case "sunset": return new string[] { "#B4507E", "#5E2552" };
                case "aurora": return new string[] { "#2A6C70", "#143841" };
                case "meadow": return new string[] { "#FFFFFF", "#DDEAF8" };
                default: return new string[] { "#8A63E0", "#3D2F8E" };
            }
        }

        // ---------------- карточка питания ----------------

        public static FrameworkElement HeroArt(Palette p)
        {
            Canvas c = new Canvas();
            c.Width = 112;
            c.Height = 110;
            c.IsHitTestVisible = false;
            string[] cc = CloudColors(p);
            Path cl = Cloud(70, cc[0], cc[1]);
            cl.Opacity = 0.8;
            Put(c, cl, 40, 76);
            // пунктирная траектория полёта
            Path trail = Line("M 6,104 C 0,80 30,74 26,92 C 22,108 10,96 20,84 C 30,72 40,70 46,64", p.Dark ? "#AFFFFFFF" : "#994C5F6D", 1.4);
            trail.StrokeDashArray = new DoubleCollection(new double[] { 2.5, 3 });
            c.Children.Add(trail);
            return c;
        }

        // ---------------- боковая панель ----------------

        public static FrameworkElement NavArt(Palette p, Bee bee, TextBlock label)
        {
            Canvas c = new Canvas();
            c.Width = 188;
            c.Height = 150;
            c.IsHitTestVisible = false;
            string[] cc = CloudColors(p);
            Put(c, Cloud(150, cc[0], cc[1]), -14, 104);
            Panel old = bee.Parent as Panel;
            if (old != null) old.Children.Remove(bee);
            Put(c, bee, 4, 30);
            Panel oldL = label.Parent as Panel;
            if (oldL != null) oldL.Children.Remove(label);
            label.FontFamily = new FontFamily("Segoe Print, Segoe UI");
            label.FontSize = 12.5;
            label.RenderTransform = new RotateTransform(-12);
            Put(c, label, 100, 22);
            return c;
        }

        // ---------------- миниатюра для выбора темы ----------------

        public static void Mini(Grid g, Palette p)
        {
            Canvas c = new Canvas();
            c.IsHitTestVisible = false;
            c.ClipToBounds = true;
            switch (p.Scene)
            {
                case "night":
                    Stars(c, 8, 130, 30, 3, "#FFFFFF");
                    Put(c, Dot(7, S("#FFC86B")), 104, 6);
                    Put(c, Cloud(70, "#8A63E0", "#3D2F8E"), 72, 50);
                    break;
                case "sunset":
                    Put(c, Dot(30, V("#FFE08A", "#FF6B5A")), 92, 44);
                    Put(c, Hill(140, 74, new double[] { 62, 54, 66, 56 }, "#5A2457"), 0, 0);
                    break;
                case "aurora":
                    Stars(c, 8, 130, 30, 4, "#E9FFF8");
                    Path r = Ribbon("M 20,26 C 60,6 90,36 140,14 L 140,26 C 90,48 60,18 20,38 Z", new string[] { "#003FE0B5", "#AA3FE0B5", "#007A5CFF" });
                    c.Children.Add(r);
                    Path m = new Path();
                    m.Data = Geometry.Parse("M 0,74 L 40,50 L 70,66 L 110,44 L 140,62 L 140,74 Z");
                    m.Fill = S("#14343F");
                    c.Children.Add(m);
                    break;
                case "meadow":
                    Put(c, Dot(12, S("#FFC93C")), 104, 6);
                    Put(c, Hill(140, 74, new double[] { 64, 56, 66, 58 }, "#93D27C"), 0, 0);
                    break;
            }
            g.Children.Insert(0, c);
        }
    }

    // Талисман: пчёлка-робот. Выражение «лица» показывает состояние обхода.
    public class Bee : Canvas
    {
        readonly double s;
        readonly Canvas eyes = new Canvas();
        readonly TranslateTransform hover = new TranslateTransform();
        readonly ScaleTransform wingA = new ScaleTransform(1, 1), wingB = new ScaleTransform(1, 1);
        string mood = "";
        bool animated = true;
        readonly Canvas deco = new Canvas();                       // блёстки, капелька и т.п.
        readonly TranslateTransform look = new TranslateTransform(); // «взгляд» при диагностике

        public Bee(double size)
        {
            s = size;
            Width = s;
            Height = s;
            IsHitTestVisible = false;
            RenderTransform = hover;

            Children.Add(Wing(0.40, 0.02, -24, wingA));
            Children.Add(Wing(0.58, 0.08, 18, wingB));

            // туловище с полосками (полоски обрезаны по форме туловища)
            double bw = s * 0.56, bh = s * 0.42;
            Canvas body = new Canvas();
            body.Width = bw;
            body.Height = bh;
            body.RenderTransform = new RotateTransform(-16, bw / 2, bh / 2);
            Path sting = new Path();
            sting.Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M {0},{1} L {2},{3} L {0},{4} Z", bw * 0.94, bh * 0.38, bw * 1.1, bh * 0.5, bh * 0.62));
            sting.Fill = Brush("#2B2140");
            body.Children.Add(sting);
            Ellipse be = new Ellipse();
            be.Width = bw; be.Height = bh;
            be.Fill = Grad("#FFD54F", "#F29A1E");
            be.Stroke = Brush("#8A5316");
            be.StrokeThickness = Math.Max(1, s * 0.012);
            body.Children.Add(be);
            Canvas stripes = new Canvas();
            stripes.Clip = new EllipseGeometry(new Rect(0, 0, bw, bh));
            foreach (double x in new double[] { 0.38, 0.62 })
            {
                Rectangle r = new Rectangle();
                r.Width = bw * 0.12;
                r.Height = bh;
                r.Fill = Brush("#2B2140");
                Canvas.SetLeft(r, bw * x);
                stripes.Children.Add(r);
            }
            body.Children.Add(stripes);
            Canvas.SetLeft(body, s * 0.36);
            Canvas.SetTop(body, s * 0.47);
            Children.Add(body);

            // усики
            Path ant = new Path();
            ant.Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M {0},{1} Q {2},{3} {4},{5} M {6},{7} Q {8},{9} {10},{11}",
                s * 0.22, s * 0.20, s * 0.16, s * 0.06, s * 0.08, s * 0.05,
                s * 0.36, s * 0.19, s * 0.40, s * 0.04, s * 0.50, s * 0.03));
            ant.Stroke = Brush("#2B2140");
            ant.StrokeThickness = Math.Max(1.2, s * 0.025);
            ant.StrokeStartLineCap = PenLineCap.Round;
            ant.StrokeEndLineCap = PenLineCap.Round;
            Children.Add(ant);
            AddDot(s * 0.08, s * 0.05, s * 0.075, "#FFC93C");
            AddDot(s * 0.50, s * 0.03, s * 0.075, "#FFC93C");

            // голова и «визор»
            Ellipse head = new Ellipse();
            head.Width = s * 0.54;
            head.Height = s * 0.52;
            head.Fill = Grad("#FFDA5C", "#F4A62A");
            head.Stroke = Brush("#8A5316");
            head.StrokeThickness = Math.Max(1, s * 0.012);
            Canvas.SetLeft(head, s * 0.03);
            Canvas.SetTop(head, s * 0.15);
            Children.Add(head);
            Rectangle visor = new Rectangle();
            visor.Width = s * 0.40;
            visor.Height = s * 0.27;
            visor.RadiusX = s * 0.12;
            visor.RadiusY = s * 0.12;
            visor.Fill = Grad("#2A2E6E", "#151738");
            Canvas.SetLeft(visor, s * 0.10);
            Canvas.SetTop(visor, s * 0.28);
            Children.Add(visor);
            Rectangle shine = new Rectangle();
            shine.Width = s * 0.12;
            shine.Height = s * 0.035;
            shine.RadiusX = s * 0.02;
            shine.RadiusY = s * 0.02;
            shine.Fill = Brush("#55FFFFFF");
            Canvas.SetLeft(shine, s * 0.15);
            Canvas.SetTop(shine, s * 0.30);
            Children.Add(shine);
            AddDot(s * 0.07, s * 0.50, s * 0.06, "#66FF8FB1");
            AddDot(s * 0.47, s * 0.50, s * 0.06, "#66FF8FB1");
            eyes.RenderTransform = look;
            Children.Add(eyes);
            Children.Add(deco);
            SetMood("idle");
        }

        static Brush Brush(string h)
        {
            SolidColorBrush b = new SolidColorBrush(Theme.C(h));
            b.Freeze();
            return b;
        }

        static Brush Grad(string a, string b)
        {
            LinearGradientBrush g = new LinearGradientBrush(Theme.C(a), Theme.C(b), 90);
            g.Freeze();
            return g;
        }

        void AddDot(double cx, double cy, double d, string color)
        {
            Ellipse e = new Ellipse();
            e.Width = d;
            e.Height = d;
            e.Fill = Brush(color);
            Canvas.SetLeft(e, cx - d / 2);
            Canvas.SetTop(e, cy - d / 2);
            Children.Add(e);
        }

        Ellipse Wing(double x, double y, double angle, ScaleTransform flutter)
        {
            Ellipse w = new Ellipse();
            w.Width = s * 0.32;
            w.Height = s * 0.40;
            w.Fill = Brush("#88B9D4FF");
            w.Stroke = Brush("#CCE6F0FF");
            w.StrokeThickness = Math.Max(1, s * 0.012);
            TransformGroup tg = new TransformGroup();
            flutter.CenterX = s * 0.16;
            flutter.CenterY = s * 0.40;
            tg.Children.Add(flutter);
            tg.Children.Add(new RotateTransform(angle, s * 0.16, s * 0.40));
            w.RenderTransform = tg;
            Canvas.SetLeft(w, s * x);
            Canvas.SetTop(w, s * y);
            return w;
        }

        public string Mood { get { return mood; } }

        // Анимации этой пчёлки (общий выключатель анимаций тоже учитывается)
        public bool Animated
        {
            get { return animated; }
            set { if (animated == value) return; animated = value; Animate(); }
        }

        Path Stroke(string data, string color, double thickness)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(data);
            p.Stroke = Brush(color);
            p.StrokeThickness = thickness;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            return p;
        }

        static string F(string fmt, params double[] v)
        {
            object[] o = new object[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = v[i];
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, o);
        }

        // idle — отдыхает (обход выключен); happy — обход работает; busy/diag — идёт проверка или подключение;
        // error — что-то не так; restored — связь только что восстановилась
        public void SetMood(string m)
        {
            if (m == mood) return;
            mood = m;
            eyes.Children.Clear();
            deco.Children.Clear();
            double vx = s * 0.10, vy = s * 0.28, vw = s * 0.40, vh = s * 0.27, e = s * 0.08;
            string cyan = m == "error" ? "#FF9AA8" : "#5CE1FF";
            double th = Math.Max(1.3, s * 0.032);
            foreach (double fx in new double[] { 0.30, 0.70 })
            {
                double cx = vx + vw * fx, cy = vy + vh * 0.52;
                if (m == "busy" || m == "diag")
                {
                    Ellipse eye = new Ellipse();
                    eye.Width = e;
                    eye.Height = e * 1.25;
                    eye.Fill = Brush(cyan);
                    Canvas.SetLeft(eye, cx - e / 2);
                    Canvas.SetTop(eye, cy - e * 0.62);
                    eyes.Children.Add(eye);
                    Ellipse hl = new Ellipse();
                    hl.Width = e * 0.35; hl.Height = e * 0.35;
                    hl.Fill = Brush("#FFFFFF");
                    Canvas.SetLeft(hl, cx - e * 0.05);
                    Canvas.SetTop(hl, cy - e * 0.45);
                    eyes.Children.Add(hl);
                }
                else if (m == "error")
                {
                    // встревоженные глаза: точки и брови домиком
                    Ellipse eye = new Ellipse();
                    eye.Width = e * 0.7; eye.Height = e * 0.7;
                    eye.Fill = Brush(cyan);
                    Canvas.SetLeft(eye, cx - e * 0.35);
                    Canvas.SetTop(eye, cy - e * 0.2);
                    eyes.Children.Add(eye);
                    double dir = fx < 0.5 ? -1 : 1;   // внутренние концы бровей приподняты — «встревоженный», а не «сердитый» вид
                    eyes.Children.Add(Stroke(F("M {0},{1} L {2},{3}", cx - e * 0.55, cy - e * 0.55 - dir * e * 0.25, cx + e * 0.55, cy - e * 0.55 + dir * e * 0.25), cyan, th * 0.8));
                }
                else
                {
                    eyes.Children.Add(Stroke(m == "happy" || m == "restored"
                        ? F("M {0},{1} Q {2},{3} {4},{1}", cx - e * 0.6, cy + e * 0.25, cx, cy - e * 0.75, cx + e * 0.6)
                        : F("M {0},{1} Q {2},{3} {4},{1}", cx - e * 0.55, cy, cx, cy + e * 0.35, cx + e * 0.55), cyan, th));
                }
            }
            double mx = vx + vw / 2, my = vy + vh * 0.8;
            if (m == "happy" || m == "restored")
                eyes.Children.Add(Stroke(F("M {0},{1} Q {2},{3} {4},{1}", mx - e * 0.5, my - e * 0.05, mx, my + e * 0.5, mx + e * 0.5), cyan, Math.Max(1.1, s * 0.022)));
            else if (m == "error")
                eyes.Children.Add(Stroke(F("M {0},{1} Q {2},{3} {4},{1}", mx - e * 0.45, my + e * 0.2, mx, my - e * 0.25, mx + e * 0.45), cyan, Math.Max(1.1, s * 0.022)));

            if (m == "error")
            {
                // капелька пота
                Path drop = new Path();
                drop.Data = Geometry.Parse(F("M {0},{1} Q {2},{3} {0},{4} Q {5},{3} {0},{1} Z", s * 0.60, s * 0.14, s * 0.645, s * 0.24, s * 0.27, s * 0.555));
                drop.Fill = Brush("#AA7FC8FF");
                deco.Children.Add(drop);
            }
            else if (m == "restored")
            {
                // блёстки радости
                double[,] pts = { { 0.02, 0.10 }, { 0.62, 0.0 }, { 0.86, 0.34 } };
                for (int i = 0; i < 3; i++)
                {
                    double x = s * pts[i, 0], y = s * pts[i, 1], r = s * (i == 1 ? 0.07 : 0.05);
                    Path st = new Path();
                    st.Data = Geometry.Parse(F("M {0},{1} Q {0},{2} {3},{2} Q {0},{2} {0},{4} Q {0},{2} {5},{2} Q {0},{2} {0},{1} Z", x, y - r, y, x + r, y + r, x - r));
                    st.Fill = Brush("#FFD54F");
                    deco.Children.Add(st);
                }
            }
            else if (m == "diag")
            {
                // маленькая лупа — идёт проверка
                Ellipse lens = new Ellipse();
                lens.Width = s * 0.16; lens.Height = s * 0.16;
                lens.Stroke = Brush("#5CE1FF");
                lens.StrokeThickness = Math.Max(1.2, s * 0.025);
                lens.Fill = Brush("#335CE1FF");
                Canvas.SetLeft(lens, s * 0.70);
                Canvas.SetTop(lens, s * 0.06);
                deco.Children.Add(lens);
                deco.Children.Add(Stroke(F("M {0},{1} L {2},{3}", s * 0.83, s * 0.20, s * 0.92, s * 0.29), "#5CE1FF", Math.Max(1.4, s * 0.03)));
            }
            Animate();
        }

        public void StopAnimations()
        {
            hover.BeginAnimation(TranslateTransform.YProperty, null);
            look.BeginAnimation(TranslateTransform.XProperty, null);
            wingA.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            wingB.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            deco.BeginAnimation(UIElement.OpacityProperty, null);
            look.X = 0;
            hover.Y = 0;
            deco.Opacity = 1;
        }

        // Парение, взмахи крыльев, «оглядывание» при диагностике, мерцание блёсток
        void Animate()
        {
            StopAnimations();
            if (!Theme.Animations || !animated) return;
            bool hop = mood == "restored";
            DoubleAnimation f = new DoubleAnimation(0, -s * (hop ? 0.10 : 0.04), new Duration(Theme.Dur(hop ? 0.35 : mood == "idle" ? 3.2 : 1.8)));
            f.AutoReverse = true;
            f.RepeatBehavior = RepeatBehavior.Forever;
            f.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            hover.BeginAnimation(TranslateTransform.YProperty, f);
            if (mood == "diag")
            {
                DoubleAnimation lk = new DoubleAnimation(-s * 0.03, s * 0.03, new Duration(Theme.Dur(0.9)));
                lk.AutoReverse = true;
                lk.RepeatBehavior = RepeatBehavior.Forever;
                lk.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                look.BeginAnimation(TranslateTransform.XProperty, lk);
            }
            if (mood == "restored")
            {
                DoubleAnimation tw = new DoubleAnimation(1, 0.3, new Duration(Theme.Dur(0.5)));
                tw.AutoReverse = true;
                tw.RepeatBehavior = RepeatBehavior.Forever;
                deco.BeginAnimation(UIElement.OpacityProperty, tw);
            }
            if (mood == "idle" || mood == "error") return;
            DoubleAnimation w = new DoubleAnimation(1, 0.78, new Duration(Theme.Dur(mood == "busy" || mood == "diag" ? 0.12 : 0.3)));
            w.AutoReverse = true;
            w.RepeatBehavior = RepeatBehavior.Forever;
            wingA.BeginAnimation(ScaleTransform.ScaleYProperty, w);
            wingB.BeginAnimation(ScaleTransform.ScaleYProperty, w);
        }

        // Перезапустить анимации (после смены скорости или выключателя анимаций)
        public void Restyle()
        {
            Animate();
        }
    }
}
