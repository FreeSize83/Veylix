using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Veylix
{
    // settings.ini: простые строки ключ=значение.
    // Неизвестные ключи сохраняются как есть, чтобы старые/будущие настройки не терялись.
    public class Settings
    {
        public string LastStrategy = "";
        public string Theme = "cream";        // cream | graphite | purple | mint | glass
        public string ThemeMode = "manual";   // manual | system | schedule
        public bool GlassReduced = false;     // упрощённый режим Liquid Glass (без размытия)
        public bool Animations = true;
        public bool CloseToTray = true;       // крестик прячет окно в трей, пока обход работает
        public string SkippedVersion = "";    // версия Flowseal, о которой не напоминать
        public string PreferredStrategy = ""; // стратегия, которую пользователь отметил как предпочтительную
        public string PinnedActions = DefaultPinned;   // закреплённые быстрые действия (id команд)
        public int PulseInterval = 60;        // Network Pulse: секунд между измерениями
        public int PulseHistory = 60;         // Network Pulse: сколько измерений хранить
        public string PulseTargets = "internet,discord,youtube";
        public bool Toasts = true;            // необязательные уведомления внутри окна
        public bool AppUpdateCheck = true;    // проверять обновления Veylix при запуске (только проверка, без загрузки)
        public bool CompanionVisible = true;  // пчёлка-компаньон на главной
        public bool CompanionAnimated = true; // анимации пчёлки (действуют, только если анимации включены вообще)
        public ThemeOptions Studio = new ThemeOptions();   // Theme Studio: акцент, свечение, углы, плотность, скорость

        public const string DefaultPinned = "check.services,diag.run,lists.user,lists.exclude,ipset.update,folder.open";
        public static readonly int[] PulseIntervals = { 30, 60, 120, 300 };
        public static readonly int[] PulseHistories = { 30, 60, 120 };

        readonly Dictionary<string, string> extra = new Dictionary<string, string>();
        readonly List<string> order = new List<string>();

        static string dirOverride;
        public static string MigrationNote;   // текст для журнала, если настройки перенесены из BeeBot

        public static readonly string[] ThemeIds = new string[] { "cream", "graphite", "purple", "mint", "glass", "night", "sunset", "aurora", "meadow" };

        public static string Dir
        {
            get
            {
                if (dirOverride != null) return dirOverride;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Veylix");
            }
        }

        public static string LegacyDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeeBot"); }
        }

        public static bool IsOverridden { get { return dirOverride != null; } }

        // для тестов и режима самопроверки
        public static void OverrideDir(string dir)
        {
            dirOverride = dir;
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "settings.ini"); }
        }

        public static Settings Load()
        {
            MigrateLegacy();
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    s.Set(k, v);
                }
            }
            catch { }
            return s;
        }

        void Set(string k, string v)
        {
            switch (k)
            {
                case "LastStrategy": LastStrategy = v; break;
                case "Theme": if (Array.IndexOf(ThemeIds, v) >= 0) Theme = v; break;
                case "ThemeMode": if (v == "manual" || v == "system" || v == "schedule") ThemeMode = v; break;
                case "GlassReduced": GlassReduced = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "Animations": Animations = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "CloseToTray": CloseToTray = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "SkippedVersion": SkippedVersion = v; break;
                case "PreferredStrategy": PreferredStrategy = v; break;
                case "PinnedActions": PinnedActions = v; break;
                case "PulseInterval": PulseInterval = Pick(v, PulseIntervals, 60); break;
                case "PulseHistory": PulseHistory = Pick(v, PulseHistories, 60); break;
                case "PulseTargets": PulseTargets = v; break;
                case "Toasts": Toasts = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "AppUpdateCheck": AppUpdateCheck = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "CompanionVisible": CompanionVisible = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "CompanionAnimated": CompanionAnimated = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)); break;
                case "StudioAccent": Studio.Accent = ThemeOptions.ValidAccent(v) ? v.ToUpperInvariant() : ""; break;
                case "StudioGlow": Studio.Glow = ThemeOptions.Pick(v, ThemeOptions.Glows, 100); break;
                case "StudioCorners": Studio.Corners = ThemeOptions.PickName(v, ThemeOptions.CornerNames, "standard"); break;
                case "StudioDensity": Studio.Density = ThemeOptions.PickName(v, ThemeOptions.DensityNames, "normal"); break;
                case "StudioSpeed": Studio.Speed = ThemeOptions.PickName(v, ThemeOptions.SpeedNames, "normal"); break;
                default:
                    if (!extra.ContainsKey(k)) order.Add(k);
                    extra[k] = v;
                    break;
            }
        }

        // число из списка допустимых, иначе значение по умолчанию (защита от слишком частых запросов)
        static int Pick(string v, int[] allowed, int def)
        {
            int x;
            if (int.TryParse(v, out x) && Array.IndexOf(allowed, x) >= 0) return x;
            return def;
        }

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("LastStrategy=").Append(LastStrategy).Append("\r\n");
            sb.Append("Theme=").Append(Theme).Append("\r\n");
            sb.Append("ThemeMode=").Append(ThemeMode).Append("\r\n");
            sb.Append("GlassReduced=").Append(GlassReduced ? "1" : "0").Append("\r\n");
            sb.Append("Animations=").Append(Animations ? "1" : "0").Append("\r\n");
            sb.Append("CloseToTray=").Append(CloseToTray ? "1" : "0").Append("\r\n");
            sb.Append("SkippedVersion=").Append(SkippedVersion).Append("\r\n");
            sb.Append("PreferredStrategy=").Append(PreferredStrategy).Append("\r\n");
            sb.Append("PinnedActions=").Append(PinnedActions).Append("\r\n");
            sb.Append("PulseInterval=").Append(PulseInterval).Append("\r\n");
            sb.Append("PulseHistory=").Append(PulseHistory).Append("\r\n");
            sb.Append("PulseTargets=").Append(PulseTargets).Append("\r\n");
            sb.Append("Toasts=").Append(Toasts ? "1" : "0").Append("\r\n");
            sb.Append("AppUpdateCheck=").Append(AppUpdateCheck ? "1" : "0").Append("\r\n");
            sb.Append("CompanionVisible=").Append(CompanionVisible ? "1" : "0").Append("\r\n");
            sb.Append("CompanionAnimated=").Append(CompanionAnimated ? "1" : "0").Append("\r\n");
            sb.Append("StudioAccent=").Append(Studio.Accent).Append("\r\n");
            sb.Append("StudioGlow=").Append(Studio.Glow).Append("\r\n");
            sb.Append("StudioCorners=").Append(Studio.Corners).Append("\r\n");
            sb.Append("StudioDensity=").Append(Studio.Density).Append("\r\n");
            sb.Append("StudioSpeed=").Append(Studio.Speed).Append("\r\n");
            foreach (string k in order)
                sb.Append(k).Append('=').Append(extra[k]).Append("\r\n");
            return sb.ToString();
        }

        // Атомарная запись: сначала временный файл, затем замена. Возвращает текст ошибки или null.
        public string Save()
        {
            string tmp = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(tmp, Serialize(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return ex.Message;
            }
        }

        // Однократный перенос пользовательских данных из %LOCALAPPDATA%\BeeBot. Старая папка не удаляется.
        static void MigrateLegacy()
        {
            if (dirOverride != null) return;
            try
            {
                string old = LegacyDir;
                if (Directory.Exists(Dir) || !Directory.Exists(old)) return;
                Directory.CreateDirectory(Dir);
                CopyIfExists(Path.Combine(old, "settings.ini"), FilePath);

                string oldLists = Path.Combine(old, "zapret", "lists");
                string newLists = Path.Combine(Dir, "zapret", "lists");
                if (Directory.Exists(oldLists))
                {
                    foreach (string f in Directory.GetFiles(oldLists))
                    {
                        string n = Path.GetFileName(f).ToLowerInvariant();
                        if (n.EndsWith("-user.txt") || n == "ipset-all.txt")
                            CopyIfExists(f, Path.Combine(newLists, Path.GetFileName(f)));
                    }
                }
                CopyIfExists(Path.Combine(old, "zapret", "utils", "game_filter.enabled"),
                             Path.Combine(Dir, "zapret", "utils", "game_filter.enabled"));
                MigrationNote = "Настройки и пользовательские списки перенесены из " + old + " (старая папка не тронута).";
            }
            catch (Exception ex)
            {
                MigrationNote = "Не удалось перенести данные BeeBot: " + ex.Message;
            }
        }

        static void CopyIfExists(string from, string to)
        {
            if (!File.Exists(from) || File.Exists(to)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, false);
        }
    }
}
