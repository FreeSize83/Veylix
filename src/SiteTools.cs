using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Veylix
{
    // ===================== DNS через HTTPS (RFC 8484) =====================

    public class DohProvider
    {
        public string Id, Title, Template, QueryUrl;
        public string[] V4, V6;
        public DohProvider(string id, string title, string template, string queryUrl, string[] v4, string[] v6)
        {
            Id = id; Title = title; Template = template; QueryUrl = queryUrl; V4 = v4; V6 = v6;
        }
    }

    public class DnsAnswer
    {
        public bool Ok;              // сервер ответил (даже если домена нет)
        public int Rcode = -1;       // 0 — ок, 3 — NXDOMAIN
        public List<IPAddress> Addresses = new List<IPAddress>();
        public int Ms = -1;
        public string Error;

        public bool HasAddress { get { return Addresses.Count > 0; } }
        public bool NxDomain { get { return Rcode == 3; } }

        public string Describe()
        {
            if (!Ok) return Error ?? "нет ответа";
            if (NxDomain) return "домен не найден (NXDOMAIN)";
            if (!HasAddress) return "адресов нет";
            List<string> s = new List<string>();
            foreach (IPAddress a in Addresses) { s.Add(a.ToString()); if (s.Count == 3) break; }
            return string.Join(", ", s.ToArray()) + (Addresses.Count > 3 ? " и ещё " + (Addresses.Count - 3) : "");
        }
    }

    public static class Doh
    {
        // Оба сервиса есть во встроенном списке DoH Windows 11, поэтому их можно включить штатно.
        // Quad9 тоже там есть, но его DoH отвечает только по HTTP/2, и проверить его отсюда (.NET 4.8) нельзя.
        public static readonly DohProvider[] Providers =
        {
            new DohProvider("cloudflare", "Cloudflare", "https://cloudflare-dns.com/dns-query", "https://1.1.1.1/dns-query",
                new string[] { "1.1.1.1", "1.0.0.1" }, new string[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }),
            new DohProvider("google", "Google", "https://dns.google/dns-query", "https://8.8.8.8/dns-query",
                new string[] { "8.8.8.8", "8.8.4.4" }, new string[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }),
        };

        public static DohProvider Find(string id)
        {
            foreach (DohProvider p in Providers) if (p.Id == id) return p;
            return null;
        }

        // Какой сервис настроен по списку адресов DNS-серверов (или null)
        public static DohProvider ByServers(IEnumerable<string> servers)
        {
            foreach (string s in servers)
                foreach (DohProvider p in Providers)
                    if (Array.IndexOf(p.V4, s) >= 0 || Array.IndexOf(p.V6, s) >= 0) return p;
            return null;
        }

        // Запрос A-записи в формате DNS (id = 0, как рекомендует RFC 8484 для кэширования)
        public static byte[] BuildQuery(string host)
        {
            List<byte> b = new List<byte>();
            b.AddRange(new byte[] { 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });   // id, RD, 1 вопрос
            foreach (string label in host.TrimEnd('.').Split('.'))
            {
                byte[] l = Encoding.ASCII.GetBytes(label);
                if (l.Length == 0 || l.Length > 63) throw new ArgumentException("Неверное имя домена: " + host);
                b.Add((byte)l.Length);
                b.AddRange(l);
            }
            b.Add(0);
            b.AddRange(new byte[] { 0, 1, 0, 1 });   // QTYPE A, QCLASS IN
            return b.ToArray();
        }

        public static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static int SkipName(byte[] m, int p)
        {
            while (p < m.Length)
            {
                int len = m[p];
                if ((len & 0xC0) == 0xC0) return p + 2;   // ссылка на имя выше
                if (len == 0) return p + 1;
                p += len + 1;
            }
            throw new InvalidDataException("обрезанный ответ DNS");
        }

        // Разбор ответа: код ответа и все A-записи (CNAME пропускаются)
        public static DnsAnswer ParseResponse(byte[] m)
        {
            DnsAnswer a = new DnsAnswer();
            if (m == null || m.Length < 12) throw new InvalidDataException("слишком короткий ответ DNS");
            a.Ok = true;
            a.Rcode = m[3] & 0x0F;
            int qd = (m[4] << 8) | m[5];
            int an = (m[6] << 8) | m[7];
            int p = 12;
            for (int i = 0; i < qd; i++) p = SkipName(m, p) + 4;
            for (int i = 0; i < an; i++)
            {
                p = SkipName(m, p);
                if (p + 10 > m.Length) throw new InvalidDataException("обрезанный ответ DNS");
                int type = (m[p] << 8) | m[p + 1];
                int rdlen = (m[p + 8] << 8) | m[p + 9];
                p += 10;
                if (p + rdlen > m.Length) throw new InvalidDataException("обрезанный ответ DNS");
                if (type == 1 && rdlen == 4)
                    a.Addresses.Add(new IPAddress(new byte[] { m[p], m[p + 1], m[p + 2], m[p + 3] }));
                p += rdlen;
            }
            return a;
        }

        public static DnsAnswer Resolve(DohProvider pr, string host, int timeoutMs)
        {
            DnsAnswer res;
            DateTime t0 = DateTime.Now;
            try
            {
                string url = pr.QueryUrl + "?dns=" + Base64Url(BuildQuery(host));
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Accept = "application/dns-message";
                req.UserAgent = Http.UserAgent;
                req.KeepAlive = false;
                req.Proxy = null;   // DNS-запрос идёт напрямую, как его сделает Windows
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                using (Stream s = r.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[4096];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > 65536) throw new InvalidDataException("слишком большой ответ DNS");
                    }
                    res = ParseResponse(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                res = new DnsAnswer();
                res.Error = pr.Title + ": " + Http.Describe(ex);
            }
            res.Ms = (int)(DateTime.Now - t0).TotalMilliseconds;
            return res;
        }

        // Обычный (системный) DNS — тот, что выдаёт провайдер или роутер
        public static DnsAnswer ResolveSystem(string host, int timeoutMs)
        {
            DnsAnswer a = new DnsAnswer();
            DateTime t0 = DateTime.Now;
            Task<IPAddress[]> t = Dns.GetHostAddressesAsync(host);
            try
            {
                if (!t.Wait(timeoutMs)) { a.Error = "DNS-сервер не ответил за " + timeoutMs / 1000 + " с"; }
                else
                {
                    a.Ok = true;
                    a.Rcode = 0;
                    foreach (IPAddress ip in t.Result)
                        if (ip.AddressFamily == AddressFamily.InterNetwork) a.Addresses.Add(ip);
                }
            }
            catch (AggregateException ex)
            {
                SocketException se = ex.GetBaseException() as SocketException;
                if (se != null && se.SocketErrorCode == SocketError.HostNotFound) { a.Ok = true; a.Rcode = 3; }
                else a.Error = "ошибка DNS: " + (se != null ? se.SocketErrorCode.ToString() : ex.GetBaseException().Message);
            }
            a.Ms = (int)(DateTime.Now - t0).TotalMilliseconds;
            return a;
        }

        // Заглушка провайдера: локальный, нулевой или служебный адрес вместо настоящего
        public static bool IsStub(IPAddress a)
        {
            byte[] b = a.GetAddressBytes();
            if (b.Length != 4) return IPAddress.IsLoopback(a);
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }

        // Системный DNS «врёт»: домена нет или адрес-заглушка, а DNS через HTTPS отдаёт настоящие адреса
        public static bool LooksPoisoned(DnsAnswer system, DnsAnswer doh)
        {
            if (doh == null || !doh.Ok || !doh.HasAddress) return false;
            if (system == null || !system.Ok) return false;
            if (system.NxDomain || !system.HasAddress) return true;
            foreach (IPAddress a in system.Addresses) if (!IsStub(a)) return false;
            return true;
        }
    }

    // ===================== проверка отдельного сайта =====================

    public class SiteReport
    {
        public string Host;
        public string Level = "idle";     // ok | warn | err | info
        public string Title = "", Text = "";
        public DnsAnswer System, Secure;
        public ProbeResult Probe;
        public string CoveredBy;          // запись списка, которая уже покрывает домен
        public string CoveredFile;
        public string ExcludedBy;         // запись в списке исключений
        public bool DnsPoisoned;
        public bool OfferAdd;             // имеет смысл добавить в «мои домены»
        public bool OfferStart;           // обход выключен — включить
        public bool OfferStrategy;        // домен в обходе, но стратегия не справляется
        public bool OfferDnsFix;
    }

    public static class SiteCheck
    {
        public const string UserListFile = "list-general-user.txt";
        public const string Placeholder = "domain.example.abc";
        public static readonly string[] IncludeFiles = { "list-general-user.txt", "list-general.txt", "list-google.txt" };
        public static readonly string[] ExcludeFiles = { "list-exclude-user.txt", "list-exclude.txt" };

        // «https://www.Example.com:8443/path?q» → «www.example.com»; null — не похоже на домен
        public static string NormalizeHost(string input)
        {
            string s = (input ?? "").Trim().Trim('"', '\'', '<', '>').Trim();
            if (s.Length == 0) return null;
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int cut = s.IndexOfAny(new char[] { '/', '?', '#', '\\' });
            if (cut >= 0) s = s.Substring(0, cut);
            int at = s.LastIndexOf('@');
            if (at >= 0) s = s.Substring(at + 1);
            int colon = s.LastIndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            s = s.Trim().TrimEnd('.').ToLowerInvariant();
            if (s.StartsWith("*.")) s = s.Substring(2);
            if (s.Length == 0) return null;
            try { s = new IdnMapping().GetAscii(s); }   // кириллические домены → punycode
            catch { return null; }
            IPAddress ip;
            if (IPAddress.TryParse(s, out ip)) return null;   // адрес, а не домен
            string[] labels = s.Split('.');
            if (labels.Length < 2 || s.Length > 253) return null;
            foreach (string l in labels)
            {
                if (l.Length == 0 || l.Length > 63 || l.StartsWith("-") || l.EndsWith("-")) return null;
                foreach (char c in l)
                    if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return null;
            }
            return s;
        }

        // Запись для списка: без «www.» — списки zapret действуют и на поддомены
        public static string ListEntry(string host)
        {
            if (host == null) return null;
            return host.StartsWith("www.") && host.IndexOf('.', 4) > 0 ? host.Substring(4) : host;
        }

        public static List<string> ParseEntries(IEnumerable<string> lines)
        {
            List<string> r = new List<string>();
            foreach (string raw in lines)
            {
                string l = (raw ?? "").Trim().ToLowerInvariant();
                if (l.Length == 0 || l.StartsWith("#") || l == Placeholder) continue;
                if (!r.Contains(l)) r.Add(l);
            }
            return r;
        }

        // Запись, под которую попадает домен (сам домен или родительский), или null
        public static string FindCovering(IEnumerable<string> entries, string host)
        {
            foreach (string e in entries)
                if (host == e || host.EndsWith("." + e, StringComparison.Ordinal)) return e;
            return null;
        }

        // Свои домены пользователя — без блока наборов «Игры и приложения»
        public static List<string> ReadUserEntries(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<string>();
                List<string> lines = new List<string>();
                bool inside = false;
                foreach (string l in File.ReadAllLines(path))
                {
                    string t = l.Trim();
                    if (t == AppPresets.BlockStart) { inside = true; continue; }
                    if (t == AppPresets.BlockEnd) { inside = false; continue; }
                    if (!inside) lines.Add(l);
                }
                return ParseEntries(lines);
            }
            catch { return new List<string>(); }
        }

        public static List<string> ReadEntries(string path)
        {
            try { return File.Exists(path) ? ParseEntries(File.ReadAllLines(path)) : new List<string>(); }
            catch { return new List<string>(); }
        }

        // Ищем домен в нескольких списках; file — имя списка, где нашёлся
        public static string FindInFiles(string listsDir, string[] files, string host, out string file)
        {
            file = null;
            foreach (string f in files)
            {
                string e = FindCovering(ReadEntries(Path.Combine(listsDir, f)), host);
                if (e != null) { file = f; return e; }
            }
            return null;
        }

        // Добавить запись в конец файла; false — уже есть
        public static bool AddEntry(string path, string entry)
        {
            List<string> have = ReadEntries(path);
            if (have.Contains(entry)) return false;
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            if (text.Length > 0 && !text.EndsWith("\n")) text += "\r\n";
            File.WriteAllText(path, text + entry + "\r\n");
            return true;
        }

        // Удалить запись; файл не оставляем пустым (так требует Flowseal)
        public static bool RemoveEntry(string path, string entry)
        {
            if (!File.Exists(path)) return false;
            List<string> keep = new List<string>();
            bool removed = false, inside = false;
            foreach (string l in File.ReadAllLines(path))
            {
                string t = l.Trim();
                if (t == AppPresets.BlockStart) inside = true;
                else if (t == AppPresets.BlockEnd) inside = false;
                // блок наборов не трогаем — им управляют галочки «Игры и приложения»
                else if (!inside && string.Equals(t, entry, StringComparison.OrdinalIgnoreCase)) { removed = true; continue; }
                keep.Add(l);
            }
            if (!removed) return false;
            if (ParseEntries(keep).Count == 0 && !keep.Exists(delegate(string l) { return l.Trim() == Placeholder; })) keep.Add(Placeholder);
            File.WriteAllText(path, string.Join("\r\n", keep.ToArray()) + "\r\n");
            return true;
        }

        // Вывод по результатам: что с сайтом и что предложить
        public static SiteReport Diagnose(string host, DnsAnswer system, DnsAnswer secure, ProbeResult probe, bool bypassOn, string coveredBy, string excludedBy)
        {
            SiteReport r = new SiteReport();
            r.Host = host;
            r.System = system;
            r.Secure = secure;
            r.Probe = probe;
            r.CoveredBy = coveredBy;
            r.ExcludedBy = excludedBy;
            r.DnsPoisoned = Doh.LooksPoisoned(system, secure);
            bool covered = coveredBy != null && excludedBy == null;

            if (probe == null)
            {
                // до соединения дело не дошло — адреса нет ни у системного DNS, ни у DNS через HTTPS
                if (secure != null && secure.Ok && secure.NxDomain && (system == null || !system.HasAddress))
                {
                    r.Level = "err";
                    r.Title = "Такого домена нет";
                    r.Text = "Ни DNS провайдера, ни независимый DNS через HTTPS не знают " + host + ". Проверьте, нет ли опечатки в адресе.";
                }
                else
                {
                    r.Level = "warn";
                    r.Title = "Не удалось узнать адрес сайта";
                    r.Text = "DNS провайдера: " + (system != null ? system.Describe() : "—") + ". DNS через HTTPS: " + (secure != null ? secure.Describe() : "—") +
                             ". Проверьте подключение к интернету и повторите.";
                }
                return r;
            }

            string lead = r.DnsPoisoned ? "DNS провайдера скрывает этот сайт: " + system.Describe() + ". Настоящий адрес получен через DNS по HTTPS. " : "";
            if (r.DnsPoisoned) r.OfferDnsFix = true;

            if (probe.Passed)
            {
                if (r.DnsPoisoned)
                {
                    r.Level = "warn";
                    r.Title = "Блокировка через DNS";
                    r.Text = lead + "По настоящему адресу сайт отвечает, так что поможет защищённый DNS — его можно включить ниже.";
                }
                else
                {
                    r.Level = "ok";
                    r.Title = "Сайт открывается";
                    r.Text = "Соединение и ответ в порядке" + (bypassOn && covered ? " — домен в обходе (" + coveredBy + ")." : ".") +
                             " Если в браузере он всё равно не грузится — проверьте расширения, VPN и прокси, а ещё очистите кэш браузера.";
                }
                return r;
            }

            bool dpi = probe.Status == ProbeStatus.Partial ||
                       (probe.Stage == "TLS" && (probe.Status == ProbeStatus.Timeout || probe.Status == ProbeStatus.Blocked)) ||
                       (probe.Stage == "HTTP" && probe.Status == ProbeStatus.Blocked);
            bool tcp = probe.Stage == "TCP" && (probe.Status == ProbeStatus.Timeout || probe.Status == ProbeStatus.Blocked);

            if (dpi)
            {
                r.Level = "err";
                r.Title = probe.Status == ProbeStatus.Partial ? "Провайдер обрывает загрузку" : "Блокировка по имени сайта (DPI)";
                string what = lead + "Провайдер распознаёт сайт в зашифрованном соединении и " +
                              (probe.Status == ProbeStatus.Partial ? "обрывает передачу" : "рвёт его") + ". Именно это обходит Veylix. ";
                if (excludedBy != null)
                    r.Text = what + "Но домен в списке исключений (" + excludedBy + ") — обход его не трогает. Уберите его оттуда.";
                else if (!covered)
                {
                    r.OfferAdd = true;
                    r.Text = what + "Домена нет в списках обхода — добавьте его, и Veylix перезапустит обход.";
                }
                else if (!bypassOn)
                {
                    r.OfferStart = true;
                    r.Text = what + "Домен уже в списке (" + coveredBy + "), но обход выключен.";
                }
                else
                {
                    r.OfferStrategy = true;
                    r.Text = what + "Домен уже в обходе (" + coveredBy + "), но текущая стратегия не справляется — попробуйте другую или автоподбор.";
                }
                return r;
            }

            if (tcp)
            {
                r.Level = "warn";
                r.Title = "Сервер не отвечает";
                r.Text = lead + "Подключиться к " + (probe.Address.Length > 0 ? probe.Address : host) + " не удалось (" + probe.Detail + "). " +
                         "Так выглядит блокировка по IP-адресу или сайт сейчас просто не работает. Обход по доменам тут обычно бессилен: может помочь режим IPSet «Все адреса» или VPN.";
                return r;
            }

            r.Level = "warn";
            r.Title = "Сайт ответил ошибкой";
            r.Text = lead + "Этап " + probe.Stage + ": " + probe.Detail + ". Похоже на неполадку самого сайта, а не на блокировку.";
            return r;
        }
    }

    // ===================== настройки DNS Windows =====================

    public class AdapterDns
    {
        public int Index;
        public string Id, Name;
        public bool Dhcp4 = true, Dhcp6 = true, HasV6;
        public bool Encrypted;   // Windows шифрует запросы к этим DNS-серверам (DNS через HTTPS)
        public List<string> Servers4 = new List<string>(), Servers6 = new List<string>();
    }

    public static class DnsFix
    {
        public static string BackupPath { get { return Path.Combine(Settings.Dir, "dns-backup.ini"); } }

        // Встроенный DoH есть в Windows 11 (сборка 22000+), в Windows 10 — только смена серверов
        public static bool DohSupported
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int b;
                        return k != null && int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out b) && b >= 22000;
                    }
                }
                catch { return false; }
            }
        }

        // Активные подключения с выходом в сеть
        public static List<AdapterDns> ActiveAdapters()
        {
            List<AdapterDns> r = new List<AdapterDns>();
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return r; }
            foreach (NetworkInterface ni in all)
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (!ni.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    IPInterfaceProperties ip = ni.GetIPProperties();
                    bool gw = false;
                    foreach (GatewayIPAddressInformation g in ip.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gw = true;
                    if (!gw || IsVirtual(ni.Name + " " + ni.Description)) continue;
                    AdapterDns a = new AdapterDns();
                    a.Index = ip.GetIPv4Properties().Index;
                    a.Id = ni.Id;
                    a.Name = ni.Name;
                    foreach (IPAddress d in ip.DnsAddresses)
                    {
                        if (d.AddressFamily == AddressFamily.InterNetwork) a.Servers4.Add(d.ToString());
                        else if (d.AddressFamily == AddressFamily.InterNetworkV6 && !d.IsIPv6SiteLocal) a.Servers6.Add(d.ToString());
                    }
                    if (a.Servers4.Count == 0 && a.Servers6.Count == 0) continue;
                    a.HasV6 = ni.Supports(NetworkInterfaceComponent.IPv6) && a.Servers6.Count > 0;   // DNS по IPv6 меняем, только если он в ходу   // подключение без DNS не участвует в разрешении имён
                    a.Dhcp4 = string.IsNullOrEmpty(StaticServers(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + a.Id));
                    a.Dhcp6 = string.IsNullOrEmpty(StaticServers(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\" + a.Id));
                    r.Add(a);
                }
                catch { }
            }
            return r;
        }

        // Виртуальные сети (VPN, игровые LAN) не трогаем: их DNS настраивает своя программа
        public static bool IsVirtual(string nameAndDescription)
        {
            string s = (nameAndDescription ?? "").ToLowerInvariant();
            foreach (string k in new string[] { "vpn", "radmin", "zerotier", "hamachi", "tap-windows", "wireguard", "wintun", "virtualbox host-only", "vmware network adapter", "openvpn", "tailscale" })
                if (s.Contains(k)) return true;
            return false;
        }

        // То же, что ActiveAdapters, плюс включено ли шифрование для их DNS-серверов (медленнее: вызывает netsh)
        public static List<AdapterDns> Snapshot()
        {
            List<AdapterDns> list = ActiveAdapters();
            if (!DohSupported) return list;
            Dictionary<string, bool> cache = new Dictionary<string, bool>();
            foreach (AdapterDns a in list)
            {
                List<string> all = new List<string>(a.Servers4);
                all.AddRange(a.Servers6);
                bool on = all.Count > 0;
                foreach (string ip in all)
                {
                    bool v;
                    if (!cache.TryGetValue(ip, out v)) { v = EncryptionOn(ip); cache[ip] = v; }
                    if (!v) { on = false; break; }
                }
                a.Encrypted = on;
            }
            return list;
        }

        public static bool EncryptionOn(string ip)
        {
            string o;
            if (Engine.RunHidden("netsh.exe", "dns show encryption server=" + ip, 10000, out o) != 0) return false;
            return ParseAutoUpgrade(o);
        }

        // Вывод netsh локализован, поэтому смотрим на порядок строк «имя : значение»:
        // шаблон DoH, автоматическое обновление (yes/no), резерв UDP
        public static bool ParseAutoUpgrade(string netshOutput)
        {
            List<string> values = new List<string>();
            bool body = false;
            foreach (string raw in (netshOutput ?? "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("---")) { body = true; continue; }
                if (!body) continue;
                int i = l.IndexOf(": ", StringComparison.Ordinal);
                if (i > 0) values.Add(l.Substring(i + 2).Trim());
            }
            return values.Count >= 2 && values[1].Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        static string StaticServers(string key)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                    return k == null ? null : Convert.ToString(k.GetValue("NameServer"));
            }
            catch { return null; }
        }

        // ---- резервная копия прежних настроек ----

        public static string Serialize(string providerId, IList<AdapterDns> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Provider=").Append(providerId).Append("\r\n");
            foreach (AdapterDns a in list)
                sb.Append("Adapter=").Append(a.Index).Append('|').Append(a.Dhcp4 ? "dhcp" : string.Join(",", a.Servers4.ToArray()))
                  .Append('|').Append(a.HasV6 ? (a.Dhcp6 ? "dhcp" : string.Join(",", a.Servers6.ToArray())) : "-")
                  .Append('|').Append(a.Name.Replace('|', ' ')).Append("\r\n");
            return sb.ToString();
        }

        public static List<AdapterDns> Deserialize(string text, out string providerId)
        {
            providerId = null;
            List<AdapterDns> r = new List<AdapterDns>();
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("Provider=")) { providerId = l.Substring(9); continue; }
                if (!l.StartsWith("Adapter=")) continue;
                string[] p = l.Substring(8).Split(new char[] { '|' }, 4);
                int idx;
                if (p.Length < 3 || !int.TryParse(p[0], out idx)) continue;
                AdapterDns a = new AdapterDns();
                a.Index = idx;
                a.Name = p.Length > 3 ? p[3] : "#" + idx;
                a.Dhcp4 = p[1] == "dhcp";
                if (!a.Dhcp4) a.Servers4.AddRange(p[1].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                a.HasV6 = p[2] != "-";
                a.Dhcp6 = p[2] == "dhcp" || p[2] == "-";
                if (a.HasV6 && !a.Dhcp6) a.Servers6.AddRange(p[2].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                r.Add(a);
            }
            return r;
        }

        public static bool HasBackup { get { return File.Exists(BackupPath); } }

        public static string BackupProvider()
        {
            try
            {
                string id;
                Deserialize(File.ReadAllText(BackupPath), out id);
                return id;
            }
            catch { return null; }
        }

        // ---- команды netsh ----

        static string Netsh(string args, List<string> errors)
        {
            string o;
            int code = Engine.RunHidden("netsh.exe", args, 20000, out o);
            if (code != 0 && errors != null) errors.Add("netsh " + args + " → " + (string.IsNullOrEmpty(o) ? "код " + code : o.Trim()));
            return code == 0 ? null : o;
        }

        static void SetServers(string family, int index, IList<string> servers, List<string> errors)
        {
            if (servers.Count == 0) { Netsh("interface " + family + " set dnsservers name=" + index + " source=dhcp", errors); return; }
            Netsh("interface " + family + " set dnsservers name=" + index + " static " + servers[0] + " primary validate=no", errors);
            for (int i = 1; i < servers.Count; i++)
                Netsh("interface " + family + " add dnsservers name=" + index + " " + servers[i] + " index=" + (i + 1) + " validate=no", errors);
        }

        static void SetEncryption(DohProvider p, bool on, List<string> errors)
        {
            List<string> ips = new List<string>(p.V4);
            ips.AddRange(p.V6);
            foreach (string ip in ips)
            {
                string tail = " dohtemplate=" + p.Template + " autoupgrade=" + (on ? "yes" : "no") + " udpfallback=no";
                if (Netsh("dns set encryption server=" + ip + tail, null) != null)
                    Netsh("dns add encryption server=" + ip + tail, on ? errors : null);
            }
        }

        static void Flush()
        {
            string o;
            Engine.RunHidden("ipconfig.exe", "/flushdns", 15000, out o);
        }

        // Включить DNS-сервис на всех активных подключениях. Прежние настройки сохраняются один раз —
        // при смене сервиса откат всё равно вернёт исходные настройки провайдера.
        public static string Apply(DohProvider p)
        {
            List<AdapterDns> list = ActiveAdapters();
            if (list.Count == 0) return "не найдено активных подключений к сети";
            List<string> errors = new List<string>();
            try
            {
                if (!HasBackup) File.WriteAllText(BackupPath, Serialize(p.Id, list), Encoding.UTF8);
                else
                {
                    // запоминаем новый сервис, сохраняя исходные адаптеры
                    string old;
                    List<AdapterDns> saved = Deserialize(File.ReadAllText(BackupPath), out old);
                    foreach (AdapterDns a in list)
                        if (!saved.Exists(delegate(AdapterDns s) { return s.Index == a.Index; })) saved.Add(a);
                    DohProvider prev = Doh.Find(old);
                    if (prev != null && prev != p && DohSupported) SetEncryption(prev, false, null);
                    File.WriteAllText(BackupPath, Serialize(p.Id, saved), Encoding.UTF8);
                }
            }
            catch (Exception ex) { return "не удалось сохранить резервную копию настроек: " + ex.Message; }

            if (DohSupported) SetEncryption(p, true, errors);
            foreach (AdapterDns a in list)
            {
                SetServers("ipv4", a.Index, p.V4, errors);
                if (a.HasV6) SetServers("ipv6", a.Index, p.V6, errors);
            }
            Flush();
            return errors.Count == 0 ? null : string.Join("; ", errors.ToArray());
        }

        // Вернуть DNS как было до Veylix
        public static string Restore()
        {
            if (!HasBackup) return "резервной копии настроек DNS нет";
            List<string> errors = new List<string>();
            string id;
            List<AdapterDns> saved;
            try { saved = Deserialize(File.ReadAllText(BackupPath), out id); }
            catch (Exception ex) { return ex.Message; }
            List<int> present = new List<int>();
            foreach (AdapterDns a in ActiveAdapters()) present.Add(a.Index);
            foreach (NetworkInterface ni in SafeInterfaces())
                try { present.Add(ni.GetIPProperties().GetIPv4Properties().Index); } catch { }
            foreach (AdapterDns a in saved)
            {
                if (!present.Contains(a.Index)) continue;   // адаптера больше нет
                SetServers("ipv4", a.Index, a.Dhcp4 ? new List<string>() : a.Servers4, errors);
                if (a.HasV6) SetServers("ipv6", a.Index, a.Dhcp6 ? new List<string>() : a.Servers6, errors);
            }
            DohProvider p = Doh.Find(id);
            if (p != null && DohSupported) SetEncryption(p, false, null);
            Flush();
            if (errors.Count > 0) return string.Join("; ", errors.ToArray());
            try { File.Delete(BackupPath); } catch { }
            return null;
        }

        static NetworkInterface[] SafeInterfaces()
        {
            try { return NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return new NetworkInterface[0]; }
        }
    }
}
