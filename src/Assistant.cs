using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veylix
{
    // ===================== данные для анализа =====================

    public class AssistantInput
    {
        public DateTime Now = DateTime.Now;
        public bool Admin = true;
        public bool EngineRunning;
        public string Strategy;
        public List<DiagItem> Diag;              // null — диагностика не запускалась
        public DateTime DiagTime;
        public ServiceResult[] Checks;           // null — проверки не было; [0] — Интернет, далее сервисы
        public DateTime ChecksTime;
        public bool ChecksWithEngine;            // проверка шла при включённом обходе
        public DateTime LastExit = DateTime.MinValue;
        public int ExitCode = -1;
        public DateTime LastStartFail = DateTime.MinValue;
        public string Installed, Latest;
        public bool TuneNoWinner;
        public DateTime TuneTime;
        public List<string[]> PulseDown = new List<string[]>();   // {сервис, адрес, подробности, неудач подряд}
    }

    public class AssistStep
    {
        public string Text;
        public string Action;        // id команды приложения или null (действие выполняет пользователь)
        public string Label;         // подпись кнопки
        public bool ChangesState;    // перед выполнением нужно подтверждение
    }

    public class Finding
    {
        public string Id, Title, Level, Confidence;   // Level: err | warn | info | ok; Confidence: Подтверждено | Вероятно | Возможно
        public List<string> Facts = new List<string>();
        public string Guess;                          // предположение (явно отделено от фактов)
        public List<AssistStep> Steps = new List<AssistStep>();
        public List<string> Anchors = new List<string>();   // к каким ошибкам интерфейса относится («Как исправить?»)
    }

    // ===================== локальные правила =====================

    // Справочник рекомендаций без внешних сервисов. Каждое правило опирается на конкретные измерения;
    // где причина не измерена, она записывается как предположение. Ни одно правило не советует
    // отключать защиту системы, проверку сертификатов или выполнять опасные команды.
    public static class Assistant
    {
        public const string Confirmed = "Подтверждено", Likely = "Вероятно", Possible = "Возможно";

        static Finding F(string id, string level, string conf, string title)
        {
            Finding f = new Finding();
            f.Id = id; f.Level = level; f.Confidence = conf; f.Title = title;
            return f;
        }

        static void Step(Finding f, string text, string action, string label, bool changes)
        {
            AssistStep s = new AssistStep();
            s.Text = text; s.Action = action; s.Label = label; s.ChangesState = changes;
            f.Steps.Add(s);
        }

        static DiagItem D(AssistantInput i, string name)
        {
            if (i.Diag == null) return null;
            foreach (DiagItem d in i.Diag) if (d.Name == name) return d;
            return null;
        }

        static string At(DateTime t)
        {
            return t.ToString("HH:mm");
        }

        public static List<Finding> Analyze(AssistantInput i)
        {
            List<Finding> r = new List<Finding>();
            string dsrc = i.Diag != null ? "Диагностика в " + At(i.DiagTime) : null;

            // ---- права ----
            if (!i.Admin)
            {
                Finding f = F("admin", "err", Confirmed, "Veylix запущен без прав администратора");
                f.Facts.Add("Проверено сейчас: у процесса Veylix нет прав администратора.");
                f.Facts.Add("Драйверу WinDivert, через который работает winws.exe, нужны права администратора.");
                Step(f, "Закройте Veylix (трей → Выход) и запустите его снова через «Запуск от имени администратора».", null, null, false);
                Step(f, "Либо включите «Запускать вместе с Windows» в настройках — задание запускается с нужными правами.", "page.settings", "Настройки", false);
                f.Anchors.Add("diag:Права администратора");
                f.Anchors.Add("exit");
                r.Add(f);
            }

            // ---- файлы ----
            DiagItem files = D(i, "Исполняемые файлы");
            if (files != null && files.Level == "err")
            {
                Finding f = F("files", "err", Confirmed, "Не хватает файлов обхода");
                f.Facts.Add(dsrc + ": " + files.Value + ".");
                f.Guess = "Чаще всего такие файлы помещает в карантин антивирус: драйвер WinDivert похож на «HackTool». Это предположение — Veylix не видит журнал антивируса.";
                Step(f, "Откройте карантин антивируса и восстановите файлы, если они там.", null, null, false);
                Step(f, "Добавьте папку обхода в исключения антивируса. Сам антивирус не отключайте.", "folder.open", "Открыть папку", false);
                Step(f, "Либо заново установите файлы Flowseal в разделе «Обновления».", "page.updates", "Обновления", false);
                f.Anchors.Add("diag:Исполняемые файлы");
                f.Anchors.Add("exit");
                r.Add(f);
            }

            // ---- служба BFE ----
            DiagItem bfe = D(i, "Base Filtering Engine");
            if (bfe != null && bfe.Level == "err")
            {
                Finding f = F("bfe", "err", Confirmed, "Остановлена служба Base Filtering Engine");
                f.Facts.Add(dsrc + ": служба BFE — " + bfe.Value + ".");
                f.Facts.Add("Без BFE драйвер WinDivert не работает.");
                Step(f, "Откройте «Службы» (Win+R → services.msc), найдите «Служба базовой фильтрации», тип запуска — «Автоматически», затем «Запустить». Veylix сам службы не меняет.", null, null, false);
                f.Anchors.Add("diag:Base Filtering Engine");
                f.Anchors.Add("exit");
                r.Add(f);
            }

            // ---- посторонний winws / служба zapret ----
            DiagItem ext = D(i, "Посторонний winws.exe");
            DiagItem zs = D(i, "Служба zapret (service.bat)");
            if (ext != null || (zs != null && zs.Level == "warn"))
            {
                Finding f = F("external", "warn", Confirmed, "Работает ещё один экземпляр обхода");
                if (ext != null) f.Facts.Add(dsrc + ": посторонний winws.exe — " + ext.Value + ".");
                if (zs != null && zs.Level == "warn") f.Facts.Add(dsrc + ": служба zapret — " + zs.Value + ".");
                f.Facts.Add("Два обхода одновременно мешают друг другу.");
                Step(f, "Если открыт general*.bat — закройте его окно.", null, null, false);
                Step(f, "Если установлена служба zapret — удалите её через service.bat → «Remove Services».", "folder.open", "Открыть папку", false);
                Step(f, "Veylix сам остановит посторонний winws.exe, когда вы включите обход.", null, null, false);
                f.Anchors.Add("diag:Посторонний winws.exe");
                f.Anchors.Add("diag:Служба zapret (service.bat)");
                r.Add(f);
            }

            // ---- конфликтующие программы ----
            DiagItem conf = D(i, "Конфликтующее ПО");
            if (conf != null && conf.Level == "warn")
            {
                Finding f = F("conflicts", "warn", Likely, "Найдены программы, известные конфликтами с zapret");
                f.Facts.Add(dsrc + ": " + conf.Value + ".");
                f.Guess = "Эти программы тоже перехватывают сетевой трафик. Мешают ли они именно сейчас, проверкой не установить — только сравнением.";
                Step(f, "Временно закройте перечисленные программы и повторите проверку сервисов.", "check.services", "Проверить сервисы", false);
                if (conf.Value.Contains("Check Point"))
                    Step(f, "Check Point — средство защиты. Не отключайте его самостоятельно; если это рабочий компьютер, обратитесь к администратору.", null, null, false);
                f.Anchors.Add("diag:Конфликтующее ПО");
                r.Add(f);
            }

            // ---- VPN ----
            DiagItem vpn = D(i, "VPN-адаптеры");
            if (vpn != null)
            {
                Finding f = F("vpn", "warn", Possible, "Активен VPN-адаптер");
                f.Facts.Add(dsrc + ": " + vpn.Value + ".");
                f.Guess = "Если VPN включён, трафик идёт через него, и проверки показывают работу VPN, а не стратегии обхода.";
                Step(f, "Если хотите проверить именно обход — отключите VPN в его программе и повторите проверку.", "check.services", "Проверить сервисы", false);
                f.Anchors.Add("diag:VPN-адаптеры");
                r.Add(f);
            }

            // ---- прокси ----
            DiagItem proxy = D(i, "Системный прокси");
            if (proxy != null && proxy.Level == "warn")
            {
                Finding f = F("proxy", "warn", Possible, "В Windows включён системный прокси");
                f.Facts.Add(dsrc + ": " + proxy.Value + ".");
                f.Guess = "Браузеры идут через прокси, а проверки Veylix — напрямую. Если прокси не работает, сайты в браузере не откроются, хотя проверки будут успешными.";
                Step(f, "Откройте настройки прокси Windows и убедитесь, что он вам нужен. Veylix сам прокси не меняет.", "win.proxy", "Настройки прокси", false);
                f.Anchors.Add("diag:Системный прокси");
                r.Add(f);
            }

            // ---- TCP timestamps ----
            DiagItem ts = D(i, "TCP timestamps");
            if (ts != null && ts.Level == "warn")
            {
                Finding f = F("timestamps", "warn", Confirmed, "Выключены TCP timestamps");
                f.Facts.Add(dsrc + ": TCP timestamps " + ts.Value + ".");
                f.Facts.Add("Часть стратегий использует приём fooling=ts, которому нужны timestamps.");
                Step(f, "Если вы готовы изменить сетевой параметр Windows: в командной строке от администратора выполните «netsh interface tcp set global timestamps=enabled». Veylix этого сам не делает.", null, null, false);
                f.Anchors.Add("diag:TCP timestamps");
                r.Add(f);
            }

            // ---- аварийное завершение / не запустился ----
            bool recentExit = i.LastExit != DateTime.MinValue && i.Now - i.LastExit < TimeSpan.FromMinutes(30) && !i.EngineRunning;
            bool recentFail = i.LastStartFail != DateTime.MinValue && i.Now - i.LastStartFail < TimeSpan.FromMinutes(30) && !i.EngineRunning;
            if (recentExit || recentFail)
            {
                Finding f = F("exit", "err", Confirmed, recentExit ? "Обход завершился сам" : "Обход не запустился");
                if (recentExit) f.Facts.Add("winws.exe завершился в " + At(i.LastExit) + (i.ExitCode != -1 ? " с кодом " + i.ExitCode : "") + ".");
                if (recentFail) f.Facts.Add("Запуск в " + At(i.LastStartFail) + ": winws.exe закрылся сразу после старта.");
                f.Guess = "Частые причины: антивирус блокирует WinDivert, работает другой обход или VPN, нет прав администратора. Что именно — покажет диагностика.";
                Step(f, "Запустите диагностику — она проверит права, файлы, службу BFE и конфликтующие программы.", "diag.run", "Диагностика", false);
                Step(f, "Посмотрите сообщения winws.exe в журнале (фильтр «winws»).", "page.journal", "Журнал", false);
                Step(f, "Попробуйте включить обход ещё раз.", "engine.toggle", "Включить обход", true);
                f.Anchors.Add("exit");
                r.Add(f);
            }

            // ---- проверки доступности ----
            bool checksOld = i.Checks != null && i.Now - i.ChecksTime > TimeSpan.FromMinutes(30);
            if (i.Checks != null && i.Checks.Length > 0)
            {
                ServiceResult inet = i.Checks[0];
                string csrc = "Проверка в " + At(i.ChecksTime) + (i.ChecksWithEngine ? " (обход был включён)" : " (обход был выключен)");
                if (inet.Status == ServiceStatus.Unavailable)
                {
                    Finding f = F("internet", "err", Confirmed, "Нет доступа к интернету");
                    f.Facts.Add(csrc + ": нейтральные адреса не ответили — " + inet.Summary + ".");
                    f.Facts.Add("Без интернета о блокировке Discord и YouTube судить нельзя.");
                    Step(f, "Проверьте подключение: кабель или Wi-Fi, роутер.", null, null, false);
                    Step(f, "Если это общественный Wi-Fi — откройте любой сайт в браузере и пройдите авторизацию.", null, null, false);
                    Step(f, "Повторите проверку.", "check.services", "Проверить", false);
                    f.Anchors.Add("svc:Интернет");
                    r.Add(f);
                }
                else
                {
                    for (int k = 1; k < i.Checks.Length; k++)
                    {
                        Finding f = ServiceFinding(i.Checks[k], csrc, i.ChecksWithEngine);
                        if (f != null) r.Add(f);
                    }
                }
            }

            // ---- Network Pulse ----
            foreach (string[] pd in i.PulseDown)
            {
                Finding f = F("pulse-" + pd[0], "warn", Confirmed, pd[0] + " не отвечает в Network Pulse");
                f.Facts.Add("Network Pulse: " + pd[1] + " — " + pd[3] + " неудачных измерения подряд, последнее: " + pd[2] + ".");
                Step(f, "Запустите полную проверку, чтобы понять, на каком этапе обрывается соединение.", "check.services", "Проверить сервисы", false);
                f.Anchors.Add("svc:" + pd[0]);
                r.Add(f);
            }

            // ---- автоподбор без результата ----
            if (i.TuneNoWinner)
            {
                Finding f = F("tune", "warn", Confirmed, "Автоподбор не нашёл рабочую стратегию");
                f.Facts.Add("Автоподбор в " + At(i.TuneTime) + ": ни одна стратегия не открыла Discord и YouTube при работающем интернете.");
                f.Guess = "Возможно, провайдер блокирует сильнее, чем умеют стратегии этой версии, или трафик перехватывает VPN/антивирус.";
                Step(f, "Обновите список IPSet и повторите подбор.", "ipset.update", "Обновить IPSet", true);
                Step(f, "Проверьте, нет ли новой версии Flowseal.", "updates.check", "Проверить обновления", false);
                Step(f, "Запустите диагностику — она покажет VPN и конфликтующие программы.", "diag.run", "Диагностика", false);
                f.Anchors.Add("tune");
                r.Add(f);
            }

            // ---- версия Flowseal ----
            if (i.Latest != null && Updater.CompareVersions(i.Latest, i.Installed) > 0)
            {
                Finding f = F("update", "info", Confirmed, "Доступна новая версия Flowseal");
                f.Facts.Add("Установлена " + (i.Installed ?? "?") + ", на GitHub — " + i.Latest + ".");
                f.Guess = "В новых версиях обычно обновляют стратегии под изменившиеся блокировки; поможет ли это у вас — проверится после установки.";
                Step(f, "Посмотрите описание релиза и установите обновление (с проверкой SHA-256 и резервной копией).", "page.updates", "Обновления", false);
                f.Anchors.Add("update");
                r.Add(f);
            }

            // ---- данных мало / всё хорошо ----
            bool problems = false;
            foreach (Finding f in r) if (f.Level == "err" || f.Level == "warn") problems = true;
            if (i.Diag == null && i.Checks == null)
            {
                Finding f = F("nodata", "info", Confirmed, "Недостаточно данных для выводов");
                f.Facts.Add("В этом сеансе ещё не было ни диагностики, ни проверки сервисов.");
                Step(f, "Запустите диагностику: она проверит систему и доступность сервисов (только чтение, ничего не меняет).", "diag.run", "Запустить диагностику", false);
                r.Add(f);
            }
            else if (checksOld)
            {
                Finding f = F("stale", "info", Confirmed, "Результаты проверки устарели");
                f.Facts.Add("Последняя проверка сервисов была в " + At(i.ChecksTime) + " — больше 30 минут назад.");
                Step(f, "Повторите проверку, чтобы выводы соответствовали текущему состоянию.", "check.services", "Проверить сервисы", false);
                r.Add(f);
            }
            else if (i.Checks == null)
            {
                Finding f = F("nochecks", "info", Confirmed, "Сервисы ещё не проверялись");
                f.Facts.Add("Есть диагностика системы, но нет проверки Discord и YouTube.");
                Step(f, "Проверьте доступность сервисов.", "check.services", "Проверить сервисы", false);
                r.Add(f);
            }
            if (!problems && i.Checks != null && !checksOld && i.Checks.Length >= 3 &&
                i.Checks[1].Status == ServiceStatus.Ok && i.Checks[2].Status == ServiceStatus.Ok)
            {
                Finding f = F("ok", "ok", Confirmed, "Всё работает");
                f.Facts.Add("Проверка в " + At(i.ChecksTime) + ": Discord " + i.Checks[1].Passed + "/" + i.Checks[1].Total + ", YouTube " + i.Checks[2].Passed + "/" + i.Checks[2].Total + " адресов ответили.");
                if (i.EngineRunning) f.Facts.Add("Обход включён" + (i.Strategy != null ? ", стратегия " + i.Strategy : "") + ".");
                r.Add(f);
            }

            // порядок: ошибки, предупреждения, сведения, «всё хорошо»
            List<Finding> sorted = new List<Finding>();
            foreach (string lvl in new string[] { "err", "warn", "info", "ok" })
                foreach (Finding f in r) if (f.Level == lvl) sorted.Add(f);
            return sorted;
        }

        // Разбор недоступного сервиса по этапу, на котором оборвалось соединение
        static Finding ServiceFinding(ServiceResult s, string src, bool engineOn)
        {
            if (s.Status == ServiceStatus.Ok || s.Status == ServiceStatus.Unknown) return null;
            if (s.Status == ServiceStatus.NoNetwork) return null;   // уже объяснено правилом «нет интернета»
            int dns = 0, cert = 0, tls = 0, tcp = 0, freeze = 0, total = 0;
            ProbeResult sample = null;
            foreach (ProbeResult p in s.Probes)
            {
                if (p.Passed) continue;
                total++;
                if (sample == null || p.Target.Main) sample = p;
                if (p.Stage == "DNS" && p.Status == ProbeStatus.Blocked) dns++;
                else if (p.Stage == "TLS" && p.Detail.Contains("сертификат")) cert++;
                else if (p.Stage == "TLS") tls++;
                else if (p.Stage == "TCP") tcp++;
                else if (p.Status == ProbeStatus.Partial) freeze++;
            }
            string lvl = s.Status == ServiceStatus.Partial ? "warn" : "err";
            Finding f;
            string fact = src + ": " + s.Name + " — " + s.Summary + ".";
            if (dns > 0 && dns >= total / 2.0)
            {
                f = F("svc-" + s.Name, lvl, Likely, s.Name + ": похоже на блокировку через DNS");
                f.Facts.Add(fact);
                f.Facts.Add("DNS вернул «домен не найден» или адрес-заглушку (" + sample.Target.Host + ").");
                f.Guess = "Провайдер подменяет ответы DNS. winws.exe DNS не меняет, поэтому обход здесь не поможет.";
                Step(f, "Включите в Windows зашифрованный DNS (DNS over HTTPS) в параметрах сетевого адаптера.", "win.network", "Параметры сети", false);
                Step(f, "После этого повторите проверку.", "check.services", "Проверить", false);
            }
            else if (cert > 0)
            {
                f = F("svc-" + s.Name, lvl, Likely, s.Name + ": ответ подменяется по пути");
                f.Facts.Add(fact);
                f.Facts.Add("Сертификат " + sample.Target.Host + " не прошёл проверку подлинности.");
                f.Guess = "Так выглядит заглушка провайдера или страница авторизации общественного Wi-Fi.";
                Step(f, "Не отключайте проверку сертификатов — она защищает от подмены.", null, null, false);
                Step(f, "Если сеть общественная — пройдите авторизацию в браузере.", null, null, false);
                Step(f, engineOn ? "Попробуйте другую стратегию через автоподбор." : "Включите обход и повторите проверку.",
                    engineOn ? "tune.toggle" : "engine.toggle", engineOn ? "Автоподбор" : "Включить обход", true);
            }
            else if (freeze > 0 && tls == 0 && tcp == 0)
            {
                f = F("svc-" + s.Name, "warn", Likely, s.Name + ": передача обрывается после начала");
                f.Facts.Add(fact);
                f.Guess = "Соединение устанавливается, но данные замирают на первых килобайтах — характерно для ограничения по объёму (TCP 16–20 КБ).";
                Step(f, "Подберите другую стратегию: лаборатория стратегий проверит их на тех же адресах.", "tune.toggle", "Автоподбор", true);
            }
            else if (tls > 0)
            {
                if (engineOn)
                {
                    f = F("svc-" + s.Name, lvl, Likely, s.Name + ": текущая стратегия не обходит блокировку");
                    f.Facts.Add(fact);
                    f.Guess = "TLS-рукопожатие зависает или сбрасывается — так выглядит DPI-блокировка по имени сайта. Стратегия не справляется.";
                    Step(f, "Запустите автоподбор: Veylix проверит стратегии по очереди и оставит лучшую.", "tune.toggle", "Автоподбор", true);
                    Step(f, "Включите режим IPSet «Список» и обновите IPSet.", "ipset.update", "Обновить IPSet", true);
                    Step(f, "Проверьте обновления Flowseal.", "updates.check", "Проверить обновления", false);
                }
                else
                {
                    f = F("svc-" + s.Name, lvl, Likely, s.Name + ": похоже на блокировку провайдером");
                    f.Facts.Add(fact);
                    f.Guess = "Проверка шла с выключенным обходом. Зависание или сброс TLS-рукопожатия — признак DPI-блокировки, которую обход и должен снимать.";
                    Step(f, "Включите обход.", "engine.toggle", "Включить обход", true);
                    Step(f, "Повторите проверку.", "check.services", "Проверить", false);
                }
            }
            else if (tcp > 0)
            {
                f = F("svc-" + s.Name, lvl, Possible, s.Name + ": не удаётся подключиться к серверу");
                f.Facts.Add(fact);
                f.Guess = "TCP-соединение не устанавливается. Возможна блокировка по IP-адресу или проблема маршрута — по одной проверке не различить.";
                Step(f, "Обновите IPSet и включите режим «Список» — так обход охватит адреса сервиса.", "ipset.update", "Обновить IPSet", true);
                Step(f, "Повторите проверку чуть позже.", "check.services", "Проверить", false);
            }
            else
            {
                f = F("svc-" + s.Name, lvl, Possible, s.Name + ": доступен не полностью");
                f.Facts.Add(fact);
                f.Guess = "Причину по этим данным определить нельзя.";
                Step(f, "Запустите диагностику для подробного разбора по каждому адресу.", "diag.run", "Диагностика", false);
            }
            f.Anchors.Add("svc:" + s.Name);
            return f;
        }
    }

    // ===================== страница «Помощник» =====================

    public partial class MainWindow
    {
        StackPanel assistList;
        TextBlock assistSummary;
        List<Finding> findings = new List<Finding>();
        DateTime lastChecksTime, lastDiagTime, lastTuneTime;
        bool lastChecksWithEngine, lastTuneNoWinner;

        AssistantInput CollectAssistantInput()
        {
            AssistantInput a = new AssistantInput();
            a.Admin = Diagnostics.IsAdmin();
            a.EngineRunning = engine.IsRunning;
            a.Strategy = engine.Current != null ? engine.Current.Display : null;
            a.Diag = lastDiag;
            a.DiagTime = lastDiagTime;
            a.Checks = lastChecks;
            a.ChecksTime = lastChecksTime;
            a.ChecksWithEngine = lastChecksWithEngine;
            a.LastExit = lastExitAt;
            a.ExitCode = lastExitCode;
            a.LastStartFail = lastStartFailAt;
            a.Installed = engine.InstalledVersion();
            a.Latest = latest != null ? latest.Version : null;
            a.TuneNoWinner = lastTuneNoWinner;
            a.TuneTime = lastTuneTime;
            foreach (PulseSeries ps in pulseMon.All.Values)
            {
                if (!pulseMon.Enabled.Contains(ps.Id) || ps.Fails < 2) continue;
                PulseSample last = ps.Last;
                a.PulseDown.Add(new string[] { ps.Name, ps.Target.Host, last != null ? last.Detail : "", ps.Fails.ToString() });
            }
            return a;
        }

        void Analyze()
        {
            findings = Assistant.Analyze(CollectAssistantInput());
        }

        bool HasFix(string anchor)
        {
            foreach (Finding f in findings)
                if (f.Anchors.Contains(anchor) && f.Level != "ok") return true;
            return false;
        }

        // Кнопка «Как исправить?» рядом с ошибкой
        VButton FixButton(string anchor)
        {
            VButton b = LinkButton("Как исправить?");
            b.ToolTip = "Помощник объяснит вероятную причину и предложит шаги";
            b.Click += delegate { OpenAssistant(anchor); };
            return b;
        }

        void OpenAssistant(string anchor)
        {
            Analyze();
            ShowPage("assistant");
            RenderAssistant(anchor);
        }

        FrameworkElement BuildAssistant()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(14));
            g.RowDefinitions.Add(Theme.RowDef(0));
            Card c = new Card(22);
            c.Inner = new Thickness(22, 16, 22, 14);
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel left = new StackPanel();
            left.Children.Add(Ui.Heading("Помощник Veylix",
                "Объясняет результаты проверок простым языком по встроенным правилам — без интернета и внешних сервисов. " +
                "Факты отделены от предположений. Сам ничего не исправляет: действия выполняются только по вашей кнопке."));
            assistSummary = Theme.Wrap("", 12, "Text2");
            assistSummary.Margin = new Thickness(0, 8, 24, 0);
            left.Children.Add(assistSummary);
            head.Children.Add(left);
            StackPanel btns = Theme.Row();
            btns.VerticalAlignment = VerticalAlignment.Top;
            VButton re = Ui.Button("Обновить анализ", 0xE72C, false);
            re.Margin = new Thickness(0, 0, 8, 0);
            re.Click += delegate { Analyze(); RenderAssistant(null); };
            VButton dg = Ui.Button("Диагностика", 0xE9D9, true);
            dg.ToolTip = "Собрать свежие данные: система и доступность сервисов";
            dg.Click += delegate { commandById["diag.run"].Run(); };
            btns.Children.Add(re);
            btns.Children.Add(dg);
            Grid.SetColumn(btns, 1);
            head.Children.Add(btns);
            c.Content = head;
            g.Children.Add(c);

            assistList = new StackPanel();
            assistList.Margin = new Thickness(0, 0, 6, 0);
            ScrollViewer sv = Theme.Scroll(assistList);
            Grid.SetRow(sv, 2);
            g.Children.Add(sv);
            return g;
        }

        void RenderAssistant(string focus)
        {
            if (assistList == null) return;
            assistList.Children.Clear();
            int errs = 0, warns = 0;
            foreach (Finding f in findings) { if (f.Level == "err") errs++; else if (f.Level == "warn") warns++; }
            List<string> src = new List<string>();
            src.Add(lastDiag != null ? "диагностика в " + lastDiagTime.ToString("HH:mm") : "диагностики не было");
            src.Add(lastChecks != null ? "проверка сервисов в " + lastChecksTime.ToString("HH:mm") : "проверки сервисов не было");
            if (pulseMon.TotalSamples > 0) src.Add("Network Pulse: " + pulseMon.TotalSamples + " измерений");
            assistSummary.Text = (errs + warns == 0 ? "Проблем не найдено. " : "Проблем: " + errs + ", предупреждений: " + warns + ". ") +
                "Данные: " + string.Join(", ", src) + ". Анализ обновлён в " + DateTime.Now.ToString("HH:mm:ss") + ".";
            FrameworkElement toFocus = null;
            foreach (Finding f in findings)
            {
                bool hit = focus != null && f.Anchors.Contains(focus);
                FrameworkElement card = FindingCard(f, hit);
                if (hit && toFocus == null) toFocus = card;
                assistList.Children.Add(card);
            }
            if (focus != null && toFocus == null && findings.Count > 0)
            {
                TextBlock note = Theme.Wrap("Для этой ошибки нет подтверждённого объяснения по текущим данным. Запустите диагностику — помощник учтёт свежие результаты.", 12.5, "Text2");
                note.Margin = new Thickness(4, 0, 4, 12);
                assistList.Children.Insert(0, note);
            }
            if (toFocus != null) toFocus.Loaded += delegate { toFocus.BringIntoView(); };
            if (toFocus != null && toFocus.IsLoaded) toFocus.BringIntoView();
        }

        FrameworkElement FindingCard(Finding f, bool focused)
        {
            Card c = focused ? new Card(20, "Surface", "Accent") : new Card(20);
            if (focused) c.BorderThickness = new Thickness(2);
            c.Margin = new Thickness(0, 0, 0, 12);
            c.Inner = new Thickness(20, 14, 20, 14);
            StackPanel sp = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(42));
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            Border ic = Ui.StatusIcon(30);
            Ui.SetStatus(ic, f.Level == "info" ? "info" : f.Level);
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(ic);
            TextBlock t = Theme.T(f.Title, 15, "Text", true);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            Grid.SetColumn(t, 1);
            head.Children.Add(t);
            Border conf = Ui.Chip(f.Confidence, f.Confidence == Assistant.Confirmed ? "info" : f.Confidence == Assistant.Likely ? "warn" : "idle");
            conf.ToolTip = f.Confidence == Assistant.Confirmed ? "Вывод следует из измерений напрямую"
                : f.Confidence == Assistant.Likely ? "Сильный признак, но не прямое доказательство" : "Одна из возможных причин — нужна дополнительная проверка";
            Grid.SetColumn(conf, 2);
            head.Children.Add(conf);
            sp.Children.Add(head);

            if (f.Facts.Count > 0)
            {
                sp.Children.Add(Section("Факты"));
                foreach (string fact in f.Facts) sp.Children.Add(Bullet("✓", fact, "Ok", "Text"));
            }
            if (f.Guess != null)
            {
                sp.Children.Add(Section("Предположение"));
                TextBlock g = Theme.Wrap(f.Guess, 12.5, "Text2");
                g.FontStyle = FontStyles.Italic;
                g.Margin = new Thickness(42, 0, 0, 2);
                sp.Children.Add(g);
            }
            if (f.Steps.Count > 0)
            {
                sp.Children.Add(Section(f.Level == "ok" ? "Дальше" : "Что сделать"));
                int n = 1;
                foreach (AssistStep st in f.Steps) sp.Children.Add(StepRow(n++, st));
            }
            c.Content = sp;
            return c;
        }

        static TextBlock Section(string title)
        {
            TextBlock t = Theme.T(title.ToUpperInvariant(), 10.5, "Text3", true);
            t.Margin = new Thickness(42, 10, 0, 4);
            return t;
        }

        static FrameworkElement Bullet(string mark, string text, string markKey, string textKey)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(42, 1, 0, 1);
            g.ColumnDefinitions.Add(Theme.Col(18));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.Children.Add(Theme.T(mark, 12, markKey, true));
            TextBlock t = Theme.Wrap(text, 12.5, textKey);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        FrameworkElement StepRow(int n, AssistStep st)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(42, 2, 0, 4);
            g.ColumnDefinitions.Add(Theme.Col(22));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            g.Children.Add(Theme.T(n + ".", 12.5, "Accent", true));
            TextBlock t = Theme.Wrap(st.Text, 12.5, "Text");
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            AppCommand cmd;
            if (st.Action != null && commandById.TryGetValue(st.Action, out cmd))
            {
                VButton b = Ui.Button(st.Label ?? cmd.ShortTitle, cmd.Glyph, false);
                b.Height = 30;
                b.Margin = new Thickness(12, 0, 0, 0);
                b.VerticalAlignment = VerticalAlignment.Center;
                string why = cmd.Reason();
                if (why != null) { b.IsEnabled = false; b.ToolTip = "Сейчас недоступно: " + why; }
                else b.ToolTip = st.ChangesState ? "Изменит состояние обхода — сначала спросим подтверждение" : cmd.CurrentTitle;
                b.Click += delegate
                {
                    if (st.ChangesState && !VDialog.Ask(this, cmd.CurrentTitle + "?", "Помощник предлагает: " + st.Text + "\n\nВыполнить сейчас?", null, "Выполнить", "Отмена", "info"))
                        return;
                    cmd.Run();
                };
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            return g;
        }
    }
}
