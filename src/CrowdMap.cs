using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Veylix
{
    // «Народная карта стратегий»: какие стратегии сейчас работают у других пользователей того же провайдера.
    // Отчёты анонимны: стратегия и работают ли Discord и YouTube. Провайдера (номер сети) и регион определяет
    // сервер Cloudflare по соединению; IP-адреса не сохраняются. Сервер — tools/feedback-worker.js.
    public static class CrowdMap
    {
        public const int MinShow = 3;            // меньше отчётов — стратегию не показываем
        public const int Reliable = 10;          // с этого числа отчётов оценка надёжна
        public static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(30);   // одна стратегия — не чаще

        public class Entry
        {
            public string Name;
            public int N, Both, Discord, Youtube, HoursAgo;
            public double Score;
            public int Percent { get { return N > 0 ? (int)Math.Round(100.0 * Both / N) : 0; } }
        }

        public class Totals
        {
            public int N, Both, Discord, Youtube;
            public double Rate(int ok) { return N > 0 ? (double)ok / N : 0; }
        }

        public class Snapshot
        {
            public int Asn;
            public string Isp = "", Region = "", Scope = "none";
            public int Window = 72;
            public List<Entry> Strategies = new List<Entry>();
            public Totals Recent = new Totals(), Earlier = new Totals();
            public DateTime Fetched = DateTime.Now;

            public string Where
            {
                get
                {
                    string isp = Isp.Length > 0 ? Isp : (Asn > 0 ? "AS" + Asn : "ваш провайдер");
                    return Region.Length > 0 ? isp + " · " + Region : isp;
                }
            }

            public Entry Find(string name)
            {
                foreach (Entry e in Strategies) if (e.Name == name && e.N >= MinShow) return e;
                return null;
            }
        }

        public static string BaseUrl
        {
            get
            {
                if (!Feedback.Configured) return null;
                string e = Feedback.Endpoint;
                return e.EndsWith("/feedback") ? e.Substring(0, e.Length - "/feedback".Length) : e;
            }
        }

        static int Int(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        static Totals ParseTotals(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            Totals t = new Totals();
            t.N = Int(d, "n"); t.Both = Int(d, "both"); t.Discord = Int(d, "discord"); t.Youtube = Int(d, "youtube");
            return t;
        }

        public static Snapshot Parse(string json)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object okv;
            if (d == null || !d.TryGetValue("ok", out okv) || !(okv is bool) || !(bool)okv)
                throw new InvalidDataException(Str(d, "error").Length > 0 ? Str(d, "error") : "неверный ответ сервера");
            Snapshot s = new Snapshot();
            s.Asn = Int(d, "asn");
            s.Isp = Str(d, "isp");
            s.Region = Str(d, "region");
            s.Scope = Str(d, "scope");
            if (Int(d, "window") > 0) s.Window = Int(d, "window");
            object list;
            if (d.TryGetValue("strategies", out list) && list is System.Collections.IEnumerable)
                foreach (object o in (System.Collections.IEnumerable)list)
                {
                    Dictionary<string, object> e = o as Dictionary<string, object>;
                    if (e == null) continue;
                    Entry en = new Entry();
                    en.Name = Str(e, "name");
                    en.N = Int(e, "n"); en.Both = Int(e, "both"); en.Discord = Int(e, "discord"); en.Youtube = Int(e, "youtube");
                    en.HoursAgo = Int(e, "hoursAgo");
                    object sc;
                    if (e.TryGetValue("score", out sc) && sc != null) try { en.Score = Convert.ToDouble(sc, System.Globalization.CultureInfo.InvariantCulture); } catch { }
                    if (en.Name.Length > 0) s.Strategies.Add(en);
                }
            object r;
            if (d.TryGetValue("recent", out r)) s.Recent = ParseTotals(r);
            if (d.TryGetValue("earlier", out r)) s.Earlier = ParseTotals(r);
            return s;
        }

        // Стратегии, которые стоит проверить первыми: надёжно работают у других (нижняя граница ≥ 0.3)
        public static List<string> Recommended(Snapshot s, int max)
        {
            List<string> r = new List<string>();
            if (s == null) return r;
            foreach (Entry e in s.Strategies)
                if (e.N >= MinShow && e.Score >= 0.3 && r.Count < max) r.Add(e.Name);
            return r;
        }

        // «Массово сломалось»: за последние 3 часа у провайдера резко упала доля рабочих проверок.
        // null — признаков нет. Возвращает понятное описание с тем сервисом, который упал сильнее.
        public static string Outage(Snapshot s)
        {
            if (s == null || s.Recent.N < 8) return null;
            Totals now = s.Recent, before = s.Earlier;
            double nowRate = now.Rate(now.Both);
            bool hasBefore = before.N >= 15;
            double beforeRate = hasBefore ? before.Rate(before.Both) : 1;
            bool drop = hasBefore ? (nowRate < 0.4 && beforeRate - nowRate >= 0.3) : nowRate < 0.2;
            if (!drop) return null;

            double dNow = now.Rate(now.Discord), yNow = now.Rate(now.Youtube);
            double dBefore = hasBefore ? before.Rate(before.Discord) : 1, yBefore = hasBefore ? before.Rate(before.Youtube) : 1;
            string what;
            if (dBefore - dNow >= 0.3 && yBefore - yNow < 0.3) what = "Discord";
            else if (yBefore - yNow >= 0.3 && dBefore - dNow < 0.3) what = "YouTube";
            else what = "обход";
            return "У пользователей " + s.Where + " за последние 3 часа массово перестал работать " + what +
                   ": работает в " + (int)Math.Round(nowRate * 100) + "% проверок" + (hasBefore ? " (раньше — " + (int)Math.Round(beforeRate * 100) + "%)" : "") +
                   ". Похоже, провайдер поменял блокировки — дело не в вашем компьютере.";
        }

        public static bool CanReport(Dictionary<string, DateTime> sent, string strategy, DateTime now)
        {
            DateTime last;
            return !sent.TryGetValue(strategy, out last) || now - last >= ReportEvery;
        }

        public static string BuildReport(string strategy, bool discord, bool youtube)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["strategy"] = strategy ?? "";
            d["discord"] = discord;
            d["youtube"] = youtube;
            return new JavaScriptSerializer().Serialize(d);
        }

        static HttpWebRequest Request(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = Http.UserAgent;
            req.Accept = "application/json";
            req.KeepAlive = false;
            return req;
        }

        public static Snapshot Fetch(int timeoutMs)
        {
            string b = BaseUrl;
            if (b == null) throw new InvalidOperationException("карта не настроена в этой сборке");
            using (HttpWebResponse r = (HttpWebResponse)Request(b + "/map", timeoutMs).GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                return Parse(sr.ReadToEnd());
        }

        // null — отправлено
        public static string Send(string payload, int timeoutMs)
        {
            string b = BaseUrl;
            if (b == null) return "карта не настроена";
            try
            {
                HttpWebRequest req = Request(b + "/report", timeoutMs);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse()) return null;
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                return hr != null ? "HTTP " + (int)hr.StatusCode : Http.Describe(ex);
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    public partial class MainWindow
    {
        CrowdMap.Snapshot crowd;
        string crowdError;
        bool crowdLoading, crowdOutageShown, crowdAskShown, crowdVpnLogged;
        string crowdVpn;                 // интернет идёт через VPN: карта показала бы сеть VPN, а не провайдера
        DateTime crowdVpnChecked = DateTime.MinValue;
        readonly Dictionary<string, DateTime> crowdSent = new Dictionary<string, DateTime>();
        StackPanel crowdRows, crowdAsk;
        TextBlock crowdWhere, crowdStatus;
        Border crowdOutage;
        TextBlock crowdOutageText;
        Card crowdCard;

        FrameworkElement BuildCrowdCard()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(20, 16, 18, 12);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(0));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            StackPanel top = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel ht = Theme.Row();
            TextBlock hi = Theme.Icon(0xE716, 14, "Accent");
            hi.Margin = new Thickness(0, 2, 8, 0);
            ht.Children.Add(hi);
            ht.Children.Add(Theme.T("Народная карта", 16, "Text", true));
            head.Children.Add(ht);
            VButton rf = Ui.IconButton(0xE72C, "Обновить карту");
            rf.Width = 28; rf.Height = 28;
            rf.Margin = new Thickness(0, -4, -6, 0);
            rf.Click += delegate { LoadCrowd(true); };
            Grid.SetColumn(rf, 1);
            head.Children.Add(rf);
            top.Children.Add(head);
            crowdWhere = Theme.Wrap("Что работает у других пользователей вашего провайдера", 12, "Text2");
            crowdWhere.Margin = new Thickness(0, 3, 0, 8);
            top.Children.Add(crowdWhere);

            crowdOutage = new Border();
            crowdOutage.CornerRadius = new CornerRadius(10);
            crowdOutage.Padding = new Thickness(10, 7, 10, 8);
            crowdOutage.Margin = new Thickness(0, 0, 0, 8);
            crowdOutage.SetResourceReference(Border.BackgroundProperty, "WarnSoft");
            crowdOutageText = Theme.Wrap("", 11.5, "Text");
            crowdOutage.Child = crowdOutageText;
            crowdOutage.Visibility = Visibility.Collapsed;
            top.Children.Add(crowdOutage);
            g.Children.Add(top);

            crowdRows = new StackPanel();
            ScrollViewer sv = Theme.Scroll(crowdRows);
            Grid.SetRow(sv, 1);
            g.Children.Add(sv);

            StackPanel bottom = new StackPanel();
            crowdAsk = new StackPanel();
            crowdAsk.Margin = new Thickness(0, 8, 0, 0);
            bottom.Children.Add(crowdAsk);
            crowdStatus = Theme.Wrap("", 11, "Text3");
            crowdStatus.Margin = new Thickness(0, 6, 0, 0);
            bottom.Children.Add(crowdStatus);
            Grid.SetRow(bottom, 2);
            g.Children.Add(bottom);
            c.Content = g;
            crowdCard = c;
            RenderCrowd();
            return c;
        }

        void RenderCrowd()
        {
            if (crowdRows == null) return;
            crowdRows.Children.Clear();
            crowdAsk.Children.Clear();

            if (!Feedback.Configured)
            {
                crowdStatus.Text = "Карта не настроена в этой сборке.";
                return;
            }
            if (crowdVpn != null)
            {
                crowdWhere.Text = "Что работает у других пользователей вашего провайдера";
                crowdOutage.Visibility = Visibility.Collapsed;
                TextBlock v = Theme.Wrap("Сейчас интернет идёт через VPN («" + crowdVpn + "»), поэтому сервер видит сеть VPN, а не вашего провайдера. " +
                    "Выключите VPN (или его режим TUN), чтобы увидеть карту своего провайдера. Пока VPN включён, ваши проверки на карту не отправляются.", 12, "Text2");
                v.Margin = new Thickness(0, 4, 0, 4);
                crowdRows.Children.Add(v);
                crowdStatus.Text = "";
            }
            else if (crowd == null)
            {
                crowdStatus.Text = crowdLoading ? "Загружаю карту…" : crowdError != null ? "Карта недоступна: " + crowdError : "Карта ещё не загружена.";
            }
            else
            {
                crowdWhere.Text = "Что работает у пользователей " + crowd.Where + " · за " + (crowd.Window / 24) + " дня";
                string outage = CrowdMap.Outage(crowd);
                crowdOutage.Visibility = outage != null ? Visibility.Visible : Visibility.Collapsed;
                crowdOutageText.Text = outage ?? "";

                int shown = 0;
                foreach (CrowdMap.Entry e in crowd.Strategies)
                {
                    if (e.N < CrowdMap.MinShow) continue;
                    if (++shown > 6) break;
                    crowdRows.Children.Add(CrowdRow(e));
                }
                if (shown == 0)
                {
                    TextBlock empty = Theme.Wrap("Пока мало данных по " + crowd.Where + ". Карта наполняется, когда пользователи проверяют стратегии: запустите автоподбор — и ваш результат тоже поможет другим.", 12, "Text3");
                    empty.Margin = new Thickness(0, 4, 0, 4);
                    crowdRows.Children.Add(empty);
                }
                crowdStatus.Text = "Обновлено в " + crowd.Fetched.ToString("HH:mm") + " · " + (crowd.Recent.N + crowd.Earlier.N) + " проверок · анонимно";
            }

            if (cfg.CrowdShare == 0)
            {
                TextBlock q = Theme.Wrap("Делиться результатами своих проверок? Уходят только название стратегии и «работает / нет» — без IP и личных данных.", 11.5, "Text2");
                crowdAsk.Children.Add(q);
                StackPanel bs = Theme.Row();
                bs.Margin = new Thickness(0, 6, 0, 0);
                VButton yes = Ui.Button("Делиться", 0xE73E, true);
                yes.Height = 30;
                yes.Click += delegate { SetCrowdShare(true); };
                bs.Children.Add(yes);
                VButton no = Ui.Button("Нет", 0, false);
                no.Height = 30;
                no.Margin = new Thickness(6, 0, 0, 0);
                no.Click += delegate { SetCrowdShare(false); };
                bs.Children.Add(no);
                crowdAsk.Children.Add(bs);
            }
            else
            {
                // выбор сделан — показываем его и даём передумать прямо здесь, без поиска по настройкам
                bool on = cfg.CrowdShare == 1;
                Grid row = new Grid();
                row.ColumnDefinitions.Add(Theme.Col(0));
                row.ColumnDefinitions.Add(Theme.Col(-1));
                StackPanel st = Theme.Row();
                st.VerticalAlignment = VerticalAlignment.Center;
                TextBlock ic = Theme.Icon(on ? 0xE73E : 0xE711, 10, on ? "Ok" : "Text3");
                ic.Margin = new Thickness(0, 1, 6, 0);
                st.Children.Add(ic);
                st.Children.Add(Theme.T(on ? "Вы делитесь результатами" : "Вы не делитесь результатами", 11.5, "Text2", false));
                row.Children.Add(st);
                VButton change = LinkButton(on ? "Не делиться" : "Делиться");
                change.Margin = new Thickness(0, -4, -8, -4);
                change.ToolTip = on ? "Перестать отправлять результаты проверок на карту" : "Анонимно отправлять название стратегии и «работает / нет»";
                change.Click += delegate { SetCrowdShare(!on); };
                Grid.SetColumn(change, 1);
                row.Children.Add(change);
                crowdAsk.Children.Add(row);
            }
        }

        FrameworkElement CrowdRow(CrowdMap.Entry e)
        {
            Grid r = new Grid();
            r.Margin = new Thickness(0, 2, 0, 2);
            r.ColumnDefinitions.Add(Theme.Col(0));
            r.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel left = new StackPanel();
            Grid nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(Theme.Col(0));
            nameRow.ColumnDefinitions.Add(Theme.Col(-1));
            Strategy local = strategies.Find(delegate(Strategy x) { return x.Display == e.Name; });
            bool active = local != null && engine.IsRunning && object.ReferenceEquals(engine.Current, local);
            nameRow.Children.Add(Theme.T(e.Name + (active ? "  · включена" : ""), 12.5, "Text", true));
            string level = e.Percent >= 70 ? "ok" : e.Percent >= 40 ? "warn" : "err";
            TextBlock pct = Theme.T(e.Percent + "%", 12.5, Ui.FgKey(level), true);
            Grid.SetColumn(pct, 1);
            nameRow.Children.Add(pct);
            left.Children.Add(nameRow);

            // полоса доли рабочих проверок
            Grid bar = new Grid();
            bar.Height = 4;
            bar.Margin = new Thickness(0, 4, 0, 3);
            Border track = new Border();
            track.CornerRadius = new CornerRadius(2);
            track.SetResourceReference(Border.BackgroundProperty, "Surface2");
            bar.Children.Add(track);
            Border fill = new Border();
            fill.CornerRadius = new CornerRadius(2);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.SetResourceReference(Border.BackgroundProperty, Ui.FgKey(level));
            bar.Children.Add(fill);
            double frac = e.N > 0 ? (double)e.Both / e.N : 0;
            bar.SizeChanged += delegate { fill.Width = Math.Max(0, bar.ActualWidth * frac); };
            left.Children.Add(bar);
            left.Children.Add(Theme.T("Discord " + Pct(e.Discord, e.N) + " · YouTube " + Pct(e.Youtube, e.N) + " · " + Reports(e.N) +
                (e.N < CrowdMap.Reliable ? " · мало данных" : ""), 10.5, "Text3", false));
            r.Children.Add(left);

            if (local != null && !active)
            {
                VButton use = LinkButton("Включить");
                use.VerticalAlignment = VerticalAlignment.Center;
                use.Margin = new Thickness(6, 0, -8, 0);
                use.ToolTip = "Выбрать " + e.Name + " и включить обход";
                use.Click += async delegate
                {
                    if (busy || tuning || updating) return;
                    Select(local);
                    ClearGamePauseByUser();
                    await StartAsync(local);
                };
                Grid.SetColumn(use, 1);
                r.Children.Add(use);
            }
            r.ToolTip = e.Name + ": Discord и YouTube вместе работали в " + e.Both + " из " + e.N + " проверок" +
                (e.HoursAgo == 0 ? " · последний отчёт в этот час" : " · последний отчёт " + e.HoursAgo + " ч назад");
            return r;
        }

        static string Pct(int ok, int n) { return n > 0 ? (int)Math.Round(100.0 * ok / n) + "%" : "—"; }

        static string Reports(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            string w = m10 == 1 && m100 != 11 ? "отчёт" : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "отчёта" : "отчётов";
            return n + " " + w;
        }

        void SetCrowdShare(bool on)
        {
            cfg.CrowdShare = on ? 1 : 2;
            SaveSettings();
            log.Add("info", on ? "Народная карта: результаты проверок будут анонимно отправляться на карту." : "Народная карта: результаты проверок не отправляются.");
            RenderCrowd();
            RefreshSettingsState();
        }

        // Загрузка карты: при показе страницы «Стратегии», после запуска и раз в 30 минут
        async void LoadCrowd(bool force)
        {
            if (!Feedback.Configured || crowdLoading || selfTest) return;
            if (!force && crowd != null && DateTime.Now - crowd.Fetched < TimeSpan.FromMinutes(10)) return;
            crowdLoading = true;
            RenderCrowd();
            try
            {
                crowdVpn = await Task.Run(() => VpnRoute.Active());
                crowdVpnChecked = DateTime.Now;
                if (crowdVpn != null) { crowd = null; return; }
                crowd = await Task.Run(() => CrowdMap.Fetch(12000));
                crowdError = null;
                string outage = CrowdMap.Outage(crowd);
                if (outage != null && !crowdOutageShown)
                {
                    crowdOutageShown = true;
                    log.Add("warn", "Народная карта: " + outage);
                    events.Add("connection", "warn", "Массовый сбой у провайдера", outage, "strategies");
                    Toast("crowd-outage", "warn", "Массовый сбой у " + (crowd.Isp.Length > 0 ? crowd.Isp : "провайдера"), outage, "strategies", true);
                }
            }
            catch (Exception ex) { crowdError = ex is WebException ? Http.Describe(ex) : ex.Message; }
            finally
            {
                crowdLoading = false;
                RenderCrowd();
                RefreshStrategyCards();
            }
        }

        // Вызывается после каждой проверки стратегии (обычной, в лаборатории, в автоподборе)
        void CrowdReport(Strategy s, ServiceResult d, ServiceResult y)
        {
            if (s == null || !Feedback.Configured || selfTest) return;
            if (d.Status == ServiceStatus.NoNetwork || y.Status == ServiceStatus.NoNetwork || d.Status == ServiceStatus.Unknown || y.Status == ServiceStatus.Unknown) return;
            if (cfg.CrowdShare == 0)
            {
                if (!crowdAskShown)
                {
                    crowdAskShown = true;
                    Toast("crowd-ask", "info", "Народная карта стратегий", "Посмотрите, какие стратегии работают у других пользователей вашего провайдера, и помогите карте своими результатами.", "strategies", false);
                }
                return;
            }
            if (cfg.CrowdShare != 1 || !CrowdMap.CanReport(crowdSent, s.Display, DateTime.Now)) return;
            crowdSent[s.Display] = DateTime.Now;
            string payload = CrowdMap.BuildReport(s.Display, d.Status == ServiceStatus.Ok, y.Status == ServiceStatus.Ok);
            Task.Run(delegate
            {
                // через VPN результат относился бы к сети VPN и только запутал бы карту
                string vpn = VpnRoute.Active();
                if (vpn != null)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (crowdVpnLogged) return;
                        crowdVpnLogged = true;
                        log.Add("info", "Народная карта: интернет идёт через VPN («" + vpn + "») — результаты проверок не отправляются.");
                    }));
                    return;
                }
                string err = CrowdMap.Send(payload, 10000);
                if (err != null) Dispatcher.BeginInvoke(new Action(delegate { log.Add("info", "Народная карта: отчёт не отправлен (" + err + ")."); }));
            });
        }

        // Строка на карточке стратегии: доля рабочих проверок у других пользователей провайдера
        string CrowdBadge(Strategy s, out string level)
        {
            level = "idle";
            CrowdMap.Entry e = crowd != null ? crowd.Find(s.Display) : null;
            if (e == null) return null;
            level = e.Percent >= 70 ? "ok" : e.Percent >= 40 ? "warn" : "err";
            return "у других " + e.Percent + "% · " + Reports(e.N);
        }
    }
}
