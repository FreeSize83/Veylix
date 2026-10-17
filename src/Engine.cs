using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Veylix
{
    public class Strategy
    {
        public string Display;   // например "ALT3"
        public string BatPath;   // полный путь к исходному .bat (он не изменяется)
        public string Note;      // короткое описание
        public bool Warn;        // помечена автором как "NOT RECOMMENDED"

        // последний результат проверки (автоподбор или ручная проверка при работе этой стратегии)
        public int LastScore = -1, LastMax;
        public string LastSummary;
        public DateTime LastTested;
        public int LastLatency = -1;          // средняя задержка TCP+TLS по ответившим адресам, мс
        public ServiceResult[] LastResults;   // подробные результаты последней проверки (Discord, YouTube)
    }

    public class Engine
    {
        // Меняйте при обновлении встроенного архива zapret.zip, чтобы файлы переизвлеклись
        public const string DataVersion = "1.10.3-r1";
        public const string EmbeddedFlowsealVersion = "1.10.3";
        public const string UpdatedMarker = ".flowseal-version";   // пишется установщиком обновлений

        public string Root, Bin, Lists, Utils;
        public Strategy Current;
        public DateTime StartedAt;

        Process proc;
        bool driverTouched;   // winws запускался после последней выгрузки WinDivert
        bool closed;          // приложение завершается — новые запуски запрещены
        readonly object sync = new object();

        public event Action<string, string> Log;   // (уровень, текст)
        public event Action<int> Exited;           // winws.exe завершился сам (код выхода или -1)

        public string GameMode = "disabled";       // disabled | all | tcp | udp
        string tcpRange = "1024-65535";
        string udpRange = "1024-65535";

        public Engine(string root)
        {
            SetRoot(root);
        }

        public void SetRoot(string root)
        {
            Root = root;
            Bin = Path.Combine(root, "bin");
            Lists = Path.Combine(root, "lists");
            Utils = Path.Combine(root, "utils");
            Directory.CreateDirectory(Lists);
            Directory.CreateDirectory(Utils);
            LoadGameMode();
        }

        void L(string level, string text)
        {
            Action<string, string> h = Log;
            if (h != null) h(level, text);
        }

        // ---------- расположение файлов zapret ----------

        public static string Locate()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string a = Path.Combine(exeDir, "zapret");
            if (File.Exists(Path.Combine(a, "bin", "winws.exe"))) return a;
            if (File.Exists(Path.Combine(exeDir, "bin", "winws.exe"))) return exeDir;
            return ExtractEmbedded();
        }

        public static string EmbeddedTarget
        {
            get { return Path.Combine(Settings.Dir, "zapret"); }
        }

        static string ExtractEmbedded()
        {
            string target = EmbeddedTarget;
            string marker = Path.Combine(target, ".version");
            string exe = Path.Combine(target, "bin", "winws.exe");
            if (File.Exists(exe))
            {
                if (File.Exists(marker) && File.ReadAllText(marker).Trim() == DataVersion)
                    return target;
                // пользователь установил более новую версию Flowseal через «Обновления» — не затираем её встроенной
                string upd = Path.Combine(target, UpdatedMarker);
                if (File.Exists(upd) && Updater.CompareVersions(File.ReadAllText(upd).Trim(), EmbeddedFlowsealVersion) >= 0)
                    return target;
            }

            Directory.CreateDirectory(target);
            Assembly asm = Assembly.GetExecutingAssembly();
            List<string> failed = new List<string>();
            using (Stream s = asm.GetManifestResourceStream("zapret.zip"))
            {
                if (s == null) throw new InvalidOperationException("Ресурс zapret.zip не найден внутри exe.");
                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) continue;
                        string dest = Updater.SafeEntryPath(target, e.FullName);
                        if (dest == null) continue;
                        string name = Path.GetFileName(dest).ToLowerInvariant();
                        bool keep = File.Exists(dest) &&
                                    (name.EndsWith("-user.txt") || name == "ipset-all.txt" || name == "game_filter.enabled");
                        if (keep) continue;
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            e.ExtractToFile(dest, true);
                        }
                        catch { failed.Add(e.FullName); }
                    }
                }
            }
            if (!File.Exists(exe))
                throw new InvalidOperationException("Не удалось распаковать bin\\winws.exe (вероятно, файл заблокирован антивирусом или запущен).");
            // маркер пишем только при полной распаковке, иначе попробуем ещё раз при следующем запуске
            if (failed.Count == 0) File.WriteAllText(marker, DataVersion);
            return target;
        }

        // Версия Flowseal из service.bat (set "LOCAL_VERSION=1.10.3")
        public string InstalledVersion()
        {
            try
            {
                string f = Path.Combine(Root, "service.bat");
                if (!File.Exists(f)) return null;
                Match m = Regex.Match(File.ReadAllText(f), "LOCAL_VERSION=([0-9A-Za-z.\\-]+)\"?");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public bool IsEmbeddedRoot
        {
            get { return string.Equals(Path.GetFullPath(Root).TrimEnd('\\'), Path.GetFullPath(EmbeddedTarget).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        }

        // ---------- стратегии (читаем оригинальные .bat, ничего в них не меняя) ----------

        public List<Strategy> LoadStrategies()
        {
            List<Strategy> list = new List<Strategy>();
            foreach (string f in Directory.GetFiles(Root, "general*.bat"))
            {
                string baseName = Path.GetFileNameWithoutExtension(f);
                Match m = Regex.Match(baseName, @"\((.+)\)");
                Strategy s = new Strategy();
                s.BatPath = f;
                s.Display = m.Success ? m.Groups[1].Value : "general";

                string d = s.Display;
                if (d == "general") s.Note = "Основная";
                else if (Regex.IsMatch(d, @"^ALT\d*$")) s.Note = "Альтернативная";
                else if (d.IndexOf("FAKE TLS AUTO", StringComparison.OrdinalIgnoreCase) >= 0) s.Note = "Fake TLS · авто";
                else if (d.IndexOf("SIMPLE FAKE", StringComparison.OrdinalIgnoreCase) >= 0) s.Note = "Simple fake";
                else if (d.IndexOf("EXP", StringComparison.OrdinalIgnoreCase) >= 0) s.Note = "Экспериментальная";
                else s.Note = "Стратегия";

                try
                {
                    string head = File.ReadAllText(f, Encoding.UTF8);
                    if (head.IndexOf("NOT RECOMMENDED", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        s.Warn = true;
                        s.Note = "Не рекомендуется";
                    }
                }
                catch { }
                list.Add(s);
            }
            list.Sort(CompareStrategies);
            return list;
        }

        static void Key(string d, out int g, out int n)
        {
            g = 2; n = 0;
            if (d == "general") { g = 0; return; }
            Match m = Regex.Match(d, @"^ALT(\d*)$");
            if (m.Success)
            {
                g = 1;
                n = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
            }
        }

        static int CompareStrategies(Strategy a, Strategy b)
        {
            int ga, na, gb, nb;
            Key(a.Display, out ga, out na);
            Key(b.Display, out gb, out nb);
            if (ga != gb) return ga.CompareTo(gb);
            if (na != nb) return na.CompareTo(nb);
            return string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
        }

        static string Sub(string s, string key, string val)
        {
            return Regex.Replace(s, Regex.Escape(key), delegate(Match m) { return val; }, RegexOptions.IgnoreCase);
        }

        // Берём из .bat всё, что идёт после winws.exe" — так же делает и сам service.bat
        public string BuildArgs(Strategy s)
        {
            string[] lines = File.ReadAllLines(s.BatPath, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!found)
                {
                    if (!line.StartsWith("start ", StringComparison.OrdinalIgnoreCase)) continue;
                    int k = line.IndexOf("winws.exe\"", StringComparison.OrdinalIgnoreCase);
                    if (k < 0) continue;
                    found = true;
                    line = line.Substring(k + 10).Trim();
                }
                bool cont = line.EndsWith("^");
                if (cont) line = line.Substring(0, line.Length - 1).Trim();
                if (line.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(line);
                }
                if (!cont) break;
            }
            if (!found) return null;

            string args = sb.ToString();
            args = Sub(args, "%BIN%", Bin + "\\");
            args = Sub(args, "%LISTS%", Lists + "\\");
            args = Sub(args, "%GameFilterTCP%", GameTcp);
            args = Sub(args, "%GameFilterUDP%", GameUdp);
            args = Sub(args, "%GameFilter%", GameTcp);
            return args;
        }

        // ---------- запуск / остановка ----------

        public bool IsRunning
        {
            get
            {
                Process p = proc;
                try { return p != null && !p.HasExited; }
                catch { return false; }
            }
        }

        public int ProcessId
        {
            get
            {
                Process p = proc;
                try { return (p != null && !p.HasExited) ? p.Id : 0; }
                catch { return 0; }
            }
        }

        public long ProcessMemory
        {
            get
            {
                Process p = proc;
                try
                {
                    if (p == null || p.HasExited) return 0;
                    p.Refresh();
                    return p.WorkingSet64;
                }
                catch { return 0; }
            }
        }

        // PID чужих winws.exe (из .bat, службы zapret или другой копии лаунчера)
        public List<int> ExternalPids()
        {
            List<int> r = new List<int>();
            int own = ProcessId;
            try
            {
                foreach (Process x in Process.GetProcessesByName("winws"))
                {
                    using (x)
                    {
                        if (x.Id != own) r.Add(x.Id);
                    }
                }
            }
            catch { }
            return r;
        }

        public bool ExternalRunning()
        {
            return ExternalPids().Count > 0;
        }

        public void EnsureUserLists()
        {
            string a = Path.Combine(Lists, "ipset-exclude-user.txt");
            string b = Path.Combine(Lists, "list-general-user.txt");
            string c = Path.Combine(Lists, "list-exclude-user.txt");
            if (!File.Exists(a)) File.WriteAllText(a, "203.0.113.113/32\r\n");
            if (!File.Exists(b)) File.WriteAllText(b, "# Never leave this file empty\r\ndomain.example.abc\r\n");
            if (!File.Exists(c)) File.WriteAllText(c, "domain.example.abc\r\n");
            string ip = Path.Combine(Lists, "ipset-all.txt");
            if (!File.Exists(ip)) File.WriteAllText(ip, "");
        }

        // Запуск стратегии. Предыдущий winws.exe останавливается, но драйвер WinDivert
        // между перезапусками не выгружается — это экономит до 20 секунд на каждом переключении.
        public bool Start(Strategy s)
        {
            lock (sync)
            {
                if (closed) return false;
                StopProcessLocked();
                try
                {
                    EnsureUserLists();
                    string args = BuildArgs(s);
                    if (args == null)
                    {
                        L("err", "Не удалось найти строку запуска winws.exe в " + Path.GetFileName(s.BatPath));
                        return false;
                    }

                    string exe = Path.Combine(Bin, "winws.exe");
                    if (!File.Exists(exe))
                    {
                        L("err", "Не найден " + exe + " (антивирус мог удалить файл).");
                        return false;
                    }

                    ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                    psi.WorkingDirectory = Bin;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;                 // <- никаких дополнительных окон
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;

                    Process p = new Process();
                    p.StartInfo = psi;
                    p.EnableRaisingEvents = true;
                    p.OutputDataReceived += delegate(object o, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data)) L("proc", e.Data.Trim());
                    };
                    p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data)) L("proc", e.Data.Trim());
                    };
                    p.Exited += delegate(object o, EventArgs e)
                    {
                        // Без lock(sync): WaitForExit в Stop() может вызвать этот обработчик синхронно,
                        // а блокировка здесь привела бы к взаимоблокировке. Хватает атомарной замены.
                        if (!object.ReferenceEquals(Interlocked.CompareExchange(ref proc, null, p), p)) return;
                        Current = null;
                        int code = -1;
                        try { code = p.ExitCode; } catch { }
                        Action<int> h = Exited;
                        if (h != null) h(code);
                    };

                    driverTouched = true;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    proc = p;
                    Current = s;
                    StartedAt = DateTime.Now;
                    return true;
                }
                catch (Exception ex)
                {
                    L("err", "Ошибка запуска: " + ex.Message);
                    return false;
                }
            }
        }

        // Полная остановка: процесс + выгрузка драйвера WinDivert (как «Remove services» в service.bat).
        public void Stop()
        {
            lock (sync)
            {
                StopProcessLocked();
                if (driverTouched)
                {
                    CleanupDivert();
                    driverTouched = false;
                }
            }
        }

        // Вызывается при выходе: после него Start() больше ничего не запустит
        public void Shutdown()
        {
            lock (sync) { closed = true; }
            Stop();
        }

        void StopProcessLocked()
        {
            Process p = Interlocked.Exchange(ref proc, null);
            Current = null;
            if (p != null)
            {
                try
                {
                    if (!p.HasExited) p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
                try { p.Dispose(); } catch { }
            }
            List<int> killed = KillStray();
            if (killed.Count > 0)
            {
                L("warn", "Остановлен посторонний winws.exe (PID " + string.Join(", ", killed) + ") — два обхода одновременно конфликтуют.");
                driverTouched = true;
            }
        }

        List<int> KillStray()
        {
            List<int> killed = new List<int>();
            try
            {
                foreach (Process x in Process.GetProcessesByName("winws"))
                {
                    using (x)
                    {
                        try
                        {
                            int id = x.Id;
                            x.Kill();
                            x.WaitForExit(2000);
                            killed.Add(id);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return killed;
        }

        void CleanupDivert()
        {
            string o;
            // как в service.bat: net stop ждёт полной остановки драйвера, затем удаляем службу
            RunHidden("net.exe", "stop WinDivert", 15000, out o);
            RunHidden("sc.exe", "delete WinDivert", 8000, out o);
        }

        public static int RunHidden(string file, string args, int timeoutMs, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // читаем потоки асинхронно, иначе при большом выводе процесс может зависнуть на записи
                    Task<string> so = p.StandardOutput.ReadToEndAsync();
                    Task<string> se = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    try { Task.WaitAll(new Task[] { so, se }, 2000); } catch { }
                    output = (so.IsCompleted ? so.Result : "") + (se.IsCompleted ? se.Result : "");
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -2;
            }
        }

        // ---------- игровой фильтр (формат файла совместим с service.bat) ----------

        string GameFlagFile { get { return Path.Combine(Utils, "game_filter.enabled"); } }

        public string GameTcp
        {
            get { return (GameMode == "all" || GameMode == "tcp") ? tcpRange : "12"; }
        }

        public string GameUdp
        {
            get { return (GameMode == "all" || GameMode == "udp") ? udpRange : "12"; }
        }

        static bool ValidRange(string v)
        {
            return Regex.IsMatch(v, @"^\d+(-\d+)?(,\d+(-\d+)?)*$");
        }

        void LoadGameMode()
        {
            GameMode = "disabled";
            try
            {
                if (!File.Exists(GameFlagFile)) return;
                foreach (string raw in File.ReadAllLines(GameFlagFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    string k = line, v = "";
                    int i = line.IndexOf('=');
                    if (i >= 0) { k = line.Substring(0, i).Trim(); v = line.Substring(i + 1).Trim(); }
                    k = k.ToLowerInvariant();
                    if (k == "mode") GameMode = v.ToLowerInvariant();
                    else if (k == "all") GameMode = "all";
                    else if (k == "udp") { if (v.Length == 0) GameMode = "udp"; else if (ValidRange(v)) udpRange = v; }
                    else if (k == "tcp") { if (v.Length == 0) GameMode = "tcp"; else if (ValidRange(v)) tcpRange = v; }
                }
                if (GameMode != "all" && GameMode != "tcp" && GameMode != "udp") GameMode = "disabled";
            }
            catch { GameMode = "disabled"; }
        }

        public void SetGameMode(string mode)
        {
            GameMode = mode;
            try
            {
                if (mode == "disabled")
                {
                    if (File.Exists(GameFlagFile)) File.Delete(GameFlagFile);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("mode=" + mode + "\r\n");
                if (tcpRange != "1024-65535") sb.Append("tcp=" + tcpRange + "\r\n");
                if (udpRange != "1024-65535") sb.Append("udp=" + udpRange + "\r\n");
                File.WriteAllText(GameFlagFile, sb.ToString());
            }
            catch (Exception ex) { L("err", "Не удалось сохранить игровой фильтр: " + ex.Message); }
        }

        public string NextGameMode()
        {
            if (GameMode == "disabled") return "all";
            if (GameMode == "all") return "tcp";
            if (GameMode == "tcp") return "udp";
            return "disabled";
        }

        // ---------- проверка обновлений Flowseal при запуске (тот же флаг, что у service.bat) ----------

        string CheckUpdatesFlag { get { return Path.Combine(Utils, "check_updates.enabled"); } }

        public bool CheckUpdatesEnabled
        {
            get { return File.Exists(CheckUpdatesFlag); }
        }

        public string SetCheckUpdates(bool on)
        {
            try
            {
                if (on) File.WriteAllText(CheckUpdatesFlag, "ENABLED\r\n");
                else if (File.Exists(CheckUpdatesFlag)) File.Delete(CheckUpdatesFlag);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------- режим IPSet (та же логика, что в service.bat) ----------

        string IpsetFile { get { return Path.Combine(Lists, "ipset-all.txt"); } }

        public string GetIpsetMode()
        {
            try
            {
                if (!File.Exists(IpsetFile)) return "any";
                string t = File.ReadAllText(IpsetFile);
                if (t.Trim().Length == 0) return "any";
                if (t.Contains("203.0.113.113/32")) return "none";
                return "loaded";
            }
            catch { return "any"; }
        }

        // none -> any -> loaded -> none. Возвращает текст ошибки или null.
        public string CycleIpset()
        {
            try
            {
                string list = IpsetFile;
                string backup = list + ".backup";
                string mode = GetIpsetMode();
                if (mode == "loaded")
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(list, backup);
                    File.WriteAllText(list, "203.0.113.113/32\r\n");
                }
                else if (mode == "none")
                {
                    File.WriteAllText(list, "");
                }
                else
                {
                    if (!File.Exists(backup)) return "Нет сохранённого списка IPSet. Сначала нажмите «Обновить IPSet».";
                    if (File.Exists(list)) File.Delete(list);
                    File.Move(backup, list);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";

        public string UpdateIpset()
        {
            string tmp = IpsetFile + ".download";
            try
            {
                Http.Download(IpsetUrl, tmp, 30000, 64L * 1024 * 1024, null);
                if (new FileInfo(tmp).Length < 1000)
                {
                    File.Delete(tmp);
                    return "Скачанный файл подозрительно мал — список не заменён.";
                }
                // в списке должны быть только IP/подсети и комментарии
                int bad = 0, good = 0;
                foreach (string raw in File.ReadLines(tmp))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (Regex.IsMatch(line, @"^[0-9A-Fa-f:.]+(/\d{1,3})?$")) good++;
                    else if (++bad > 5) break;
                }
                if (bad > 5 || good == 0)
                {
                    File.Delete(tmp);
                    return "Скачанный файл не похож на список IP-адресов — список не заменён.";
                }
                File.Copy(tmp, IpsetFile, true);
                File.Delete(tmp);
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return Http.Describe(ex);
            }
        }

        // ---------- автозапуск через планировщик (без UAC при входе в систему) ----------

        public const string TaskName = "VeylixLauncher";
        public const string LegacyTaskName = "BeeBotLauncher";

        static bool TaskExists(string name)
        {
            string o;
            return RunHidden("schtasks.exe", "/Query /TN " + name, 10000, out o) == 0;
        }

        public static bool IsAutorun()
        {
            return TaskExists(TaskName) || TaskExists(LegacyTaskName);
        }

        public static string SetAutorun(bool on, string exe)
        {
            string o = "";
            int code = 0;
            if (on)
            {
                code = RunHidden("schtasks.exe",
                    "/Create /TN " + TaskName + " /SC ONLOGON /RL HIGHEST /F /TR \"\\\"" + exe + "\\\" --autostart\"",
                    20000, out o);
                if (code == 0 && TaskExists(LegacyTaskName))
                    RunHidden("schtasks.exe", "/Delete /TN " + LegacyTaskName + " /F", 20000, out o);
            }
            else
            {
                foreach (string name in new string[] { TaskName, LegacyTaskName })
                {
                    if (!TaskExists(name)) continue;
                    code = RunHidden("schtasks.exe", "/Delete /TN " + name + " /F", 20000, out o);
                    if (code != 0) break;
                }
            }
            return code == 0 ? null : (string.IsNullOrEmpty(o) ? "код " + code : o.Trim());
        }
    }
}
