using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace Veylix
{
    public class DiagItem
    {
        public string Group, Name, Value, Hint;
        public string Level;   // ok | warn | err | info

        public DiagItem(string group, string level, string name, string value, string hint)
        {
            Group = group; Level = level; Name = name; Value = value ?? ""; Hint = hint ?? "";
        }
    }

    // Сбор сведений о системе. Только чтение: никакие настройки Windows здесь не меняются.
    public static class Diagnostics
    {
        public static List<DiagItem> Collect(Engine engine, int strategyCount)
        {
            List<DiagItem> r = new List<DiagItem>();
            const string G1 = "Приложение", G2 = "Flowseal / winws", G3 = "Службы и конфликты", G4 = "Сеть";

            // ---------- приложение ----------
            r.Add(new DiagItem(G1, "info", "Veylix", Program.Version, null));
            bool admin = IsAdmin();
            r.Add(new DiagItem(G1, admin ? "ok" : "err", "Права администратора", admin ? "есть" : "нет",
                admin ? null : "WinDivert не запустится без прав администратора. Запустите Veylix от имени администратора."));
            r.Add(new DiagItem(G1, "info", "Windows", OsDescription(), null));
            r.Add(new DiagItem(G1, "info", ".NET Framework", NetFrameworkVersion(), null));
            r.Add(new DiagItem(G1, "info", "Папка данных", Settings.Dir, null));

            // ---------- файлы и процесс ----------
            string ver = engine.InstalledVersion();
            r.Add(new DiagItem(G2, ver != null ? "ok" : "warn", "Версия Flowseal", ver ?? "не определена", ver == null ? "Не найден service.bat с номером версии." : null));
            r.Add(new DiagItem(G2, "info", "Файлы обхода", engine.Root, engine.IsEmbeddedRoot ? "встроенный комплект" : "внешняя папка рядом с exe"));
            string[] files = { "bin\\winws.exe", "bin\\WinDivert.dll", "bin\\WinDivert64.sys", "bin\\cygwin1.dll" };
            List<string> missing = new List<string>();
            foreach (string f in files) if (!File.Exists(Path.Combine(engine.Root, f))) missing.Add(f);
            r.Add(new DiagItem(G2, missing.Count == 0 ? "ok" : "err", "Исполняемые файлы",
                missing.Count == 0 ? "на месте" : "нет: " + string.Join(", ", missing),
                missing.Count == 0 ? null : "Файлы часто удаляет антивирус (ложное срабатывание на WinDivert). Добавьте папку в исключения и переустановите."));
            r.Add(new DiagItem(G2, strategyCount > 0 ? "ok" : "err", "Стратегий найдено", strategyCount.ToString(), null));

            int pid = engine.ProcessId;
            if (pid > 0)
            {
                TimeSpan up = DateTime.Now - engine.StartedAt;
                r.Add(new DiagItem(G2, "ok", "winws.exe (Veylix)", "PID " + pid + " · " + (engine.Current != null ? engine.Current.Display : "?") +
                    " · " + string.Format("{0:00}:{1:00}:{2:00}", (int)up.TotalHours, up.Minutes, up.Seconds) +
                    " · " + (engine.ProcessMemory / (1024 * 1024)) + " МБ", null));
            }
            else
            {
                r.Add(new DiagItem(G2, "info", "winws.exe (Veylix)", "не запущен", null));
            }
            List<int> ext = engine.ExternalPids();
            if (ext.Count > 0)
                r.Add(new DiagItem(G2, "warn", "Посторонний winws.exe", "PID " + string.Join(", ", ext),
                    "Запущен другой экземпляр обхода (bat-файл, служба zapret или другой лаунчер). Veylix остановит его при запуске своей стратегии."));

            // ---------- службы ----------
            Dictionary<string, ServiceController> services = new Dictionary<string, ServiceController>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ServiceController sc in ServiceController.GetServices())
                    services[sc.ServiceName] = sc;
                foreach (ServiceController sc in ServiceController.GetDevices())
                    services[sc.ServiceName] = sc;
            }
            catch { }

            ServiceController bfe;
            if (services.TryGetValue("BFE", out bfe))
            {
                bool run = SafeStatus(bfe) == ServiceControllerStatus.Running;
                r.Add(new DiagItem(G3, run ? "ok" : "err", "Base Filtering Engine", run ? "работает" : "остановлена",
                    run ? null : "Без службы BFE WinDivert не работает. Включите её в services.msc (Тип запуска: Автоматически)."));
            }
            ServiceController wd;
            if (services.TryGetValue("WinDivert", out wd))
                r.Add(new DiagItem(G3, "info", "Драйвер WinDivert", StatusText(SafeStatus(wd)), null));
            else
                r.Add(new DiagItem(G3, "info", "Драйвер WinDivert", "не загружен", pid > 0 ? "Странно: winws работает, а драйвер не найден." : null));
            ServiceController zs;
            if (services.TryGetValue("zapret", out zs))
            {
                bool run = SafeStatus(zs) == ServiceControllerStatus.Running;
                r.Add(new DiagItem(G3, run ? "warn" : "info", "Служба zapret (service.bat)", StatusText(SafeStatus(zs)),
                    run ? "Служба запускает свой winws.exe и конфликтует с Veylix. Удалите её через service.bat → Remove Services." : null));
            }

            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, ServiceController> kv in services)
            {
                string n = kv.Key + " " + SafeDisplay(kv.Value);
                string low = n.ToLowerInvariant();
                if (low.Contains("killer")) conflicts.Add("Killer (" + kv.Key + ")");
                else if (low.Contains("intel") && low.Contains("connectivity") && low.Contains("network")) conflicts.Add("Intel Connectivity Network Service");
                else if (kv.Key.Equals("TracSrvWrapper", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("EPWD", StringComparison.OrdinalIgnoreCase)) conflicts.Add("Check Point (" + kv.Key + ")");
                else if (low.Contains("smartbyte")) conflicts.Add("SmartByte");
                else if (low.Contains("goodbyedpi")) conflicts.Add("GoodbyeDPI");
            }
            if (ProcessRunning("AdguardSvc")) conflicts.Add("AdGuard (AdguardSvc.exe)");
            r.Add(new DiagItem(G3, conflicts.Count == 0 ? "ok" : "warn", "Конфликтующее ПО",
                conflicts.Count == 0 ? "не найдено" : string.Join(", ", conflicts),
                conflicts.Count == 0 ? null : "Эти программы известны конфликтами с zapret (см. диагностику в service.bat). Отключите их на время проверки."));
            foreach (ServiceController sc in services.Values) { try { sc.Dispose(); } catch { } }

            // ---------- сеть ----------
            bool up2 = false;
            List<string> vpn = new List<string>();
            List<string> dns = new List<string>();
            try
            {
                up2 = NetworkInterface.GetIsNetworkAvailable();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || VpnRoute.LooksLikeVpn(d))
                        vpn.Add(ni.Description);
                    try
                    {
                        foreach (System.Net.IPAddress a in ni.GetIPProperties().DnsAddresses)
                        {
                            string s = a.ToString();
                            if (!s.StartsWith("fec0:") && !dns.Contains(s)) dns.Add(s);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            r.Add(new DiagItem(G4, up2 ? "ok" : "err", "Сетевое подключение", up2 ? "есть" : "нет активных подключений", null));
            string route = VpnRoute.Active();
            if (route != null)
                r.Add(new DiagItem(G4, "warn", "Интернет идёт через VPN", route,
                    "Весь трафик уходит в туннель VPN, поэтому winws его не обрабатывает, а проверки показывают работу VPN, а не стратегии. Чтобы проверить обход, выключите VPN или его режим TUN."));
            else if (vpn.Count > 0)
                r.Add(new DiagItem(G4, "info", "VPN-адаптеры", string.Join(", ", vpn), "Адаптеры есть, но интернет идёт мимо них — на обход это не влияет."));
            r.Add(new DiagItem(G4, "info", "DNS-серверы", dns.Count > 0 ? string.Join(", ", dns) : "не определены", null));
            string proxy = NetCheck.SystemProxyDescription();
            r.Add(new DiagItem(G4, proxy == null ? "ok" : "warn", "Системный прокси", proxy ?? "не используется",
                proxy == null ? null : "Браузеры пойдут через прокси, а не напрямую. Если прокси не нужен — отключите его в параметрах Windows. Veylix настройки прокси не меняет."));
            string ts = TcpTimestamps();
            if (ts != null)
                r.Add(new DiagItem(G4, ts == "enabled" ? "ok" : "warn", "TCP timestamps", ts == "enabled" ? "включены" : "выключены",
                    ts == "enabled" ? null : "Часть стратегий (fooling=ts) требует timestamps. Включить вручную: netsh interface tcp set global timestamps=enabled (от администратора)."));
            return r;
        }

        static ServiceControllerStatus SafeStatus(ServiceController sc)
        {
            try { return sc.Status; } catch { return 0; }
        }

        static string SafeDisplay(ServiceController sc)
        {
            try { return sc.DisplayName; } catch { return ""; }
        }

        static string StatusText(ServiceControllerStatus s)
        {
            switch (s)
            {
                case ServiceControllerStatus.Running: return "работает";
                case ServiceControllerStatus.Stopped: return "остановлена";
                case ServiceControllerStatus.StartPending: return "запускается";
                case ServiceControllerStatus.StopPending: return "останавливается";
                default: return s.ToString();
            }
        }

        static bool ProcessRunning(string name)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(name);
                foreach (Process p in ps) p.Dispose();
                return ps.Length > 0;
            }
            catch { return false; }
        }

        public static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static int WindowsBuild()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int b;
                    if (k != null && int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out b)) return b;
                }
            }
            catch { }
            return Environment.OSVersion.Version.Build;
        }

        static string OsDescription()
        {
            int b = WindowsBuild();
            return (b >= 22000 ? "Windows 11" : "Windows 10") + " (сборка " + b + ")";
        }

        static string NetFrameworkVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    int rel = k != null ? Convert.ToInt32(k.GetValue("Release")) : 0;
                    if (rel >= 533320) return "4.8.1";
                    if (rel >= 528040) return "4.8";
                    if (rel >= 461808) return "4.7.2";
                    if (rel >= 394802) return "4.6.2";
                    return "4.x (" + rel + ")";
                }
            }
            catch { return Environment.Version.ToString(); }
        }

        // "enabled" / "disabled" / null — только чтение, ничего не включаем сами
        static string TcpTimestamps()
        {
            string o;
            if (Engine.RunHidden("netsh.exe", "interface tcp show global", 8000, out o) != 0) return null;
            foreach (string line in o.Split('\n'))
            {
                string l = line.ToLowerInvariant();
                // строка локализована («Метки времени RFC 1323» / «RFC 1323 Timestamps»), но номер RFC и значение — нет
                if (!l.Contains("timestamps") && !l.Contains("rfc 1323")) continue;
                if (l.Contains("enabled") || l.Contains("включ")) return "enabled";
                if (l.Contains("disabled") || l.Contains("отключ") || l.Contains("выключ")) return "disabled";
            }
            return null;
        }

        public static string Report(List<DiagItem> items, ServiceResult[] checks)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Отчёт диагностики Veylix ").Append(Program.Version).Append(" — ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\r\n");
            string group = null;
            foreach (DiagItem d in items)
            {
                if (d.Group != group) { group = d.Group; sb.Append("\r\n[").Append(group).Append("]\r\n"); }
                sb.Append("  ").Append(Mark(d.Level)).Append(' ').Append(d.Name).Append(": ").Append(d.Value).Append("\r\n");
                if (d.Hint.Length > 0) sb.Append("      ").Append(d.Hint).Append("\r\n");
            }
            if (checks != null)
            {
                sb.Append("\r\n[Доступность]\r\n");
                foreach (ServiceResult s in checks)
                {
                    sb.Append("  ").Append(s.Name).Append(": ").Append(NetCheck.StatusWord(s.Status)).Append(" — ").Append(s.Summary).Append("\r\n");
                    foreach (ProbeResult p in s.Probes)
                        sb.Append("      ").Append(p.Passed ? "✓ " : "✕ ").Append(p.Target.Host).Append("  ").Append(p.Timing())
                          .Append(p.Passed ? "" : "  [" + p.Stage + "] " + p.Detail).Append("\r\n");
                }
            }
            return LogStore.Sanitize(sb.ToString());
        }

        static string Mark(string level)
        {
            if (level == "ok") return "[ OK ]";
            if (level == "warn") return "[ !! ]";
            if (level == "err") return "[ERR ]";
            return "[ -- ]";
        }
    }
}
