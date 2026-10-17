using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Veylix
{
    // ===================== простые HTTP-запросы (обновления, IPSet) =====================

    public static class Http
    {
        public const string UserAgent = "Veylix/" + Program.Version + " (+https://github.com/Flowseal/zapret-discord-youtube)";

        static HttpWebRequest Create(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = UserAgent;
            req.KeepAlive = false;
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // системный прокси используется как есть (WebRequest.DefaultWebProxy) — ничего не переопределяем
            return req;
        }

        public static string GetString(string url, int timeoutMs, string accept)
        {
            HttpWebRequest req = Create(url, timeoutMs);
            if (accept != null) req.Accept = accept;
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        // Скачивание во временный файл с ограничением размера
        public static void Download(string url, string path, int timeoutMs, long maxBytes, Action<long, long> progress)
        {
            Download(url, path, timeoutMs, maxBytes, progress, System.Threading.CancellationToken.None);
        }

        // С возможностью отмены: при отмене бросается OperationCanceledException, вызывающий удаляет недокачанный файл
        public static void Download(string url, string path, int timeoutMs, long maxBytes, Action<long, long> progress, System.Threading.CancellationToken ct)
        {
            HttpWebRequest req = Create(url, timeoutMs);
            req.AutomaticDecompression = DecompressionMethods.None;
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            {
                long total = r.ContentLength;
                if (total > maxBytes) throw new InvalidDataException("Файл больше допустимого размера.");
                using (Stream s = r.GetResponseStream())
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        done += n;
                        if (done > maxBytes) throw new InvalidDataException("Файл больше допустимого размера.");
                        fs.Write(buf, 0, n);
                        if (progress != null) progress(done, total);
                    }
                }
            }
        }

        // Понятное описание сетевой ошибки
        public static string Describe(Exception ex)
        {
            WebException we = ex as WebException;
            if (we != null)
            {
                switch (we.Status)
                {
                    case WebExceptionStatus.Timeout: return "превышено время ожидания";
                    case WebExceptionStatus.NameResolutionFailure: return "не удалось определить адрес сервера (DNS)";
                    case WebExceptionStatus.ConnectFailure: return "не удалось подключиться к серверу";
                    case WebExceptionStatus.TrustFailure: return "сертификат сервера не прошёл проверку";
                    case WebExceptionStatus.SecureChannelFailure: return "не удалось установить защищённое соединение";
                    case WebExceptionStatus.ProxyNameResolutionFailure: return "не удалось найти прокси-сервер";
                    case WebExceptionStatus.ProtocolError:
                        HttpWebResponse hr = we.Response as HttpWebResponse;
                        if (hr != null)
                        {
                            int code = (int)hr.StatusCode;
                            if (code == 403 || code == 429)
                            {
                                // GitHub API: лимит запросов без авторизации — 60 в час
                                string reset = hr.Headers["X-RateLimit-Reset"];
                                long unix;
                                if (reset != null && long.TryParse(reset, out unix))
                                    return "GitHub временно ограничил запросы (HTTP " + code + "); повторите после " +
                                        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime().ToString("HH:mm");
                                return "сервер ограничил запросы (HTTP " + code + "), попробуйте позже";
                            }
                            if (code == 404) return "не найдено (HTTP 404)";
                            return "сервер вернул HTTP " + code;
                        }
                        break;
                }
            }
            return ex.Message;
        }
    }

    // ===================== послойная проверка доступности =====================

    public enum ProbeStatus { Ok, Partial, Timeout, NetError, Blocked }
    public enum ServiceStatus { Unknown, Ok, Partial, Unavailable, NoNetwork }

    public class Target
    {
        public string Host, Path;
        public int Port;
        public bool Tls;
        public bool Main;    // ключевой адрес сервиса
        public string ExpectBody;   // для HTTP без TLS: ожидаемый текст ответа (иначе это заглушка/портал авторизации)

        public Target(string host, string path, bool main)
        {
            Host = host; Path = path; Port = 443; Tls = true; Main = main;
        }

        public override string ToString() { return Host; }
    }

    public class ServiceDef
    {
        public string Name;
        public Target[] Targets;
        public ServiceDef(string name, params Target[] t) { Name = name; Targets = t; }
    }

    public class ProbeResult
    {
        public Target Target;
        public ProbeStatus Status;
        public string Stage = "";      // DNS | TCP | TLS | HTTP
        public string Detail = "";
        public string Address = "";
        public int DnsMs = -1, TcpMs = -1, TlsMs = -1, HttpMs = -1;
        public int HttpCode;
        public long Bytes;
        public bool Flaky;             // первая попытка не прошла, повторная — прошла

        public bool Passed { get { return Status == ProbeStatus.Ok; } }

        public string Timing()
        {
            List<string> p = new List<string>();
            if (DnsMs >= 0) p.Add("DNS " + DnsMs + " мс");
            if (TcpMs >= 0) p.Add("TCP " + TcpMs + " мс");
            if (TlsMs >= 0) p.Add("TLS " + TlsMs + " мс");
            if (HttpMs >= 0) p.Add("HTTP " + (HttpCode > 0 ? HttpCode + ", " : "") + HttpMs + " мс");
            return string.Join(" · ", p);
        }
    }

    public class ServiceResult
    {
        public string Name;
        public ServiceStatus Status;
        public string Summary = "";
        public List<ProbeResult> Probes = new List<ProbeResult>();
        public int Passed, Total;
        public DateTime When = DateTime.Now;

        // вклад в оценку стратегии: ок = 2, частично = 1
        public int Score
        {
            get
            {
                int s = 0;
                foreach (ProbeResult p in Probes)
                {
                    if (p.Status == ProbeStatus.Ok) s += 2;
                    else if (p.Status == ProbeStatus.Partial) s += 1;
                }
                return s;
            }
        }

        public int MaxScore { get { return Probes.Count * 2; } }
    }

    public static class NetCheck
    {
        public static int StageTimeoutMs = 6000;
        const int BodyLimit = 64 * 1024;      // сколько данных читаем, чтобы заметить «обрыв на 16–20 КБ»

        public static readonly ServiceDef Internet = new ServiceDef("Интернет",
            new Target("www.msftconnecttest.com", "/connecttest.txt", true),
            new Target("ya.ru", "/", false));

        public static readonly ServiceDef Discord = new ServiceDef("Discord",
            new Target("discord.com", "/", true),
            new Target("gateway.discord.gg", "/", false),
            new Target("cdn.discordapp.com", "/", false));

        public static readonly ServiceDef YouTube = new ServiceDef("YouTube",
            new Target("www.youtube.com", "/", true),
            new Target("i.ytimg.com", "/", false),
            new Target("redirector.googlevideo.com", "/report_mapping", false));

        static NetCheck()
        {
            // Адрес проверки подключения Windows (NCSI) работает только по HTTP — так его проверяет и сама Windows.
            // По HTTPS у него чужой сертификат, и проверка ложно показала бы «подмену».
            Target ncsi = Internet.Targets[0];
            ncsi.Tls = false;
            ncsi.Port = 80;
            ncsi.ExpectBody = "Microsoft Connect Test";
        }

        static async Task<bool> Within(Task t, int ms)
        {
            Task done = await Task.WhenAny(t, Task.Delay(ms)).ConfigureAwait(false);
            if (done != t)
            {
                // наблюдаем исключение, чтобы оно не всплыло как UnobservedTaskException
                Task ignored = t.ContinueWith(delegate(Task x) { Exception e = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return false;
            }
            return true;
        }

        static SocketException FindSocketError(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                AggregateException ae = e as AggregateException;
                if (ae != null && ae.InnerExceptions.Count > 0) return FindSocketError(ae.InnerExceptions[0]);
                SocketException se = e as SocketException;
                if (se != null) return se;
            }
            return null;
        }

        static void Fail(ProbeResult r, ProbeStatus st, string stage, string detail)
        {
            r.Status = st;
            r.Stage = stage;
            r.Detail = detail;
        }

        static bool IsStubAddress(IPAddress a)
        {
            if (IPAddress.IsLoopback(a)) return true;
            byte[] b = a.GetAddressBytes();
            if (b.Length == 4)
            {
                if (b[0] == 0 || b[0] == 10 || b[0] == 127) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            }
            return false;
        }

        public static Task<ProbeResult> Probe(Target t)
        {
            return Probe(t, false);
        }

        // light = true: только соединение и заголовки ответа, без чтения тела (для периодического мониторинга)
        public static async Task<ProbeResult> Probe(Target t, bool light)
        {
            ProbeResult r = new ProbeResult();
            r.Target = t;
            int to = StageTimeoutMs;
            Stopwatch sw = Stopwatch.StartNew();

            // ---- DNS ----
            IPAddress addr = null;
            Task<IPAddress[]> dns = Dns.GetHostAddressesAsync(t.Host);
            if (!await Within(dns, to).ConfigureAwait(false))
            {
                Fail(r, ProbeStatus.Timeout, "DNS", "DNS-сервер не ответил за " + to / 1000 + " с");
                return r;
            }
            if (dns.IsFaulted)
            {
                SocketException se = FindSocketError(dns.Exception);
                if (se != null && se.SocketErrorCode == SocketError.HostNotFound)
                    Fail(r, ProbeStatus.Blocked, "DNS", "домен не найден (NXDOMAIN) — возможна DNS-блокировка");
                else
                    Fail(r, ProbeStatus.NetError, "DNS", "ошибка DNS: " + (se != null ? se.SocketErrorCode.ToString() : dns.Exception.GetBaseException().Message));
                return r;
            }
            foreach (IPAddress a in dns.Result)
                if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; break; }
            if (addr == null && dns.Result.Length > 0) addr = dns.Result[0];
            r.DnsMs = (int)sw.ElapsedMilliseconds;
            if (addr == null)
            {
                Fail(r, ProbeStatus.NetError, "DNS", "DNS не вернул ни одного адреса");
                return r;
            }
            r.Address = addr.ToString();
            if (IsStubAddress(addr))
            {
                Fail(r, ProbeStatus.Blocked, "DNS", "DNS вернул адрес-заглушку " + addr + " — домен подменён");
                return r;
            }

            // ---- TCP ----
            TcpClient client = new TcpClient(addr.AddressFamily);
            try
            {
                sw.Restart();
                Task conn = client.ConnectAsync(addr, t.Port);
                if (!await Within(conn, to).ConfigureAwait(false))
                {
                    Fail(r, ProbeStatus.Timeout, "TCP", "TCP-подключение к " + addr + ":" + t.Port + " не установилось за " + to / 1000 + " с");
                    return r;
                }
                if (conn.IsFaulted)
                {
                    SocketException se = FindSocketError(conn.Exception);
                    SocketError code = se != null ? se.SocketErrorCode : SocketError.SocketError;
                    if (code == SocketError.ConnectionRefused) Fail(r, ProbeStatus.NetError, "TCP", "сервер отклонил подключение");
                    else if (code == SocketError.ConnectionReset) Fail(r, ProbeStatus.Blocked, "TCP", "подключение сброшено (RST) — признак фильтрации");
                    else if (code == SocketError.NetworkUnreachable || code == SocketError.HostUnreachable)
                        Fail(r, ProbeStatus.NetError, "TCP", "нет маршрута до сервера (" + code + ")");
                    else Fail(r, ProbeStatus.NetError, "TCP", "ошибка подключения: " + code);
                    return r;
                }
                r.TcpMs = (int)sw.ElapsedMilliseconds;

                Stream stream = client.GetStream();

                // ---- TLS (проверка сертификата НЕ отключается) ----
                if (t.Tls)
                {
                    SslPolicyErrors certErrors = SslPolicyErrors.None;
                    SslStream ssl = new SslStream(stream, false,
                        delegate(object s, X509Certificate c, X509Chain ch, SslPolicyErrors e)
                        {
                            certErrors = e;
                            return e == SslPolicyErrors.None;
                        });
                    stream = ssl;
                    sw.Restart();
                    Task auth;
                    try { auth = ssl.AuthenticateAsClientAsync(t.Host, null, (SslProtocols)0, false); }
                    catch (ArgumentException) { auth = ssl.AuthenticateAsClientAsync(t.Host, null, SslProtocols.Tls12, false); }
                    catch (NotSupportedException) { auth = ssl.AuthenticateAsClientAsync(t.Host, null, SslProtocols.Tls12, false); }

                    if (!await Within(auth, to).ConfigureAwait(false))
                    {
                        Fail(r, ProbeStatus.Timeout, "TLS", "TLS-рукопожатие зависло — так обычно выглядит DPI-блокировка по SNI");
                        return r;
                    }
                    if (auth.IsFaulted)
                    {
                        Exception be = auth.Exception.GetBaseException();
                        SocketException se = FindSocketError(auth.Exception);
                        if (certErrors != SslPolicyErrors.None)
                            Fail(r, ProbeStatus.Blocked, "TLS", "сертификат не прошёл проверку (" + certErrors + ") — возможна подмена ответа провайдером");
                        else if (se != null && se.SocketErrorCode == SocketError.ConnectionReset)
                            Fail(r, ProbeStatus.Blocked, "TLS", "соединение сброшено во время TLS — признак DPI-блокировки");
                        else if (be is IOException || be is AuthenticationException)
                            Fail(r, ProbeStatus.Blocked, "TLS", "соединение разорвано во время TLS-рукопожатия");
                        else
                            Fail(r, ProbeStatus.NetError, "TLS", "ошибка TLS: " + be.Message);
                        return r;
                    }
                    r.TlsMs = (int)sw.ElapsedMilliseconds;
                }

                // ---- HTTP: запрос и чтение начала ответа ----
                sw.Restart();
                string req = "GET " + t.Path + " HTTP/1.1\r\nHost: " + t.Host +
                             "\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) Veylix/" + Program.Version +
                             "\r\nAccept: */*\r\nAccept-Encoding: identity\r\nConnection: close\r\n\r\n";
                byte[] rb = Encoding.ASCII.GetBytes(req);
                Task w = stream.WriteAsync(rb, 0, rb.Length);
                if (!await Within(w, to).ConfigureAwait(false) || w.IsFaulted)
                {
                    Fail(r, ProbeStatus.NetError, "HTTP", "не удалось отправить запрос");
                    return r;
                }

                byte[] buf = new byte[16384];
                StringBuilder head = new StringBuilder();
                StringBuilder sample = new StringBuilder();
                bool headDone = false, eof = false;
                long total = 0;
                while (total < BodyLimit)
                {
                    Task<int> rd = stream.ReadAsync(buf, 0, buf.Length);
                    if (!await Within(rd, to).ConfigureAwait(false))
                    {
                        if (total == 0)
                        {
                            Fail(r, ProbeStatus.Timeout, "HTTP", "сервер не прислал ответ за " + to / 1000 + " с");
                            return r;
                        }
                        r.Bytes = total;
                        r.HttpMs = (int)sw.ElapsedMilliseconds;
                        Fail(r, ProbeStatus.Partial, "HTTP", "передача остановилась после " + (total / 1024) + " КБ" +
                             (total >= 12 * 1024 && total <= 24 * 1024 ? " — характерный обрыв DPI на 16–20 КБ" : ""));
                        return r;
                    }
                    if (rd.IsFaulted)
                    {
                        if (total == 0)
                        {
                            Fail(r, ProbeStatus.Blocked, "HTTP", "соединение сброшено до ответа сервера");
                            return r;
                        }
                        r.Bytes = total;
                        Fail(r, ProbeStatus.Partial, "HTTP", "соединение оборвалось после " + (total / 1024) + " КБ");
                        return r;
                    }
                    int n = rd.Result;
                    if (n <= 0) { eof = true; break; }
                    total += n;
                    if (t.ExpectBody != null && sample.Length < 8192) sample.Append(Encoding.ASCII.GetString(buf, 0, n));
                    if (!headDone)
                    {
                        head.Append(Encoding.ASCII.GetString(buf, 0, n));
                        string hs = head.ToString();
                        int end = hs.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (end >= 0 || hs.Length > 8192)
                        {
                            headDone = true;
                            if (r.HttpMs < 0) r.HttpMs = (int)sw.ElapsedMilliseconds;
                            string[] first = hs.Split(new string[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                            int code;
                            if (first.Length >= 2 && first[0].StartsWith("HTTP/") && int.TryParse(first[1], out code)) r.HttpCode = code;
                        }
                    }
                    if (headDone && light && t.ExpectBody == null) break;   // лёгкий режим: хватает заголовков ответа
                }
                r.Bytes = total;
                if (r.HttpMs < 0) r.HttpMs = (int)sw.ElapsedMilliseconds;
                if (r.HttpCode == 0)
                {
                    Fail(r, ProbeStatus.NetError, "HTTP", "получен ответ не в формате HTTP");
                    return r;
                }
                if (t.ExpectBody != null && sample.ToString().IndexOf(t.ExpectBody, StringComparison.Ordinal) < 0)
                {
                    Fail(r, ProbeStatus.Blocked, "HTTP", "вместо ожидаемого ответа пришла другая страница (HTTP " + r.HttpCode +
                         ") — похоже на портал авторизации Wi-Fi или заглушку провайдера");
                    return r;
                }
                r.Status = ProbeStatus.Ok;
                r.Stage = "HTTP";
                // Любой HTTP-ответ (даже 404) означает, что сервер реально достижим
                r.Detail = "ответ HTTP " + r.HttpCode + (eof ? "" : ", получено " + (total / 1024) + " КБ без обрывов");
                return r;
            }
            catch (Exception ex)
            {
                Fail(r, ProbeStatus.NetError, r.Stage.Length > 0 ? r.Stage : "TCP", ex.GetBaseException().Message);
                return r;
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        // Повторяем неудачную проверку один раз на новом соединении, чтобы отсеять случайные сбои
        public static async Task<ProbeResult> ProbeWithRetry(Target t)
        {
            ProbeResult a = await Probe(t).ConfigureAwait(false);
            if (a.Passed) return a;
            await Task.Delay(400).ConfigureAwait(false);
            ProbeResult b = await Probe(t).ConfigureAwait(false);
            return CombineAttempts(a, b);
        }

        public static ProbeResult CombineAttempts(ProbeResult first, ProbeResult second)
        {
            if (second.Passed)
            {
                second.Flaky = true;
                second.Detail += " (со второй попытки)";
                return second;
            }
            if (first.Status == ProbeStatus.Partial && second.Status != ProbeStatus.Partial) return first;
            if (first.Status == second.Status && first.Stage == second.Stage)
                second.Detail += " — повторилось дважды";
            return second;
        }

        public static async Task<ServiceResult> CheckService(ServiceDef def)
        {
            List<Task<ProbeResult>> tasks = new List<Task<ProbeResult>>();
            foreach (Target t in def.Targets) tasks.Add(ProbeWithRetry(t));
            ProbeResult[] res = await Task.WhenAll(tasks).ConfigureAwait(false);
            return Verdict(def.Name, res, null);
        }

        // internetOk: null — не проверялся, false — базовая проверка интернета не прошла
        public static ServiceResult Verdict(string name, IList<ProbeResult> probes, bool? internetOk)
        {
            ServiceResult sr = new ServiceResult();
            sr.Name = name;
            sr.Probes.AddRange(probes);
            sr.Total = probes.Count;
            bool mainOk = false;
            int timeouts = 0, blocked = 0, partial = 0;
            ProbeResult worst = null;
            foreach (ProbeResult p in probes)
            {
                if (p.Passed) { sr.Passed++; if (p.Target.Main) mainOk = true; continue; }
                if (p.Status == ProbeStatus.Timeout) timeouts++;
                else if (p.Status == ProbeStatus.Blocked) blocked++;
                else if (p.Status == ProbeStatus.Partial) partial++;
                if (worst == null || p.Target.Main) worst = p;
            }

            if (sr.Passed == sr.Total)
            {
                sr.Status = ServiceStatus.Ok;
                sr.Summary = "Доступен · " + sr.Passed + "/" + sr.Total;
            }
            else if (sr.Passed > 0 || partial > 0)
            {
                sr.Status = ServiceStatus.Partial;
                sr.Summary = (mainOk ? "Частично: " : "Нестабильно: ") + worst.Target.Host + " — " + worst.Detail;
            }
            else if (internetOk == false)
            {
                sr.Status = ServiceStatus.NoNetwork;
                sr.Summary = "Нет подключения к интернету — о блокировке судить нельзя";
            }
            else
            {
                sr.Status = ServiceStatus.Unavailable;
                string why;
                if (blocked > 0 && timeouts == 0) why = "соединение сбрасывается или подменяется";
                else if (timeouts == sr.Total) why = "все адреса не отвечают (таймаут на этапе " + worst.Stage + ")";
                else why = worst.Detail;
                sr.Summary = "Недоступен (проверено дважды): " + why;
            }
            return sr;
        }

        public static string StatusWord(ServiceStatus s)
        {
            switch (s)
            {
                case ServiceStatus.Ok: return "Доступен";
                case ServiceStatus.Partial: return "Частично";
                case ServiceStatus.Unavailable: return "Недоступен";
                case ServiceStatus.NoNetwork: return "Нет сети";
                default: return "Не проверено";
            }
        }

        // Полная проверка: сначала базовый интернет, затем сервисы
        public static async Task<ServiceResult[]> CheckAll()
        {
            ServiceResult inet = await CheckService(Internet).ConfigureAwait(false);
            bool online = inet.Status == ServiceStatus.Ok || inet.Status == ServiceStatus.Partial;
            Task<ServiceResult> d = CheckService(Discord);
            Task<ServiceResult> y = CheckService(YouTube);
            await Task.WhenAll(d, y).ConfigureAwait(false);
            return new ServiceResult[] { inet, Verdict("Discord", d.Result.Probes, online), Verdict("YouTube", y.Result.Probes, online) };
        }

        // Системный прокси (как в диагностике service.bat). Проверки идут напрямую, потому что
        // winws обрабатывает прямой трафик; включённый прокси — частая причина «у меня не работает».
        public static string SystemProxyDescription()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (k == null) return null;
                    object en = k.GetValue("ProxyEnable");
                    object srv = k.GetValue("ProxyServer");
                    object pac = k.GetValue("AutoConfigURL");
                    if (en is int && (int)en == 1 && srv != null) return "прокси " + srv;
                    if (pac != null && pac.ToString().Length > 0) return "скрипт автонастройки " + pac;
                }
            }
            catch { }
            return null;
        }
    }
}
