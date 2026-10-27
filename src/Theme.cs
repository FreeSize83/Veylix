using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Veylix
{
    // Набор цветов одной темы. Все элементы интерфейса ссылаются на ключи через DynamicResource,
    // поэтому смена темы — это просто замена значений в Application.Resources.
    public class Palette
    {
        public string Family, Title;
        public bool Dark, Glass;
        public readonly Dictionary<string, object> Res = new Dictionary<string, object>();
        public Brush GlassTint;            // фон окна поверх системного размытия (только Liquid Glass)
        public string Scene;               // иллюстрированные темы: night | sunset | aurora | meadow
        public bool Glow;                  // свечение кольца кнопки питания
        public Color[] Blobs;              // цветные пятна за стеклом

        public string Id { get { return Family + (Dark ? "-dark" : "-light"); } }
        public Color AccentColor { get { return ((SolidColorBrush)Res["Accent"]).Color; } }
    }

    public static class Theme
    {
        public static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        public static Palette Current;
        public static bool Animations = true;
        public static ThemeOptions Options = new ThemeOptions();   // Theme Studio поверх темы
        public static bool AccentAdjusted;                         // пользовательский акцент пришлось сделать контрастнее

        // Длительность с учётом выбранной скорости анимаций
        public static TimeSpan Dur(double seconds)
        {
            return TimeSpan.FromSeconds(seconds / Options.SpeedFactor);
        }

        public static double CornerScale { get { return Options.CornerScale; } }
        public static double Density { get { return Options.DensityFactor; } }
        public static double GlowFactor { get { return Options.GlowFactor; } }
        public static bool GlassActive;          // реальное размытие DWM сейчас включено
        public static event Action Changed;

        public static readonly string[] Families = { "cream", "graphite", "purple", "mint", "glass", "night", "sunset", "aurora", "meadow" };

        // Иллюстрированные темы с талисманом-пчелой. Meadow — светлая, остальные — тёмные.
        public static bool IsIllustrated(string f)
        {
            return f == "night" || f == "sunset" || f == "aurora" || f == "meadow";
        }

        public static string FamilyTitle(string f)
        {
            switch (f)
            {
                case "cream": return "Cream Light";
                case "graphite": return "Graphite Dark";
                case "purple": return "Veylix Purple";
                case "mint": return "Veylix Mint";
                case "glass": return "Liquid Glass";
                case "night": return "Bee Night";
                case "sunset": return "Sunset Hive";
                case "aurora": return "Aurora";
                case "meadow": return "Morning Meadow";
                default: return f;
            }
        }

        // ---------------- выбор темы ----------------

        public static bool SystemPrefersLight()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    if (v is int) return (int)v != 0;
                }
            }
            catch { }
            return true;
        }

        public static bool SystemTransparencyEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("EnableTransparency") : null;
                    if (v is int) return (int)v != 0;
                }
            }
            catch { }
            return true;
        }

        public static readonly TimeSpan LightFrom = new TimeSpan(7, 0, 0);
        public static readonly TimeSpan DarkFrom = new TimeSpan(19, 0, 0);

        public static bool InLightHours(DateTime now)
        {
            TimeSpan t = now.TimeOfDay;
            return t >= LightFrom && t < DarkFrom;
        }

        // Чистая функция: какую палитру показать при данных настройках.
        //  manual   — Cream всегда светлая, Graphite всегда тёмная, остальные следуют системной теме;
        //  system   — светлый/тёмный вариант по системной теме Windows;
        //  schedule — светлый вариант с 07:00 до 19:00, тёмный в остальное время.
        // Для Cream/Graphite в режимах system/schedule светлым вариантом считается Cream, тёмным — Graphite.
        public static Palette Resolve(string family, string mode, bool systemLight, DateTime now)
        {
            if (Array.IndexOf(Families, family) < 0) family = "cream";
            bool dark;
            if (mode == "schedule") dark = !InLightHours(now);
            else if (mode == "system") dark = !systemLight;
            else if (family == "cream" || family == "meadow") dark = false;
            else if (family == "graphite" || IsIllustrated(family)) dark = true;
            else dark = !systemLight;

            if (mode != "manual")
            {
                if (family == "cream" || family == "graphite") family = dark ? "graphite" : "cream";
                // иллюстрированные: светлый вариант — Morning Meadow, тёмный — выбранная тема (для Meadow — Bee Night)
                else if (IsIllustrated(family))
                {
                    if (!dark) family = "meadow";
                    else if (family == "meadow") family = "night";
                }
            }
            return Build(family, dark);
        }

        public static string ModeTitle(string mode)
        {
            if (mode == "system") return "Как в Windows";
            if (mode == "schedule") return "По расписанию";
            return "Вручную";
        }

        // ---------------- палитры ----------------

        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static SolidColorBrush B(string hex)
        {
            SolidColorBrush b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        static LinearGradientBrush V(string top, string bottom)
        {
            LinearGradientBrush g = new LinearGradientBrush(C(top), C(bottom), 90.0);
            g.Freeze();
            return g;
        }

        static LinearGradientBrush Diag(string a, string b, string c)
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(1, 1);
            g.GradientStops.Add(new GradientStop(C(a), 0));
            g.GradientStops.Add(new GradientStop(C(b), 0.5));
            g.GradientStops.Add(new GradientStop(C(c), 1));
            g.Freeze();
            return g;
        }

        static Color WithAlpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        static void Base(Palette p, string[] kv)
        {
            for (int i = 0; i + 1 < kv.Length; i += 2) p.Res[kv[i]] = B(kv[i + 1]);
        }

        public static Palette Build(string family, bool dark)
        {
            Palette p = new Palette();
            p.Family = family;
            p.Dark = dark;
            p.Title = FamilyTitle(family);
            p.Res["Sheen"] = Brushes.Transparent;

            if (family == "cream")
            {
                p.Dark = false;
                Base(p, new string[] {
                    "Nav", "#EFE5D7", "Surface", "#FFFCF7", "Surface2", "#F5EDE2", "Stroke", "#E6D9C8", "StrokeStrong", "#D3C1AB",
                    "Text", "#3B2F28", "Text2", "#7A6B5F", "Text3", "#A89887",
                    "Accent", "#A3591F", "AccentSoft", "#F4E2D0", "OnAccent", "#FFFFFF",
                    "Ok", "#3B8A55", "OkSoft", "#E1EFE2", "Warn", "#AD7116", "WarnSoft", "#F6E8CD",
                    "Err", "#BE4339", "ErrSoft", "#F6DDD8", "Info", "#4A6FA5", "InfoSoft", "#E0E7F2",
                    "Hover", "#0F3B2F28", "Press", "#1F3B2F28", "Input", "#FFFDFA", "Popup", "#FFFCF7", "ScrollThumb", "#557A6B5F" });
                p.Res["Bg"] = V("#F8F1E7", "#F0E6D8");
            }
            else if (family == "graphite")
            {
                p.Dark = true;
                Base(p, new string[] {
                    "Nav", "#1A1D21", "Surface", "#22262B", "Surface2", "#2A2F35", "Stroke", "#323840", "StrokeStrong", "#47505B",
                    "Text", "#E8EAED", "Text2", "#A2A9B3", "Text3", "#6F7782",
                    "Accent", "#8AB4F8", "AccentSoft", "#253246", "OnAccent", "#0D1521",
                    "Ok", "#5CC48A", "OkSoft", "#1C3227", "Warn", "#E2B341", "WarnSoft", "#383019",
                    "Err", "#F2777C", "ErrSoft", "#3B2224", "Info", "#82AAFF", "InfoSoft", "#212E47",
                    "Hover", "#14FFFFFF", "Press", "#24FFFFFF", "Input", "#1C2024", "Popup", "#2A2F35", "ScrollThumb", "#55FFFFFF" });
                p.Res["Bg"] = V("#17191C", "#141619");
            }
            else if (family == "purple")
            {
                if (!dark)
                {
                    Base(p, new string[] {
                        "Nav", "#EEE9FB", "Surface", "#FFFFFF", "Surface2", "#F5F2FD", "Stroke", "#E3DDF4", "StrokeStrong", "#CBC0EC",
                        "Text", "#231D38", "Text2", "#686185", "Text3", "#9A94B3",
                        "Accent", "#7152DF", "AccentSoft", "#ECE6FD", "OnAccent", "#FFFFFF",
                        "Ok", "#2A9663", "OkSoft", "#DFF3E9", "Warn", "#B4760F", "WarnSoft", "#F8EBD2",
                        "Err", "#CC3F5E", "ErrSoft", "#FBE1E7", "Info", "#4A66D4", "InfoSoft", "#E3E8FB",
                        "Hover", "#0F231D38", "Press", "#1F231D38", "Input", "#FFFFFF", "Popup", "#FFFFFF", "ScrollThumb", "#55686185" });
                    p.Res["Bg"] = V("#F7F5FD", "#EEE9FA");
                }
                else
                {
                    Base(p, new string[] {
                        "Nav", "#181425", "Surface", "#211C30", "Surface2", "#2A2440", "Stroke", "#342D4D", "StrokeStrong", "#4B4270",
                        "Text", "#EEEAFB", "Text2", "#ABA3C8", "Text3", "#766E94",
                        "Accent", "#A68CFF", "AccentSoft", "#2E2650", "OnAccent", "#150F26",
                        "Ok", "#5ED39A", "OkSoft", "#1C3329", "Warn", "#EBBD5B", "WarnSoft", "#393020",
                        "Err", "#FF7D98", "ErrSoft", "#40202B", "Info", "#8FA4FF", "InfoSoft", "#262C4F",
                        "Hover", "#14FFFFFF", "Press", "#24FFFFFF", "Input", "#1C1829", "Popup", "#2A2440", "ScrollThumb", "#55FFFFFF" });
                    p.Res["Bg"] = V("#15121E", "#1A1528");
                }
            }
            else if (family == "mint")
            {
                if (!dark)
                {
                    Base(p, new string[] {
                        "Nav", "#E5F2EB", "Surface", "#FFFFFF", "Surface2", "#F0F7F3", "Stroke", "#D6EADF", "StrokeStrong", "#B4D8C6",
                        "Text", "#15302A", "Text2", "#536F65", "Text3", "#88A398",
                        "Accent", "#0A7A60", "AccentSoft", "#D9F1E7", "OnAccent", "#FFFFFF",
                        "Ok", "#2A9556", "OkSoft", "#DDF2E4", "Warn", "#AF7419", "WarnSoft", "#F6EAD3",
                        "Err", "#C84646", "ErrSoft", "#F8E0DF", "Info", "#2B78B0", "InfoSoft", "#DDECF7",
                        "Hover", "#0F15302A", "Press", "#1F15302A", "Input", "#FFFFFF", "Popup", "#FFFFFF", "ScrollThumb", "#55536F65" });
                    p.Res["Bg"] = V("#F2F9F5", "#E8F4EE");
                }
                else
                {
                    Base(p, new string[] {
                        "Nav", "#111D19", "Surface", "#172521", "Surface2", "#1E2F2A", "Stroke", "#263B35", "StrokeStrong", "#36544A",
                        "Text", "#E5F4EE", "Text2", "#9BB8AD", "Text3", "#66837A",
                        "Accent", "#4FD1A5", "AccentSoft", "#1A3A31", "OnAccent", "#06201A",
                        "Ok", "#5BD08E", "OkSoft", "#1A3326", "Warn", "#E6BC5C", "WarnSoft", "#38301D",
                        "Err", "#F47F7F", "ErrSoft", "#3D2222", "Info", "#6FB7F0", "InfoSoft", "#1C3041",
                        "Hover", "#14FFFFFF", "Press", "#24FFFFFF", "Input", "#13201C", "Popup", "#1E2F2A", "ScrollThumb", "#55FFFFFF" });
                    p.Res["Bg"] = V("#0F1815", "#11201B");
                }
            }
            else if (family == "night")
            {
                p.Dark = true;
                Base(p, new string[] {
                    "Nav", "#14153D", "Surface2", "#2B2E7A", "Stroke", "#33368A", "StrokeStrong", "#4A4FB2",
                    "Text", "#F3F4FF", "Text2", "#B9BCEB", "Text3", "#8287C6",
                    "Accent", "#5557EE", "AccentSoft", "#2F3290", "OnAccent", "#FFFFFF",
                    "Ok", "#3BE08A", "OkSoft", "#1C4752", "Warn", "#FFC24D", "WarnSoft", "#463D35",
                    "Err", "#FF6F86", "ErrSoft", "#47284F", "Info", "#7CC4FF", "InfoSoft", "#26387A",
                    "Hover", "#1AFFFFFF", "Press", "#2BFFFFFF", "Input", "#1B1D57", "Popup", "#26286E", "ScrollThumb", "#55FFFFFF" });
                p.Res["Surface"] = V("#272A73", "#1F2163");
                p.Res["Bg"] = V("#1C1D55", "#111236");
                Tiles(p, new string[] { "#3A54DE", "#2A3CB8", "#6C45D8", "#5232BC", "#995114", "#6E3A0D", "#0D7072", "#0A5A62" });
                p.Scene = "night";
                p.Glow = true;
            }
            else if (family == "sunset")
            {
                p.Dark = true;
                Base(p, new string[] {
                    "Nav", "#1E1028", "Surface2", "#3A2347", "Stroke", "#44294F", "StrokeStrong", "#633A6E",
                    "Text", "#FFF2EC", "Text2", "#E3BFC4", "Text3", "#A98494",
                    "Accent", "#FF7A59", "AccentSoft", "#4A2737", "OnAccent", "#2A0E16",
                    "Ok", "#5FE0A0", "OkSoft", "#213B37", "Warn", "#FFC861", "WarnSoft", "#48352B",
                    "Err", "#FF7B8E", "ErrSoft", "#4A2232", "Info", "#9AB6FF", "InfoSoft", "#2F2B54",
                    "Hover", "#1AFFFFFF", "Press", "#2BFFFFFF", "Input", "#26152F", "Popup", "#36203F", "ScrollThumb", "#55FFFFFF" });
                p.Res["Surface"] = V("#38213F", "#2A1833");
                p.Res["Bg"] = V("#2B1638", "#170C1F");
                Tiles(p, new string[] { "#B5412B", "#8E2A1C", "#A8386F", "#76204F", "#8C4E0E", "#653809", "#6A47C4", "#45278F" });
                p.Scene = "sunset";
                p.Glow = true;
            }
            else if (family == "aurora")
            {
                p.Dark = true;
                Base(p, new string[] {
                    "Nav", "#0A1820", "Surface2", "#183440", "Stroke", "#1C3B47", "StrokeStrong", "#2B5563",
                    "Text", "#E9FFF8", "Text2", "#A6CFC6", "Text3", "#6C958D",
                    "Accent", "#3FE0B5", "AccentSoft", "#16423F", "OnAccent", "#04211B",
                    "Ok", "#4BE38E", "OkSoft", "#173F31", "Warn", "#FFCF66", "WarnSoft", "#3C3A28",
                    "Err", "#FF7F8F", "ErrSoft", "#43252F", "Info", "#7FC8FF", "InfoSoft", "#1A3550",
                    "Hover", "#1AFFFFFF", "Press", "#2BFFFFFF", "Input", "#0F222A", "Popup", "#173039", "ScrollThumb", "#55FFFFFF" });
                p.Res["Surface"] = V("#16303A", "#10242C");
                p.Res["Bg"] = V("#0C1C26", "#071117");
                Tiles(p, new string[] { "#0B6C64", "#09524D", "#2C6FC0", "#1C4787", "#2C7030", "#1F5523", "#6C4FCF", "#46309A" });
                p.Scene = "aurora";
                p.Glow = true;
            }
            else if (family == "meadow")
            {
                p.Dark = false;
                Base(p, new string[] {
                    "Nav", "#E4F1F7", "Surface", "#FFFFFF", "Surface2", "#F0F7F3", "Stroke", "#D5E6EC", "StrokeStrong", "#B5D0DA",
                    "Text", "#1B2A35", "Text2", "#4C5F6D", "Text3", "#7F93A0",
                    "Accent", "#2D63D2", "AccentSoft", "#DCE8FB", "OnAccent", "#FFFFFF",
                    "Ok", "#23874F", "OkSoft", "#DCF2E3", "Warn", "#A86A0D", "WarnSoft", "#FBEBCF",
                    "Err", "#C63D4E", "ErrSoft", "#FADFE3", "Info", "#2D63D2", "InfoSoft", "#DCE8FB",
                    "Hover", "#0F1B2A35", "Press", "#1F1B2A35", "Input", "#FFFFFF", "Popup", "#FFFFFF", "ScrollThumb", "#554C5F6D" });
                p.Res["Bg"] = V("#E6F3FF", "#F3FAEC");
                Tiles(p, new string[] { "#3567DA", "#2448A8", "#7B54D6", "#5534A8", "#985011", "#7A400C", "#1A7044", "#155C37" });
                p.Scene = "meadow";
            }
            else // glass
            {
                p.Glass = true;
                if (!dark)
                {
                    Base(p, new string[] {
                        "Nav", "#40FFFFFF", "Surface2", "#59FFFFFF", "StrokeStrong", "#F2FFFFFF",
                        "Text", "#111628", "Text2", "#3F4866", "Text3", "#5F6886",
                        "Accent", "#4A55E6", "AccentSoft", "#2E4A55E6", "OnAccent", "#FFFFFF",
                        "Ok", "#1B8754", "OkSoft", "#331B8754", "Warn", "#9C6308", "WarnSoft", "#339C6308",
                        "Err", "#BF3549", "ErrSoft", "#2EBF3549", "Info", "#2B63CF", "InfoSoft", "#2E2B63CF",
                        "Hover", "#40FFFFFF", "Press", "#1A000000", "Input", "#8CFFFFFF", "Popup", "#F7F8FD", "ScrollThumb", "#55141A2E" });
                    p.Res["Surface"] = V("#B8FFFFFF", "#80FFFFFF");
                    p.Res["Stroke"] = V("#F0FFFFFF", "#4DFFFFFF");
                    p.Res["Sheen"] = SheenBrush("#66FFFFFF");
                    p.Res["Bg"] = Diag("#D9E3FF", "#F1E4FF", "#D8F3EE");
                    p.GlassTint = B("#B3F2F4FB");
                    p.Blobs = new Color[] { C("#8C98ADFF"), C("#73E4A3FF"), C("#7380E3D0") };
                }
                else
                {
                    Base(p, new string[] {
                        "Nav", "#14FFFFFF", "Surface2", "#17FFFFFF", "StrokeStrong", "#66FFFFFF",
                        "Text", "#F3F5FF", "Text2", "#BCC3E0", "Text3", "#9AA2C4",
                        "Accent", "#9DAAFF", "AccentSoft", "#339DAAFF", "OnAccent", "#0B1030",
                        "Ok", "#69E0A4", "OkSoft", "#2669E0A4", "Warn", "#F2C46B", "WarnSoft", "#26F2C46B",
                        "Err", "#FF8A9B", "ErrSoft", "#26FF8A9B", "Info", "#8CC4FF", "InfoSoft", "#268CC4FF",
                        "Hover", "#1FFFFFFF", "Press", "#33FFFFFF", "Input", "#1AFFFFFF", "Popup", "#252A42", "ScrollThumb", "#55FFFFFF" });
                    p.Res["Surface"] = V("#30FFFFFF", "#17FFFFFF");
                    p.Res["Stroke"] = V("#5CFFFFFF", "#14FFFFFF");
                    p.Res["Sheen"] = SheenBrush("#26FFFFFF");
                    p.Res["Bg"] = Diag("#0F1328", "#1B1236", "#0B1F29");
                    p.GlassTint = B("#C70E1224");   // достаточно плотный, чтобы текст оставался читаемым над любым фоном
                    p.Blobs = new Color[] { C("#664B5BFF"), C("#59B04BFF"), C("#4D22C3A6") };
                }
            }
            p.Title = FamilyTitle(family) + (family == "purple" || family == "mint" || family == "glass" ? (p.Dark ? " · тёмная" : " · светлая") : "");
            if (!p.Res.ContainsKey("TileHue1"))
            {
                // обычные темы: спокойные оттенки — синий, фиолетовый, янтарный, бирюзовый
                string[] hues = p.Dark ? new string[] { "#7C9CFF", "#B08CFF", "#F0B45E", "#4FD1C5" }
                                       : new string[] { "#3B5BDB", "#7048E8", "#D9822B", "#0F9488" };
                for (int i = 0; i < 4; i++) p.Res["TileHue" + (i + 1)] = B(hues[i]);
            }
            return p;
        }

        // Цвета плиток иллюстрированных тем (раньше это были яркие градиенты на всю плитку;
        // теперь цвет даёт только мягкий оттенок в углу и значок, чтобы плитки не перетягивали внимание)
        static void Tiles(Palette p, string[] stops)
        {
            for (int i = 0; i < 4; i++) p.Res["TileHue" + (i + 1)] = B(stops[i * 2]);
        }

        // Цвет значка плитки: на тёмных темах осветлённый, на светлых — затемнённый, чтобы читался на подложке
        public static Color TileIconColor(Color hue, bool dark)
        {
            if (!dark) return Color.FromRgb((byte)(hue.R * 0.7), (byte)(hue.G * 0.7), (byte)(hue.B * 0.7));
            return Color.FromRgb((byte)(hue.R + (255 - hue.R) * 0.42), (byte)(hue.G + (255 - hue.G) * 0.42), (byte)(hue.B + (255 - hue.B) * 0.42));
        }

        // световой блик по верхней кромке стеклянных панелей
        static Brush SheenBrush(string top)
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(C(top), 0));
            g.GradientStops.Add(new GradientStop(Colors.Transparent, 0.42));
            g.Freeze();
            return g;
        }

        // ---------------- применение ----------------

        public static void Apply(Palette p, bool glassActive)
        {
            EnsureStyles();
            ResourceDictionary res = Application.Current.Resources;
            foreach (KeyValuePair<string, object> kv in p.Res) res[kv.Key] = kv.Value;
            res["WindowBg"] = (p.Glass && glassActive && p.GlassTint != null) ? p.GlassTint : (Brush)p.Res["Bg"];
            ApplyOptions(p, res);
            Current = p;
            GlassActive = p.Glass && glassActive;
            Action h = Changed;
            if (h != null) h();
        }

        static Color SolidOf(object brush)
        {
            SolidColorBrush s = brush as SolidColorBrush;
            if (s != null) return s.Color;
            GradientBrush g = brush as GradientBrush;
            return g != null && g.GradientStops.Count > 0 ? g.GradientStops[g.GradientStops.Count - 1].Color : Colors.Gray;
        }

        static Color Over(Color top, Color bottom)
        {
            double a = top.A / 255.0;
            return Color.FromRgb((byte)(top.R * a + bottom.R * (1 - a)), (byte)(top.G * a + bottom.G * (1 - a)), (byte)(top.B * a + bottom.B * (1 - a)));
        }

        static Brush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Пользовательский акцент и интенсивность бликов поверх выбранной темы
        static void ApplyOptions(Palette p, ResourceDictionary res)
        {
            AccentAdjusted = false;
            ThemeOptions o = Options;
            if (ThemeOptions.ValidAccent(o.Accent))
            {
                Color surface = Over(SolidOf(p.Res["Surface"]), SolidOf(p.Res["Bg"]));
                bool adj;
                Color a = ThemeOptions.Readable(C(o.Accent), surface, p.Dark, out adj);
                AccentAdjusted = adj;
                Brush accent = Frozen(a);
                Brush soft = p.Glass ? Frozen(Color.FromArgb(0x33, a.R, a.G, a.B)) : Frozen(ThemeOptions.Mix(surface, a, p.Dark ? 0.28 : 0.16));
                res["Accent"] = accent;
                res["AccentSoft"] = soft;
                res["OnAccent"] = Frozen(ThemeOptions.OnAccentFor(a));
                if (p.Scene == null)
                {
                    // у обычных тем значок плитки — цвет акцента; у иллюстрированных плитки свои
                    res["TileIcon"] = accent;
                    res["TileIconBg"] = soft;
                }
            }
            // блики стеклянных поверхностей масштабируются вместе со «свечением»
            LinearGradientBrush sheen = p.Res["Sheen"] as LinearGradientBrush;
            if (sheen != null && Math.Abs(o.GlowFactor - 1) > 0.01)
            {
                LinearGradientBrush s2 = sheen.Clone();
                foreach (GradientStop gs in s2.GradientStops)
                    gs.Color = Color.FromArgb((byte)Math.Min(255, gs.Color.A * o.GlowFactor), gs.Color.R, gs.Color.G, gs.Color.B);
                s2.Freeze();
                res["Sheen"] = s2;
            }
        }

        // Пересчитать скругления у всех карточек и кнопок окна (без подписок на события — утечек нет)
        public static void RescaleTree(DependencyObject root)
        {
            if (root == null) return;
            Card c = root as Card;
            if (c != null) c.Rescale();
            VButton b = root as VButton;
            if (b != null) b.Rescale();
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) RescaleTree(VisualTreeHelper.GetChild(root, i));
        }

        public static Brush Res(string key)
        {
            object o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return o as Brush ?? Brushes.Gray;
        }

        static bool stylesLoaded;

        // Стили стандартных элементов (поля ввода, полосы прокрутки, подсказки) в духе текущей темы
        public static void EnsureStyles()
        {
            if (stylesLoaded) return;
            stylesLoaded = true;
            const string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Background' Value='{DynamicResource Popup}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Stroke}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='8' Padding='10,6' MaxWidth='360'>
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Background' Value='{DynamicResource Input}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Stroke}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='FontFamily' Value='Segoe UI Variable Text, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Height' Value='36'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='10'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False'
                          VerticalAlignment='Center'
                          HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource StrokeStrong}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='VeylixThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border CornerRadius='3' Background='{DynamicResource ScrollThumb}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='VeylixPage' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Margin' Value='2,0,0,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource VeylixPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb Style='{StaticResource VeylixThumb}'/></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource VeylixPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='6'/>
        <Setter Property='MinHeight' Value='6'/>
        <Setter Property='Margin' Value='0,2,0,0'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Track x:Name='PART_Track'>
                <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource VeylixPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Style='{StaticResource VeylixThumb}'/></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource VeylixPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
            ResourceDictionary rd = (ResourceDictionary)XamlReader.Parse(xaml);
            Application.Current.Resources.MergedDictionaries.Add(rd);
        }

        // ---------------- анимации ----------------

        public static void Animate(DependencyObject target, DependencyProperty dp, double to, int ms)
        {
            IAnimatable a = target as IAnimatable;
            if (a == null) return;
            if (!Animations || ms <= 0)
            {
                a.BeginAnimation(dp, null);
                target.SetValue(dp, to);
                return;
            }
            DoubleAnimation an = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms / Options.SpeedFactor)));
            an.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            a.BeginAnimation(dp, an, HandoffBehavior.SnapshotAndReplace);
        }

        // ---------------- фабрики элементов ----------------

        public static void Fg(FrameworkElement e, string key)
        {
            if (e is TextBlock) e.SetResourceReference(TextBlock.ForegroundProperty, key);
            else if (e is Control) e.SetResourceReference(Control.ForegroundProperty, key);
        }

        public static TextBlock T(string text, double size, string key, bool bold)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = size >= 18 ? Display : Ui;
            t.FontSize = size;
            t.SetResourceReference(TextBlock.ForegroundProperty, key);
            t.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static TextBlock Wrap(string text, double size, string key)
        {
            TextBlock t = T(text, size, key, false);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            return t;
        }

        public static TextBlock Icon(int code, double size, string key)
        {
            TextBlock t = new TextBlock();
            t.Text = char.ConvertFromUtf32(code);
            t.FontFamily = Icons;
            t.FontSize = size;
            t.SetResourceReference(TextBlock.ForegroundProperty, key);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        public static StackPanel Row(params UIElement[] items)
        {
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            foreach (UIElement e in items) sp.Children.Add(e);
            return sp;
        }

        public static FrameworkElement Gap(double w, double h)
        {
            Border b = new Border();
            b.Width = w;
            b.Height = h;
            return b;
        }

        public static ColumnDefinition Col(double w)
        {
            ColumnDefinition c = new ColumnDefinition();
            c.Width = w < 0 ? GridLength.Auto : (w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w));
            return c;
        }

        public static RowDefinition RowDef(double h)
        {
            RowDefinition r = new RowDefinition();
            r.Height = h < 0 ? GridLength.Auto : (h == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h));
            return r;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Content = content;
            sv.Focusable = false;
            return sv;
        }

        // Логотип Veylix: скруглённый квадрат с градиентом акцента и буквой V из двух штрихов
        public static FrameworkElement Logo(double s)
        {
            Grid g = new Grid();
            g.Width = s;
            g.Height = s;
            Border bg = new Border();
            bg.CornerRadius = new CornerRadius(s * 0.3);
            bg.SetResourceReference(Border.BackgroundProperty, "Accent");
            g.Children.Add(bg);
            Border shine = new Border();
            shine.CornerRadius = new CornerRadius(s * 0.3);
            LinearGradientBrush sh = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
            sh.Freeze();
            shine.Background = sh;
            g.Children.Add(shine);
            Path v = new Path();
            PathFigure fig = new PathFigure();
            fig.StartPoint = new Point(s * 0.28, s * 0.30);
            fig.Segments.Add(new LineSegment(new Point(s * 0.50, s * 0.73), true));
            fig.Segments.Add(new LineSegment(new Point(s * 0.72, s * 0.30), true));
            PathGeometry geo = new PathGeometry();
            geo.Figures.Add(fig);
            v.Data = geo;
            v.Stretch = Stretch.None;
            v.StrokeThickness = s * 0.11;
            v.StrokeStartLineCap = PenLineCap.Round;
            v.StrokeEndLineCap = PenLineCap.Round;
            v.StrokeLineJoin = PenLineJoin.Round;
            v.SetResourceReference(Shape.StrokeProperty, "OnAccent");
            g.Children.Add(v);
            return g;
        }
    }

    // ===================== элементы интерфейса =====================

    // Карточка-поверхность. Для Liquid Glass поверх неё рисуется световой блик (ключ Sheen).
    public class Card : Border
    {
        readonly Grid host = new Grid();
        readonly Border sheen = new Border();
        UIElement content;

        public Card(double radius) : this(radius, "Surface", "Stroke") { }

        readonly double baseRadius;

        public Card(double radius, string bgKey, string strokeKey)
        {
            baseRadius = radius;
            BorderThickness = new Thickness(1);
            SetResourceReference(BackgroundProperty, bgKey);
            SetResourceReference(BorderBrushProperty, strokeKey);
            Rescale();
            sheen.IsHitTestVisible = false;
            sheen.SetResourceReference(Border.BackgroundProperty, "Sheen");
            host.Children.Add(sheen);
            Child = host;
            SnapsToDevicePixels = true;
        }

        public Thickness Inner = new Thickness(18);

        public void Rescale()
        {
            double r = baseRadius * Theme.CornerScale;
            CornerRadius = new CornerRadius(r);
            sheen.CornerRadius = new CornerRadius(Math.Max(0, r - 1));
        }

        public UIElement Content
        {
            get { return content; }
            set
            {
                if (content != null) host.Children.Remove(content);
                content = value;
                FrameworkElement fe = value as FrameworkElement;
                if (fe != null) fe.Margin = Inner;
                host.Children.Add(value);
            }
        }
    }

    // Кнопка: реагирует на наведение (мягкая подсветка), нажатие (лёгкое сжатие) и фокус с клавиатуры
    public class VButton : Border
    {
        public event EventHandler Click;
        readonly Grid host = new Grid();
        readonly Border overlay = new Border();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        UIElement content;
        bool pressed;
        string bgKey, strokeKey;

        readonly double baseRadius;

        public void Rescale()
        {
            double r = baseRadius >= 40 ? baseRadius : baseRadius * Theme.CornerScale;
            CornerRadius = new CornerRadius(r);
            overlay.CornerRadius = new CornerRadius(r);
        }

        public VButton(string bgKey, string strokeKey, double radius)
        {
            baseRadius = radius;
            SetKeys(bgKey, strokeKey);
            Rescale();
            overlay.IsHitTestVisible = false;
            overlay.Opacity = 0;
            overlay.SetResourceReference(Border.BackgroundProperty, "Hover");
            host.Children.Add(overlay);
            Child = host;
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = scale;
            SnapsToDevicePixels = true;
            IsEnabledChanged += delegate { Opacity = IsEnabled ? 1.0 : 0.45; Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow; };
        }

        public void SetKeys(string bg, string stroke)
        {
            bgKey = bg;
            strokeKey = stroke;
            if (bg == null) Background = Brushes.Transparent; else SetResourceReference(BackgroundProperty, bg);
            if (stroke == null) { BorderThickness = new Thickness(0); BorderBrush = null; }
            else { BorderThickness = new Thickness(1); SetResourceReference(BorderBrushProperty, stroke); }
            UpdateFocus();
        }

        public UIElement Content
        {
            get { return content; }
            set
            {
                if (content != null) host.Children.Remove(content);
                content = value;
                host.Children.Insert(0, value);
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Theme.Animate(overlay, UIElement.OpacityProperty, 1, 140);
            // Liquid Glass: при наведении кромка «ловит свет»
            if (Theme.Current != null && Theme.Current.Glass && strokeKey != null && !IsKeyboardFocused)
                SetResourceReference(BorderBrushProperty, "StrokeStrong");
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Theme.Animate(overlay, UIElement.OpacityProperty, 0, 220);
            UpdateFocus();
            if (pressed) { Theme.Animate(scale, ScaleTransform.ScaleXProperty, 1, 120); Theme.Animate(scale, ScaleTransform.ScaleYProperty, 1, 120); }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            pressed = true;
            CaptureMouse();
            overlay.SetResourceReference(Border.BackgroundProperty, "Press");
            double k = ActualWidth > 120 ? 0.985 : 0.95;
            Theme.Animate(scale, ScaleTransform.ScaleXProperty, k, 90);
            Theme.Animate(scale, ScaleTransform.ScaleYProperty, k, 90);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!pressed) return;
            pressed = false;
            ReleaseMouseCapture();
            overlay.SetResourceReference(Border.BackgroundProperty, "Hover");
            Theme.Animate(scale, ScaleTransform.ScaleXProperty, 1, 160);
            Theme.Animate(scale, ScaleTransform.ScaleYProperty, 1, 160);
            Point p = e.GetPosition(this);
            if (p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight) Fire();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Enter || e.Key == Key.Space) { Fire(); e.Handled = true; }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); UpdateFocus(); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); UpdateFocus(); }

        void UpdateFocus()
        {
            if (IsKeyboardFocused)
            {
                BorderThickness = new Thickness(2);
                SetResourceReference(BorderBrushProperty, "Accent");
            }
            else if (strokeKey != null)
            {
                BorderThickness = new Thickness(1);
                SetResourceReference(BorderBrushProperty, strokeKey);
            }
            else
            {
                BorderThickness = new Thickness(0);
            }
        }

        void Fire()
        {
            if (!IsEnabled) return;
            EventHandler h = Click;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    // Переключатель «вкл/выкл». Состояние задаёт владелец (IsOn), щелчок только сообщает о намерении.
    public class VToggle : Border
    {
        public event EventHandler Clicked;
        readonly Ellipse thumb = new Ellipse();
        readonly TranslateTransform move = new TranslateTransform(3, 0);
        bool on;

        public VToggle()
        {
            Width = 42;
            Height = 24;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            thumb.Width = 16;
            thumb.Height = 16;
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            thumb.VerticalAlignment = VerticalAlignment.Center;
            thumb.RenderTransform = move;
            Child = thumb;
            Apply(false);
            IsEnabledChanged += delegate { Opacity = IsEnabled ? 1.0 : 0.45; };
        }

        public bool IsOn
        {
            get { return on; }
            set { if (on != value) { on = value; Apply(true); } }
        }

        void Apply(bool animate)
        {
            SetResourceReference(BackgroundProperty, on ? "Accent" : "Surface2");
            SetResourceReference(BorderBrushProperty, on ? "Accent" : "StrokeStrong");
            thumb.SetResourceReference(Shape.FillProperty, on ? "OnAccent" : "Text2");
            Theme.Animate(move, TranslateTransform.XProperty, on ? 21 : 3, animate ? 160 : 0);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            Fire();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Space || e.Key == Key.Enter) { Fire(); e.Handled = true; }
        }

        void Fire()
        {
            if (!IsEnabled) return;
            EventHandler h = Clicked;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    // ===================== системное размытие (Windows 11) =====================

    public static class Backdrop
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMSBT_NONE = 1, DWMSBT_TRANSIENTWINDOW = 3;

        // Размытие подложки окна (Acrylic) появилось в Windows 11 22H2 (сборка 22621)
        public static bool OsSupportsBlur
        {
            get { return Diagnostics.WindowsBuild() >= 22621; }
        }

        // Почему размытие недоступно (или null, если доступно)
        public static string BlurUnavailableReason(bool userReduced)
        {
            if (userReduced) return "включён упрощённый режим";
            if (!OsSupportsBlur) return "нужна Windows 11 22H2 или новее";
            if (!Theme.SystemTransparencyEnabled()) return "в Windows отключены эффекты прозрачности";
            if ((RenderCapability.Tier >> 16) < 2) return "нет аппаратного ускорения графики";
            if (SystemParameters.IsRemoteSession) return "удалённый рабочий стол";
            return null;
        }

        public static void SetCorners(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int round = 2;
            try { DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4); } catch { }
        }

        // Возвращает true, если системное размытие действительно включено
        public static bool Apply(Window w, bool blur, bool dark)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return false;
            bool ok = false;
            try
            {
                int d = dark ? 1 : 0;
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);
                SetCorners(w);
                if (OsSupportsBlur)
                {
                    int type = blur ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
                    ok = DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) == 0 && blur;
                }
                HwndSource src = HwndSource.FromHwnd(h);
                if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            catch { ok = false; }
            return ok;
        }
    }
}
