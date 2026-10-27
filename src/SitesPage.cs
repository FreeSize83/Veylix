using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Veylix
{
    // Страница «Сайты и DNS»: почему не открывается конкретный сайт и защищённый DNS
    public partial class MainWindow
    {
        static readonly string[] DnsControlHosts = { "www.youtube.com", "discord.com", "www.instagram.com", "rutracker.org" };

        TextBox siteInput;
        VButton siteCheckBtn;
        StackPanel siteResult, siteMyList;
        bool siteChecking;

        StackPanel dnsInfo, dnsRows;
        Border dnsChip;
        TextBlock dnsVerdict, dnsProviders;
        VButton dnsCheckBtn, dnsApplyBtn, dnsRestoreBtn;
        VButton[] dnsSeg = new VButton[2];
        Card dnsCard;
        string dnsPick = "cloudflare";
        Dictionary<string, DnsAnswer> dnsSpeed = new Dictionary<string, DnsAnswer>();
        List<AdapterDns> dnsAdapters;   // последний снимок настроек (читается в фоне: netsh небыстрый)
        bool dnsBusy, dnsLoading;

        FrameworkElement BuildSites()
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(BuildPresetsCard());

            // ---- проверка сайта ----
            Card c1 = new Card(22);
            c1.Margin = new Thickness(0, 0, 0, 16);
            c1.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s1 = new StackPanel();
            s1.Children.Add(Ui.Heading("Проверка сайта",
                "Введите адрес, который не открывается. Veylix проверит DNS, соединение и шифрование, объяснит, в чём дело, и при необходимости добавит сайт в обход."));

            Grid ig = new Grid();
            ig.Margin = new Thickness(0, 14, 0, 4);
            ig.ColumnDefinitions.Add(Theme.Col(0));
            ig.ColumnDefinitions.Add(Theme.Col(-1));
            Grid ib = new Grid();
            siteInput = new TextBox();
            siteInput.Padding = new Thickness(8, 0, 8, 0);
            ib.Children.Add(siteInput);
            TextBlock ph = Theme.T("Адрес сайта, например rutracker.org или https://example.com/page", 12.5, "Text3", false);
            ph.VerticalAlignment = VerticalAlignment.Center;
            ph.Margin = new Thickness(12, 0, 0, 0);
            ph.IsHitTestVisible = false;
            ib.Children.Add(ph);
            siteInput.TextChanged += delegate { ph.Visibility = siteInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            siteInput.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                RunSiteCheck(siteInput.Text, null);
            };
            ig.Children.Add(ib);
            siteCheckBtn = Ui.Button("Проверить", 0xE721, true);
            siteCheckBtn.Margin = new Thickness(8, 0, 0, 0);
            siteCheckBtn.Click += delegate { RunSiteCheck(siteInput.Text, null); };
            Grid.SetColumn(siteCheckBtn, 1);
            ig.Children.Add(siteCheckBtn);
            s1.Children.Add(ig);

            siteResult = new StackPanel();
            siteResult.Visibility = Visibility.Collapsed;
            s1.Children.Add(siteResult);

            s1.Children.Add(Ui.Divider());
            Grid lh = new Grid();
            lh.Margin = new Thickness(0, 8, 0, 4);
            lh.ColumnDefinitions.Add(Theme.Col(0));
            lh.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel lt = new StackPanel();
            lt.Children.Add(Theme.T("Мои домены в обходе", 13, "Text", true));
            TextBlock ld = Theme.Wrap("Файл " + SiteCheck.UserListFile + ". Запись действует и на поддомены: youtube.com включает www.youtube.com.", 11.5, "Text3");
            ld.Margin = new Thickness(0, 2, 0, 0);
            lt.Children.Add(ld);
            lh.Children.Add(lt);
            VButton open = Ui.IconButton(0xE8A5, "Открыть файл в Блокноте");
            open.VerticalAlignment = VerticalAlignment.Top;
            open.Click += delegate { OnOpenUserList(SiteCheck.UserListFile); };
            Grid.SetColumn(open, 1);
            lh.Children.Add(open);
            s1.Children.Add(lh);
            siteMyList = new StackPanel();
            s1.Children.Add(siteMyList);
            c1.Content = s1;
            sp.Children.Add(c1);

            // ---- защищённый DNS ----
            Card c2 = new Card(22);
            c2.Margin = new Thickness(0, 0, 0, 16);
            c2.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s2 = new StackPanel();
            s2.Children.Add(Ui.Heading("Защищённый DNS",
                "Многие провайдеры блокируют сайты через DNS: на вопрос «какой адрес у сайта» отвечают «такого нет» или подсовывают заглушку. " +
                "Обход zapret это не лечит. DNS через HTTPS шифрует такие запросы, и подменить ответ провайдер уже не может."));

            Grid vg = new Grid();
            vg.Margin = new Thickness(0, 14, 0, 6);
            vg.ColumnDefinitions.Add(Theme.Col(-1));
            vg.ColumnDefinitions.Add(Theme.Col(0));
            dnsChip = Ui.Chip("Не проверено", "idle");
            dnsChip.Margin = new Thickness(0, 0, 10, 0);
            vg.Children.Add(dnsChip);
            dnsVerdict = Theme.Wrap("Нажмите «Проверить DNS», чтобы узнать, подменяет ли провайдер ответы.", 12.5, "Text2");
            dnsVerdict.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dnsVerdict, 1);
            vg.Children.Add(dnsVerdict);
            s2.Children.Add(vg);

            dnsInfo = new StackPanel();
            dnsInfo.Margin = new Thickness(0, 2, 0, 4);
            s2.Children.Add(dnsInfo);
            dnsRows = new StackPanel();
            s2.Children.Add(dnsRows);

            s2.Children.Add(Ui.Divider());
            TextBlock pt = Theme.T("Сервис DNS через HTTPS", 12.5, "Text2", true);
            pt.Margin = new Thickness(0, 8, 0, 6);
            s2.Children.Add(pt);
            string[] ids = { "cloudflare", "google" };
            Border seg = Ui.Segmented(new string[] { "Cloudflare", "Google" }, dnsSeg, delegate(int i) { dnsPick = ids[i]; RefreshDnsCard(); });
            seg.HorizontalAlignment = HorizontalAlignment.Left;
            s2.Children.Add(seg);
            dnsProviders = Theme.Wrap("", 11.5, "Text3");
            dnsProviders.Margin = new Thickness(0, 6, 0, 0);
            s2.Children.Add(dnsProviders);

            WrapPanel bw = new WrapPanel();
            bw.Margin = new Thickness(0, 12, 0, 2);
            dnsCheckBtn = Ui.Button("Проверить DNS", 0xE721, false);
            dnsCheckBtn.Margin = new Thickness(0, 0, 8, 6);
            dnsCheckBtn.Click += delegate { RunDnsCheck(); };
            bw.Children.Add(dnsCheckBtn);
            dnsApplyBtn = Ui.Button("Включить защищённый DNS", 0xE72E, true);
            dnsApplyBtn.Margin = new Thickness(0, 0, 8, 6);
            dnsApplyBtn.Click += delegate { ApplyDns(); };
            bw.Children.Add(dnsApplyBtn);
            dnsRestoreBtn = Ui.Button("Вернуть как было", 0xE7A7, false);
            dnsRestoreBtn.Margin = new Thickness(0, 0, 8, 6);
            dnsRestoreBtn.Click += delegate { RestoreDns(); };
            bw.Children.Add(dnsRestoreBtn);
            s2.Children.Add(bw);
            c2.Content = s2;
            dnsCard = c2;
            sp.Children.Add(c2);

            RefreshSiteList();
            RefreshDnsCard();
            return Theme.Scroll(sp);
        }

        // ====================== проверка сайта ======================

        async void RunSiteCheck(string input, string note)
        {
            if (siteChecking) return;
            string host = SiteCheck.NormalizeHost(input);
            if (host == null)
            {
                RenderSiteMessage("warn", "Не похоже на адрес сайта", "Введите домен, например rutracker.org, или скопируйте адрес из браузера целиком.");
                return;
            }
            siteChecking = true;
            Ui.SetButtonText(siteCheckBtn, "Проверяю…");
            siteCheckBtn.IsEnabled = false;
            RenderSiteMessage("busy", "Проверяю " + host + "…", "DNS провайдера и DNS через HTTPS, затем соединение, шифрование и ответ сервера. Это займёт до 15 секунд.");
            try
            {
                DohProvider first = Doh.Find(dnsPick) ?? Doh.Providers[0];
                DnsAnswer sys = null, sec = null;
                await Task.Run(delegate
                {
                    Task<DnsAnswer> ts = Task.Run(() => Doh.ResolveSystem(host, 6000));
                    sec = Doh.Resolve(first, host, 5000);
                    if (!sec.Ok)
                        foreach (DohProvider p in Doh.Providers)
                        {
                            if (p == first) continue;
                            sec = Doh.Resolve(p, host, 5000);
                            if (sec.Ok) break;
                        }
                    sys = ts.Result;
                });

                ProbeResult probe = null;
                Target t = new Target(host, "/", true);
                bool sysUsable = sys.Ok && sys.HasAddress && !Doh.LooksPoisoned(sys, sec);
                if (!sysUsable && sec.Ok && sec.HasAddress) t.FixedAddress = sec.Addresses[0];
                if (sysUsable || t.FixedAddress != null)
                {
                    if (engine.IsRunning) await Task.Delay(300);
                    probe = await NetCheck.ProbeWithRetry(t);
                }

                string inFile, exFile;
                string covered = SiteCheck.FindInFiles(engine.Lists, SiteCheck.IncludeFiles, host, out inFile);
                string excluded = SiteCheck.FindInFiles(engine.Lists, SiteCheck.ExcludeFiles, host, out exFile);
                SiteReport r = SiteCheck.Diagnose(host, sys, sec, probe, engine.IsRunning, covered, excluded);
                r.CoveredFile = inFile;
                RenderSiteReport(r, note);
                log.Add(r.Level == "ok" ? "ok" : r.Level == "err" ? "warn" : "info", "Проверка сайта " + host + ": " + r.Title +
                    (probe != null && !probe.Passed ? " (" + probe.Stage + ": " + probe.Detail + ")" : "") + ".");
            }
            catch (Exception ex)
            {
                RenderSiteMessage("err", "Проверка не выполнена", ex.Message);
            }
            finally
            {
                siteChecking = false;
                Ui.SetButtonText(siteCheckBtn, "Проверить");
                siteCheckBtn.IsEnabled = true;
            }
        }

        Grid SiteHeadline(string level, string title, string text)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 10, 0, 0);
            g.ColumnDefinitions.Add(Theme.Col(-1));
            g.ColumnDefinitions.Add(Theme.Col(0));
            Border ic = Ui.StatusIcon(32);
            Ui.SetStatus(ic, level);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.Margin = new Thickness(0, 2, 12, 0);
            g.Children.Add(ic);
            StackPanel t = new StackPanel();
            t.Children.Add(Theme.T(title, 14.5, level == "ok" ? "Ok" : level == "err" ? "Err" : level == "warn" ? "Warn" : "Text", true));
            TextBlock d = Theme.Wrap(text, 12.5, "Text2");
            d.Margin = new Thickness(0, 3, 0, 0);
            t.Children.Add(d);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        void RenderSiteMessage(string level, string title, string text)
        {
            siteResult.Children.Clear();
            siteResult.Children.Add(SiteHeadline(level, title, text));
            siteResult.Visibility = Visibility.Visible;
        }

        void RenderSiteReport(SiteReport r, string note)
        {
            siteResult.Children.Clear();
            if (note != null)
            {
                TextBlock n = Theme.Wrap(note, 12, "Text2");
                n.Margin = new Thickness(0, 10, 0, 0);
                siteResult.Children.Add(n);
            }
            siteResult.Children.Add(SiteHeadline(r.Level, r.Title + " · " + r.Host, r.Text));

            StackPanel det = new StackPanel();
            det.Margin = new Thickness(44, 8, 0, 0);
            det.Children.Add(DetailLine("DNS провайдера", r.System != null ? r.System.Describe() : "—", r.DnsPoisoned ? "Err" : "Text2"));
            det.Children.Add(DetailLine("DNS через HTTPS", r.Secure != null ? r.Secure.Describe() : "—", "Text2"));
            if (r.Probe != null)
                det.Children.Add(DetailLine("Соединение", (r.Probe.Passed ? "" : r.Probe.Stage + ": ") + r.Probe.Detail +
                    (r.Probe.Timing().Length > 0 ? " · " + r.Probe.Timing() : ""), r.Probe.Passed ? "Text2" : "Warn"));
            if (r.CoveredBy != null)
                det.Children.Add(DetailLine("Списки обхода", r.CoveredBy + " (" + r.CoveredFile + ")", "Text2"));
            if (r.ExcludedBy != null)
                det.Children.Add(DetailLine("Исключения", r.ExcludedBy, "Warn"));
            siteResult.Children.Add(det);

            WrapPanel act = new WrapPanel();
            act.Margin = new Thickness(44, 10, 0, 4);
            string host = r.Host;
            if (r.OfferAdd)
            {
                VButton b = Ui.Button("Добавить " + SiteCheck.ListEntry(host) + " в обход", 0xE710, true);
                b.Click += delegate { AddSiteToBypass(host); };
                act.Children.Add(Spaced(b));
            }
            if (r.OfferStart)
            {
                VButton b = Ui.Button("Включить обход", 0xE7E8, true);
                b.Click += async delegate
                {
                    if (busy || tuning || updating) return;
                    ClearGamePauseByUser();
                    if (await StartAsync(selected)) RunSiteCheck(host, "Обход включён — проверяю ещё раз.");
                };
                act.Children.Add(Spaced(b));
            }
            if (r.OfferDnsFix)
            {
                VButton b = Ui.Button("Включить защищённый DNS", 0xE72E, !r.OfferAdd);
                b.Click += delegate { dnsCard.BringIntoView(); RunDnsCheck(); };
                act.Children.Add(Spaced(b));
            }
            if (r.OfferStrategy)
            {
                VButton b = Ui.Button("Другая стратегия", 0xE8FD, false);
                b.Click += delegate { ShowPage("strategies"); };
                act.Children.Add(Spaced(b));
            }
            VButton again = Ui.Button("Проверить снова", 0xE72C, false);
            again.Click += delegate { RunSiteCheck(host, null); };
            act.Children.Add(Spaced(again));
            siteResult.Children.Add(act);
            siteResult.Visibility = Visibility.Visible;
        }

        static VButton Spaced(VButton b)
        {
            b.Margin = new Thickness(0, 0, 8, 6);
            return b;
        }

        static Grid DetailLine(string label, string value, string valueKey)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 1, 0, 1);
            g.ColumnDefinitions.Add(Theme.Col(132));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.Children.Add(Theme.T(label, 12, "Text3", false));
            TextBlock v = Theme.Wrap(value, 12, valueKey);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        async void AddSiteToBypass(string host)
        {
            string entry = SiteCheck.ListEntry(host);
            string path = Path.Combine(engine.Lists, SiteCheck.UserListFile);
            try
            {
                engine.EnsureUserLists();
                bool added = SiteCheck.AddEntry(path, entry);
                log.Add("ok", added ? "Домен " + entry + " добавлен в " + SiteCheck.UserListFile + "." : "Домен " + entry + " уже есть в " + SiteCheck.UserListFile + ".");
                RefreshSiteList();
            }
            catch (Exception ex)
            {
                log.Add("err", "Не удалось добавить домен: " + ex.Message);
                RenderSiteMessage("err", "Не удалось добавить домен", ex.Message);
                return;
            }
            if (engine.IsRunning && !busy && !tuning && !updating)
            {
                RenderSiteMessage("busy", entry + " добавлен в обход", "Перезапускаю обход с обновлённым списком и проверяю сайт ещё раз…");
                await RestartIfRunning();
                await Task.Delay(800);
                RunSiteCheck(host, "Домен " + entry + " добавлен, обход перезапущен. Результат повторной проверки:");
            }
            else RunSiteCheck(host, "Домен " + entry + " добавлен в список." + (engine.IsRunning ? " Перезапустите обход, когда он освободится." : ""));
        }

        async void RemoveSiteFromBypass(string entry)
        {
            try
            {
                if (!SiteCheck.RemoveEntry(Path.Combine(engine.Lists, SiteCheck.UserListFile), entry)) return;
                log.Add("info", "Домен " + entry + " убран из " + SiteCheck.UserListFile + ".");
                RefreshSiteList();
                if (engine.IsRunning && !busy && !tuning && !updating) await RestartIfRunning();
            }
            catch (Exception ex) { log.Add("err", "Не удалось убрать домен: " + ex.Message); }
        }

        void RefreshSiteList()
        {
            if (siteMyList == null) return;
            siteMyList.Children.Clear();
            List<string> list = SiteCheck.ReadUserEntries(Path.Combine(engine.Lists, SiteCheck.UserListFile));
            if (list.Count == 0)
            {
                TextBlock empty = Theme.Wrap("Пока пусто. Проверьте сайт выше — если его блокирует DPI, Veylix предложит добавить домен сюда.", 12, "Text3");
                empty.Margin = new Thickness(2, 2, 0, 4);
                siteMyList.Children.Add(empty);
                return;
            }
            foreach (string e in list)
            {
                string entry = e;
                Grid r = new Grid();
                r.Margin = new Thickness(0, 1, 0, 1);
                r.ColumnDefinitions.Add(Theme.Col(-1));
                r.ColumnDefinitions.Add(Theme.Col(0));
                r.ColumnDefinitions.Add(Theme.Col(-1));
                r.ColumnDefinitions.Add(Theme.Col(-1));
                TextBlock ic = Theme.Icon(0xE774, 13, "Text2");
                ic.VerticalAlignment = VerticalAlignment.Center;
                ic.Margin = new Thickness(4, 0, 10, 0);
                r.Children.Add(ic);
                TextBlock t = Theme.T(entry, 13, "Text", false);
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, 1);
                r.Children.Add(t);
                VButton chk = Ui.IconButton(0xE721, "Проверить сайт");
                chk.Click += delegate { siteInput.Text = entry; RunSiteCheck(entry, null); };
                Grid.SetColumn(chk, 2);
                r.Children.Add(chk);
                VButton del = Ui.IconButton(0xE711, "Убрать из обхода");
                del.Click += delegate { RemoveSiteFromBypass(entry); };
                Grid.SetColumn(del, 3);
                r.Children.Add(del);
                siteMyList.Children.Add(r);
            }
        }

        // ====================== защищённый DNS ======================

        async void ReloadDnsState()
        {
            if (dnsLoading || selfTest || dnsInfo == null) return;
            dnsLoading = true;
            try { dnsAdapters = await Task.Run(() => DnsFix.Snapshot()); }
            catch { }
            finally { dnsLoading = false; }
            RefreshDnsCard();
        }

        void RefreshDnsCard()
        {
            if (dnsInfo == null) return;
            int sel = 0;
            for (int i = 0; i < Doh.Providers.Length; i++) if (Doh.Providers[i].Id == dnsPick) sel = i;
            Ui.SelectSegment(dnsSeg, sel);

            dnsInfo.Children.Clear();
            DohProvider active = null;
            bool allSecure = dnsAdapters != null && dnsAdapters.Count > 0;
            if (dnsAdapters == null)
                dnsInfo.Children.Add(DetailLine("Подключения", selfTest ? "«Ethernet»: 192.168.1.1 — выдан роутером или провайдером" : "читаю настройки…", "Text3"));
            else
            {
                foreach (AdapterDns a in dnsAdapters)
                {
                    List<string> all = new List<string>(a.Servers4);
                    all.AddRange(a.Servers6);
                    DohProvider p = Doh.ByServers(all);
                    if (p != null && a.Encrypted) active = p;
                    else allSecure = false;
                    string servers = a.Servers4.Count > 0 ? string.Join(", ", a.Servers4.ToArray()) : string.Join(", ", a.Servers6.ToArray());
                    string src = a.Encrypted ? (p != null ? p.Title : "сервер") + ", DNS через HTTPS — защищён"
                        : p != null ? p.Title + ", без шифрования — провайдер может подменить ответ"
                        : a.Dhcp4 ? "выдан роутером или провайдером" : "задан вручную";
                    dnsInfo.Children.Add(DetailLine("«" + a.Name + "»", servers + " — " + src, a.Encrypted ? "Ok" : "Text2"));
                }
                if (dnsAdapters.Count == 0)
                    dnsInfo.Children.Add(DetailLine("Подключения", "активных подключений не найдено", "Warn"));
            }

            DohProvider pick = Doh.Find(dnsPick);
            bool same = allSecure && active == pick;
            Ui.SetButtonText(dnsApplyBtn, active == null ? "Включить защищённый DNS" : same ? "Применить заново" : "Переключить на " + pick.Title);
            dnsRestoreBtn.Visibility = DnsFix.HasBackup ? Visibility.Visible : Visibility.Collapsed;
            dnsApplyBtn.IsEnabled = !dnsBusy;
            dnsRestoreBtn.IsEnabled = !dnsBusy;
            dnsCheckBtn.IsEnabled = !dnsBusy;

            List<string> sp = new List<string>();
            foreach (DohProvider p in Doh.Providers)
            {
                DnsAnswer a;
                if (dnsSpeed.TryGetValue(p.Id, out a))
                    sp.Add(p.Title + " — " + (a.Ok ? a.Ms + " мс" : "недоступен"));
            }
            string note = DnsFix.DohSupported
                ? "Адреса: " + string.Join(", ", pick.V4) + ". Windows будет шифровать запросы к ним (DNS через HTTPS)."
                : "В этой версии Windows нет встроенного DNS через HTTPS: Veylix пропишет адреса " + string.Join(", ", pick.V4) + ", но запросы пойдут без шифрования — от подмены это помогает не всегда.";
            dnsProviders.Text = (sp.Count > 0 ? "Скорость ответа: " + string.Join(" · ", sp.ToArray()) + ". " : "") + note;
        }

        async void RunDnsCheck()
        {
            if (dnsBusy) return;
            dnsBusy = true;
            Ui.SetButtonText(dnsCheckBtn, "Проверяю…");
            Ui.SetChip(dnsChip, "Проверка", "busy");
            dnsVerdict.Text = "Сравниваю ответы DNS провайдера и DNS через HTTPS…";
            RefreshDnsCard();
            try
            {
                Dictionary<string, DnsAnswer> speed = new Dictionary<string, DnsAnswer>();
                List<DnsAnswer[]> pairs = new List<DnsAnswer[]>();
                DohProvider best = null;
                await Task.Run(delegate
                {
                    List<Task<DnsAnswer>> probes = new List<Task<DnsAnswer>>();
                    foreach (DohProvider p in Doh.Providers)
                    {
                        DohProvider pp = p;
                        probes.Add(Task.Run(() => Doh.Resolve(pp, "www.youtube.com", 6000)));
                    }
                    for (int i = 0; i < Doh.Providers.Length; i++)
                    {
                        DnsAnswer a = probes[i].Result;
                        speed[Doh.Providers[i].Id] = a;
                        if (a.Ok && (best == null || a.Ms < speed[best.Id].Ms)) best = Doh.Providers[i];
                    }
                    DohProvider use = speed[dnsPick].Ok ? Doh.Find(dnsPick) : best;
                    foreach (string h in DnsControlHosts)
                    {
                        string host = h;
                        Task<DnsAnswer> ts = Task.Run(() => Doh.ResolveSystem(host, 6000));
                        DnsAnswer sec = use != null ? Doh.Resolve(use, host, 6000) : null;
                        pairs.Add(new DnsAnswer[] { ts.Result, sec });
                    }
                });
                dnsSpeed = speed;
                if (!speed[dnsPick].Ok && best != null) dnsPick = best.Id;

                dnsRows.Children.Clear();
                int poisoned = 0;
                for (int i = 0; i < DnsControlHosts.Length; i++)
                {
                    DnsAnswer sys = pairs[i][0], sec = pairs[i][1];
                    bool bad = Doh.LooksPoisoned(sys, sec);
                    if (bad) poisoned++;
                    dnsRows.Children.Add(DetailLine(DnsControlHosts[i], "провайдер: " + sys.Describe() + (sec != null ? " · через HTTPS: " + sec.Describe() : ""), bad ? "Err" : "Text2"));
                }
                if (best == null)
                {
                    Ui.SetChip(dnsChip, "DoH недоступен", "warn");
                    dnsVerdict.Text = "Ни один сервис DNS через HTTPS не ответил. Включать его сейчас нельзя: без ответа от него перестанут открываться все сайты.";
                }
                else if (poisoned > 0)
                {
                    Ui.SetChip(dnsChip, "Подмена DNS", "err");
                    dnsVerdict.Text = "Провайдер подменяет DNS у " + poisoned + " из " + DnsControlHosts.Length + " проверенных сайтов. Включите защищённый DNS — это уберёт блокировки такого типа.";
                }
                else
                {
                    Ui.SetChip(dnsChip, "Подмены нет", "ok");
                    dnsVerdict.Text = "DNS провайдера отвечает честно. Защищённый DNS можно включить для приватности, но для обхода блокировок он сейчас не нужен.";
                }
                log.Add(poisoned > 0 ? "warn" : "info", "Проверка DNS: подмена у " + poisoned + " из " + DnsControlHosts.Length + " сайтов" +
                    (best != null ? ", быстрее всех отвечает " + best.Title + " (" + speed[best.Id].Ms + " мс)." : ", DNS через HTTPS недоступен."));
            }
            catch (Exception ex)
            {
                Ui.SetChip(dnsChip, "Ошибка", "err");
                dnsVerdict.Text = "Проверка не выполнена: " + ex.Message;
            }
            finally
            {
                dnsBusy = false;
                Ui.SetButtonText(dnsCheckBtn, "Проверить DNS");
                RefreshDnsCard();
            }
        }

        async void ApplyDns()
        {
            if (dnsBusy) return;
            DohProvider p = Doh.Find(dnsPick);
            List<AdapterDns> ads = DnsFix.ActiveAdapters();
            if (ads.Count == 0) { VDialog.Show(this, "Нет подключений", "Не найдено ни одного активного подключения к сети.", "warn"); return; }
            List<string> names = new List<string>();
            foreach (AdapterDns a in ads) names.Add("«" + a.Name + "»");
            if (!VDialog.Ask(this, "Включить защищённый DNS " + p.Title + "?",
                "Veylix пропишет DNS " + string.Join(", ", p.V4) + " для подключений " + string.Join(", ", names.ToArray()) +
                (DnsFix.DohSupported ? " и включит шифрование запросов (DNS через HTTPS)." : ".") +
                " Прежние настройки сохранятся: их вернёт кнопка «Вернуть как было».", null, "Включить", "Отмена", "info")) return;

            dnsBusy = true;
            Ui.SetChip(dnsChip, "Настройка", "busy");
            dnsVerdict.Text = "Проверяю, что " + p.Title + " отвечает, и меняю настройки Windows…";
            RefreshDnsCard();
            try
            {
                DnsAnswer test = await Task.Run(() => Doh.Resolve(p, "www.msftconnecttest.com", 6000));
                if (!test.Ok)
                {
                    Ui.SetChip(dnsChip, "Не включён", "err");
                    dnsVerdict.Text = p.Title + " сейчас не отвечает (" + test.Error + "). Выберите другой сервис — включать недоступный нельзя, иначе сайты перестанут открываться.";
                    return;
                }
                string err = await Task.Run(() => DnsFix.Apply(p));
                if (err != null) log.Add("warn", "Защищённый DNS: часть настроек не применилась — " + err);

                // убеждаемся, что DNS работает; если нет — сразу возвращаем как было
                await Task.Delay(1500);
                DnsAnswer a1 = await Task.Run(() => Doh.ResolveSystem("www.msftconnecttest.com", 8000));
                DnsAnswer a2 = a1.HasAddress ? a1 : await Task.Run(() => Doh.ResolveSystem("ya.ru", 8000));
                if (!a2.HasAddress)
                {
                    string back = await Task.Run(() => DnsFix.Restore());
                    log.Add("err", "Защищённый DNS: после включения DNS перестал отвечать — настройки возвращены" + (back != null ? " (с ошибками: " + back + ")" : "") + ".");
                    Ui.SetChip(dnsChip, "Отменено", "err");
                    dnsVerdict.Text = "После включения " + p.Title + " Windows не смогла получить ни одного адреса, поэтому Veylix сразу вернул прежние настройки. Попробуйте другой сервис.";
                    VDialog.Show(this, "Защищённый DNS не включён", dnsVerdict.Text, "err");
                    return;
                }
                log.Add("ok", "Защищённый DNS включён: " + p.Title + " (" + string.Join(", ", p.V4) + ")" + (DnsFix.DohSupported ? ", запросы шифруются." : "."));
                events.Add("dns", "ok", "Защищённый DNS: " + p.Title, "Прежние настройки сохранены", "sites");
                Toast("dns-on", "ok", "Защищённый DNS включён", p.Title + (DnsFix.DohSupported ? " · DNS через HTTPS" : ""), "sites", false);
                Ui.SetChip(dnsChip, "Включён", "ok");
                dnsVerdict.Text = "Готово: DNS " + p.Title + (DnsFix.DohSupported ? " с шифрованием" : "") + ". Браузеру может понадобиться перезапуск, чтобы забыть старые ответы.";
            }
            catch (Exception ex)
            {
                log.Add("err", "Защищённый DNS: " + ex.Message);
                Ui.SetChip(dnsChip, "Ошибка", "err");
                dnsVerdict.Text = ex.Message;
            }
            finally
            {
                dnsBusy = false;
                RefreshDnsCard();
                ReloadDnsState();
            }
        }

        async void RestoreDns()
        {
            if (dnsBusy || !DnsFix.HasBackup) return;
            dnsBusy = true;
            RefreshDnsCard();
            try
            {
                string err = await Task.Run(() => DnsFix.Restore());
                if (err != null)
                {
                    log.Add("err", "DNS: не удалось вернуть прежние настройки — " + err);
                    Ui.SetChip(dnsChip, "Ошибка", "err");
                    dnsVerdict.Text = "Не всё удалось вернуть: " + err;
                }
                else
                {
                    log.Add("ok", "DNS: возвращены прежние настройки.");
                    Ui.SetChip(dnsChip, "Как было", "idle");
                    dnsVerdict.Text = "Возвращены настройки DNS, которые были до Veylix.";
                }
            }
            catch (Exception ex) { log.Add("err", "DNS: " + ex.Message); }
            finally
            {
                dnsBusy = false;
                RefreshDnsCard();
                ReloadDnsState();
            }
        }
    }
}
