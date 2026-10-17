using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace Veylix
{
    // Пользовательские настройки Theme Studio. Накладываются поверх любой из тем,
    // включая Liquid Glass и иллюстрированные, не меняя сами темы.
    public class ThemeOptions
    {
        public string Accent = "";          // "" — акцент темы; иначе #RRGGBB
        public int Glow = 100;              // интенсивность свечения и бликов, %
        public string Corners = "standard"; // compact | standard | round
        public string Density = "normal";   // compact | normal | spacious
        public string Speed = "normal";     // slow | normal | fast

        public static readonly int[] Glows = { 0, 50, 100, 150 };
        public static readonly string[] CornerNames = { "compact", "standard", "round" };
        public static readonly string[] DensityNames = { "compact", "normal", "spacious" };
        public static readonly string[] SpeedNames = { "slow", "normal", "fast" };
        public static readonly string[] AccentPresets = { "#5557EE", "#7152DF", "#2D63D2", "#0A7A60", "#A3591F", "#C2185B", "#D9573C", "#00838F" };
        public const string Format = "veylix-theme";
        public const int MaxImportBytes = 16 * 1024;

        public double CornerScale { get { return Corners == "compact" ? 0.5 : Corners == "round" ? 1.45 : 1.0; } }
        public double DensityFactor { get { return Density == "compact" ? 0.6 : Density == "spacious" ? 1.45 : 1.0; } }
        public double SpeedFactor { get { return Speed == "slow" ? 0.6 : Speed == "fast" ? 1.7 : 1.0; } }
        public double GlowFactor { get { return Glow / 100.0; } }

        public bool IsDefault
        {
            get { return Accent.Length == 0 && Glow == 100 && Corners == "standard" && Density == "normal" && Speed == "normal"; }
        }

        public ThemeOptions Clone()
        {
            ThemeOptions o = new ThemeOptions();
            o.Accent = Accent; o.Glow = Glow; o.Corners = Corners; o.Density = Density; o.Speed = Speed;
            return o;
        }

        public static bool ValidAccent(string v)
        {
            return v != null && Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$");
        }

        public static int Pick(string v, int[] allowed, int def)
        {
            int x;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out x) && Array.IndexOf(allowed, x) >= 0) return x;
            return def;
        }

        public static string PickName(string v, string[] allowed, string def)
        {
            return Array.IndexOf(allowed, v) >= 0 ? v : def;
        }

        // ---------- цвет ----------

        static double Lum(Color c)
        {
            Func<double, double> ch = delegate(double v) { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        public static double Contrast(Color a, Color b)
        {
            double x = Lum(a) + 0.05, y = Lum(b) + 0.05;
            return Math.Max(x, y) / Math.Min(x, y);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // Текст поверх акцентной кнопки: белый или почти чёрный — что контрастнее
        public static Color OnAccentFor(Color accent)
        {
            Color white = Colors.White, dark = Color.FromRgb(0x10, 0x14, 0x18);
            return Contrast(white, accent) >= Contrast(dark, accent) ? white : dark;
        }

        // Акцент используется и как цвет ссылок, и как заливка: доводим его до контраста ≥ 3:1 с карточкой,
        // затемняя на светлых темах и осветляя на тёмных. adjusted = true, если цвет пришлось поправить.
        public static Color Readable(Color accent, Color surface, bool darkTheme, out bool adjusted)
        {
            adjusted = false;
            Color c = accent;
            Color target = darkTheme ? Colors.White : Colors.Black;
            for (int i = 0; i < 20 && Contrast(c, surface) < 3.0; i++)
            {
                c = Mix(c, target, 0.12);
                adjusted = true;
            }
            return c;
        }

        // ---------- экспорт / импорт (только данные, строгая проверка каждого поля) ----------

        public string ToJson(string baseTheme)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": \"").Append(Format).Append("\",\n");
            sb.Append("  \"version\": 1,\n");
            sb.Append("  \"baseTheme\": \"").Append(Array.IndexOf(Settings.ThemeIds, baseTheme) >= 0 ? baseTheme : "cream").Append("\",\n");
            sb.Append("  \"accent\": \"").Append(ValidAccent(Accent) ? Accent : "").Append("\",\n");
            sb.Append("  \"glow\": ").Append(Glow.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"corners\": \"").Append(Corners).Append("\",\n");
            sb.Append("  \"density\": \"").Append(Density).Append("\",\n");
            sb.Append("  \"speed\": \"").Append(Speed).Append("\"\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // Возвращает настройки или null с текстом ошибки. Неизвестные поля игнорируются (их число — в ignored).
        public static ThemeOptions FromJson(string json, out string baseTheme, out int ignored, out string error)
        {
            baseTheme = null;
            ignored = 0;
            error = null;
            if (json == null || json.Length == 0) { error = "файл пустой"; return null; }
            if (json.Length > MaxImportBytes) { error = "файл больше 16 КБ — это не тема Veylix"; return null; }
            Dictionary<string, object> d;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.RecursionLimit = 4;
                d = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception) { error = "файл не является корректным JSON"; return null; }
            if (d == null) { error = "ожидался объект JSON"; return null; }
            object fmt;
            if (!d.TryGetValue("format", out fmt) || !(fmt is string) || (string)fmt != Format) { error = "это не файл темы Veylix (нет \"format\": \"" + Format + "\")"; return null; }
            object ver;
            if (!d.TryGetValue("version", out ver) || !(ver is int) || (int)ver != 1) { error = "неподдерживаемая версия формата темы"; return null; }

            ThemeOptions o = new ThemeOptions();
            foreach (KeyValuePair<string, object> kv in d)
            {
                string s = kv.Value as string;
                switch (kv.Key)
                {
                    case "format": case "version": break;
                    case "baseTheme":
                        if (s != null && Array.IndexOf(Settings.ThemeIds, s) >= 0) baseTheme = s;
                        else { error = "неизвестная базовая тема"; return null; }
                        break;
                    case "accent":
                        if (s == null || (s.Length > 0 && !ValidAccent(s))) { error = "акцент должен быть в виде #RRGGBB"; return null; }
                        o.Accent = s.ToUpperInvariant();
                        break;
                    case "glow":
                        if (!(kv.Value is int) || Array.IndexOf(Glows, (int)kv.Value) < 0) { error = "свечение: допустимо 0, 50, 100 или 150"; return null; }
                        o.Glow = (int)kv.Value;
                        break;
                    case "corners":
                        if (s == null || Array.IndexOf(CornerNames, s) < 0) { error = "недопустимое значение corners"; return null; }
                        o.Corners = s;
                        break;
                    case "density":
                        if (s == null || Array.IndexOf(DensityNames, s) < 0) { error = "недопустимое значение density"; return null; }
                        o.Density = s;
                        break;
                    case "speed":
                        if (s == null || Array.IndexOf(SpeedNames, s) < 0) { error = "недопустимое значение speed"; return null; }
                        o.Speed = s;
                        break;
                    default:
                        ignored++;
                        break;
                }
            }
            return o;
        }
    }
}
