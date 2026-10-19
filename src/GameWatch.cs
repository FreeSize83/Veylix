using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Veylix
{
    // Игровой режим: программы, при запуске которых обход ставится на паузу.
    // Некоторые античиты (Vanguard, FACEIT и др.) не дают запустить игру, пока загружен драйвер WinDivert.
    public static class GameWatch
    {
        public class Preset
        {
            public string Title, Hint;
            public string[] Names;
            public Preset(string title, string hint, params string[] names) { Title = title; Hint = hint; Names = names; }
        }

        // Имена процессов античитов и игр. Если у игры другое имя exe — его можно добавить вручную.
        public static readonly Preset[] Presets =
        {
            new Preset("Vanguard", "Valorant, League of Legends (античит Riot)", "vgc.exe", "valorant-win64-shipping.exe", "league of legends.exe"),
            new Preset("FACEIT", "клиент и античит FACEIT", "faceit.exe", "faceitservice.exe"),
            new Preset("Easy Anti-Cheat", "Fortnite, Apex Legends и др.", "easyanticheat.exe", "easyanticheat_eos.exe"),
            new Preset("BattlEye", "PUBG, Rainbow Six Siege, DayZ и др.", "beservice.exe", "beservice_x64.exe"),
        };

        // «C:\Games\Game.EXE» → «game.exe», «Discord» → «discord.exe»; null — не имя программы
        public static string Normalize(string s)
        {
            string t = (s ?? "").Trim().Trim('"').Trim();
            if (t.Length == 0) return null;
            if (t.Contains("\\") || t.Contains("/")) t = Path.GetFileName(t.Replace('/', '\\'));
            if (t.Length == 0 || t.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (!t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t += ".exe";
            return t.ToLowerInvariant();
        }

        // Хранение в settings.ini: имена через «|» (в именах exe этот символ запрещён, а запятая — нет)
        public static List<string> Parse(string stored)
        {
            List<string> r = new List<string>();
            foreach (string part in (stored ?? "").Split('|'))
            {
                string n = Normalize(part);
                if (n != null && !r.Contains(n)) r.Add(n);
            }
            return r;
        }

        public static string Join(IList<string> names)
        {
            return string.Join("|", new List<string>(names).ToArray());
        }

        // Первая запущенная программа из списка или null. Имена процессов — без «.exe», как их отдаёт Windows.
        public static string FindRunning(IList<string> watched, IEnumerable<string> processNames)
        {
            if (watched == null || watched.Count == 0) return null;
            HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in processNames)
            {
                if (string.IsNullOrEmpty(p)) continue;
                running.Add(p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe");
            }
            foreach (string w in watched)
                if (running.Contains(w)) return w;
            return null;
        }

        public static List<string> RunningProcessNames()
        {
            List<string> r = new List<string>();
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return r; }
            foreach (Process p in all)
            {
                try { r.Add(p.ProcessName); } catch { }
                try { p.Dispose(); } catch { }
            }
            return r;
        }

        // Понятное имя для сообщений: «vgc.exe» → «Vanguard», иначе имя файла без .exe
        public static string Pretty(string exe)
        {
            if (exe == null) return "";
            if (exe == "valorant-win64-shipping.exe") return "Valorant";
            if (exe == "league of legends.exe") return "League of Legends";
            foreach (Preset p in Presets)
                if (Array.IndexOf(p.Names, exe) >= 0) return p.Title;
            return exe.EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe;
        }

        public static bool HasPreset(IList<string> list, Preset p)
        {
            foreach (string n in p.Names) if (!list.Contains(n)) return false;
            return true;
        }
    }
}
