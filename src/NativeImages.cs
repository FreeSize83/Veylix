using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Veylix
{
    // Предкомпиляция штатным ngen.exe из .NET Framework: код Veylix перестаёт компилироваться при каждом запуске,
    // создание окна ускоряется более чем вдвое. Выполняется в фоне с низким приоритетом, один раз на версию и путь.
    public static class NativeImages
    {
        public static string Key(string version, string exePath)
        {
            return version + "|" + (exePath ?? "").ToLowerInvariant();
        }

        public static bool Needed(string stored, string key)
        {
            return !string.Equals(stored ?? "", key, StringComparison.Ordinal);
        }

        public static string NgenPath()
        {
            string fw = Path.GetDirectoryName(typeof(object).Assembly.Location);   // каталог .NET Framework нужной разрядности
            string p = Path.Combine(fw ?? "", "ngen.exe");
            return File.Exists(p) ? p : null;
        }

        public static void Schedule(Settings cfg, Action save, LogStore log)
        {
            string exe = Assembly.GetEntryAssembly() != null ? Assembly.GetEntryAssembly().Location : null;
            if (string.IsNullOrEmpty(exe) || !Diagnostics.IsAdmin()) return;   // ngen требует прав администратора
            string key = Key(Program.Version, exe);
            if (!Needed(cfg.NativeImageFor, key)) return;
            string ngen = NgenPath();
            if (ngen == null) return;

            // отмечаем сразу: при сбое не повторяем на каждом запуске, следующая попытка — с новой версией
            cfg.NativeImageFor = key;
            save();
            Task.Run(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(ngen, "install \"" + exe + "\" /nologo");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    using (Process p = Process.Start(psi))
                    {
                        try { p.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                        if (!p.WaitForExit(10 * 60 * 1000))
                        {
                            try { p.Kill(); } catch { }
                            log.Add("info", "Оптимизация запуска: предкомпиляция заняла слишком долго и остановлена.");
                            return;
                        }
                        log.Add("info", p.ExitCode == 0
                            ? "Оптимизация запуска: Veylix предкомпилирован, следующие запуски будут быстрее."
                            : "Оптимизация запуска: предкомпиляция не удалась (код " + p.ExitCode + "), Veylix работает как обычно.");
                    }
                }
                catch (Exception ex) { log.Add("info", "Оптимизация запуска пропущена: " + ex.Message); }
            });
        }
    }
}
