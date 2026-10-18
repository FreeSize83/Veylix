using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;

namespace Veylix.Tests
{
    // Простые тесты без внешних библиотек: build.bat test
    // Флаг --net дополнительно выполняет реальные сетевые проверки (нужен интернет).
    public static class TestProgram
    {
        static int passed, failed;
        static string tmp;

        static void Check(bool cond, string name)
        {
            if (cond) { passed++; }
            else { failed++; Console.WriteLine("  FAIL: " + name); }
        }

        static void Eq(object expected, object actual, string name)
        {
            Check(object.Equals(expected, actual), name + " (ожидалось «" + expected + "», получено «" + actual + "»)");
        }

        static void Section(string name)
        {
            Console.WriteLine("— " + name);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool net = Array.IndexOf(args, "--net") >= 0;
            Program.SetupTls();
            tmp = Path.Combine(Path.GetTempPath(), "veylix-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            Settings.OverrideDir(Path.Combine(tmp, "data"));
            try
            {
                Run("Настройки", TestSettings);
                Run("Выбор темы и расписание", TestThemeResolve);
                Run("Палитры и контраст", TestPalettes);
                Run("Версии и источник обновлений", TestVersions);
                Run("Разбор релиза GitHub", TestReleaseParse);
                Run("Безопасная распаковка", TestSafePaths);
                Run("Установка обновления и откат", TestInstall);
                Run("Очистка журнала", TestSanitize);
                Run("Стратегии и аргументы winws", TestEngine);
                Run("Автозапуск: задание Планировщика", TestAutorun);
                Run("Вердикты сетевых проверок", TestVerdicts);
                Run("Диагностика", TestDiagnostics);
                Run("Команды и палитра", TestCommands);
                Run("Network Pulse", TestPulse);
                Run("События и статистика сеанса", TestEvents);
                Run("Лаборатория стратегий", TestLab);
                Run("Theme Studio", TestStudio);
                Run("Пчёлка-компаньон", TestCompanion);
                Run("Помощник", TestAssistant);
                Run("Обновление Veylix: версии и релизы", TestAppReleases);
                Run("Обновление Veylix: пакет и план", TestAppPackage);
                if (net) Run("Сеть (реальные запросы)", TestNetwork);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
            Console.WriteLine();
            Console.WriteLine("Итого: пройдено " + passed + ", провалено " + failed);
            return failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action a)
        {
            Section(name);
            try { a(); }
            catch (Exception ex) { failed++; Console.WriteLine("  EXCEPTION: " + ex); }
        }

        // ---------------------------------------------------------------

        static void TestSettings()
        {
            Directory.CreateDirectory(Settings.Dir);
            string original = "LastStrategy=ALT3\r\nTheme=purple\r\nThemeMode=bogus\r\nFutureKey=abc=def\r\nCloseToTray=0\r\n";
            File.WriteAllText(Settings.FilePath, original);
            Settings s = Settings.Load();
            Eq("ALT3", s.LastStrategy, "LastStrategy прочитан");
            Eq("purple", s.Theme, "Theme прочитан");
            Eq("manual", s.ThemeMode, "некорректный ThemeMode заменён значением по умолчанию");
            Eq(false, s.CloseToTray, "CloseToTray=0");
            Eq(original, File.ReadAllText(Settings.FilePath), "Load не перезаписывает файл");

            s.Theme = "glass";
            s.ThemeMode = "schedule";
            Eq(null, s.Save(), "Save без ошибок");
            string saved = File.ReadAllText(Settings.FilePath);
            Check(saved.Contains("FutureKey=abc=def"), "неизвестный ключ сохранён");
            Check(!File.Exists(Settings.FilePath + ".tmp"), "временный файл удалён");
            Settings s2 = Settings.Load();
            Eq("glass", s2.Theme, "тема сохранилась между запусками");
            Eq("schedule", s2.ThemeMode, "режим сохранился между запусками");
            s2.Theme = "night";
            s2.Save();
            Eq("night", Settings.Load().Theme, "иллюстрированная тема сохраняется");

            // новые настройки: значения по умолчанию, сохранение и защита от слишком частых измерений
            Settings s3 = Settings.Load();
            Eq(60, s3.PulseInterval, "интервал Pulse по умолчанию 60 с");
            Eq(Settings.DefaultPinned, s3.PinnedActions, "закреплённые действия по умолчанию");
            s3.PreferredStrategy = "ALT3";
            s3.PulseInterval = 120;
            s3.Toasts = false;
            s3.PinnedActions = "check.services,page.events";
            s3.Save();
            Settings s4 = Settings.Load();
            Eq("ALT3", s4.PreferredStrategy, "предпочтительная стратегия сохраняется");
            Eq(120, s4.PulseInterval, "интервал Pulse сохраняется");
            Eq(false, s4.Toasts, "отключение уведомлений сохраняется");
            Eq("check.services,page.events", s4.PinnedActions, "закреплённые действия сохраняются");
            File.WriteAllText(Settings.FilePath, "PulseInterval=1\r\nPulseHistory=99999\r\n");
            Settings s5 = Settings.Load();
            Eq(60, s5.PulseInterval, "интервал 1 с отклонён");
            Eq(60, s5.PulseHistory, "история 99999 отклонена");
            Eq("ALT3", s2.LastStrategy, "стратегия сохранилась");
        }

        static void TestThemeResolve()
        {
            DateTime d = new DateTime(2026, 10, 15);
            Eq("purple-dark", Theme.Resolve("purple", "schedule", true, d.AddHours(6).AddMinutes(59)).Id, "06:59 — тёмная");
            Eq("purple-light", Theme.Resolve("purple", "schedule", false, d.AddHours(7)).Id, "07:00 — светлая");
            Eq("purple-light", Theme.Resolve("purple", "schedule", false, d.AddHours(18).AddMinutes(59)).Id, "18:59 — светлая");
            Eq("purple-dark", Theme.Resolve("purple", "schedule", true, d.AddHours(19)).Id, "19:00 — тёмная");
            Eq("graphite-dark", Theme.Resolve("cream", "schedule", true, d.AddHours(22)).Id, "Cream по расписанию вечером → Graphite");
            Eq("cream-light", Theme.Resolve("graphite", "schedule", false, d.AddHours(10)).Id, "Graphite по расписанию днём → Cream");
            Eq("cream-light", Theme.Resolve("cream", "manual", false, d).Id, "Cream вручную всегда светлая");
            Eq("graphite-dark", Theme.Resolve("graphite", "manual", true, d.AddHours(12)).Id, "Graphite вручную всегда тёмная");
            Eq("mint-dark", Theme.Resolve("mint", "manual", false, d).Id, "Mint вручную следует системе (тёмная)");
            Eq("mint-light", Theme.Resolve("mint", "system", true, d).Id, "Mint как в Windows (светлая)");
            Eq("glass-dark", Theme.Resolve("glass", "system", false, d).Id, "Glass как в Windows (тёмная)");
            Check(Theme.Resolve("glass", "manual", true, d).Glass, "Glass помечена как стеклянная");
            Eq("cream-light", Theme.Resolve("unknown", "manual", true, d).Id, "неизвестная тема → Cream");
            Eq("night-dark", Theme.Resolve("night", "manual", true, d.AddHours(12)).Id, "Bee Night вручную всегда тёмная");
            Eq("meadow-light", Theme.Resolve("meadow", "manual", false, d).Id, "Morning Meadow вручную всегда светлая");
            Eq("meadow-light", Theme.Resolve("sunset", "schedule", true, d.AddHours(10)).Id, "Sunset днём по расписанию → Meadow");
            Eq("sunset-dark", Theme.Resolve("sunset", "schedule", true, d.AddHours(21)).Id, "Sunset вечером по расписанию");
            Eq("night-dark", Theme.Resolve("meadow", "system", false, d).Id, "Meadow при тёмной Windows → Bee Night");
            Eq("aurora-dark", Theme.Resolve("aurora", "system", false, d).Id, "Aurora при тёмной Windows");
            Check(Theme.Build("night", true).Scene == "night" && Theme.Build("night", true).Glow, "у Bee Night есть сцена и свечение");
            Check(Theme.Build("cream", false).Scene == null, "у обычных тем сцены нет");
        }

        static double Lum(Color c)
        {
            Func<double, double> ch = delegate(double v) { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        static Color Over(Color top, Color bottom)
        {
            double a = top.A / 255.0;
            return Color.FromRgb((byte)(top.R * a + bottom.R * (1 - a)), (byte)(top.G * a + bottom.G * (1 - a)), (byte)(top.B * a + bottom.B * (1 - a)));
        }

        static Color Solid(Brush b)
        {
            SolidColorBrush s = b as SolidColorBrush;
            if (s != null) return s.Color;
            GradientBrush g = b as GradientBrush;
            return g != null ? g.GradientStops[g.GradientStops.Count - 1].Color : Colors.Gray;
        }

        static double Contrast(Color a, Color b)
        {
            double x = Lum(a) + 0.05, y = Lum(b) + 0.05;
            return Math.Max(x, y) / Math.Min(x, y);
        }

        static void TestPalettes()
        {
            string[] keys = { "Bg", "Nav", "Surface", "Surface2", "Stroke", "StrokeStrong", "Text", "Text2", "Text3", "Accent", "AccentSoft", "OnAccent",
                              "Ok", "OkSoft", "Warn", "WarnSoft", "Err", "ErrSoft", "Info", "InfoSoft", "Hover", "Press", "Input", "Popup", "ScrollThumb", "Sheen",
                              "Tile1", "Tile2", "Tile3", "Tile4", "TileFg", "TileFg2", "TileIcon", "TileIconBg", "TileStroke" };
            foreach (string fam in Theme.Families)
            {
                foreach (bool dark in new bool[] { false, true })
                {
                    Palette p = Theme.Build(fam, dark);
                    foreach (string k in keys) Check(p.Res.ContainsKey(k), p.Id + ": есть ключ " + k);
                    // поверхность карточки поверх фона окна (для стекла — с учётом прозрачности)
                    Color bg = Solid((Brush)p.Res["Bg"]);
                    Color surface = Over(Solid((Brush)p.Res["Surface"]), bg);
                    double t1 = Contrast(Solid((Brush)p.Res["Text"]), surface);
                    double t2 = Contrast(Solid((Brush)p.Res["Text2"]), surface);
                    double ac = Contrast(Solid((Brush)p.Res["OnAccent"]), Solid((Brush)p.Res["Accent"]));
                    double ok = Contrast(Over(Solid((Brush)p.Res["Ok"]), surface), surface);
                    Check(t1 >= 7.0, p.Id + ": контраст основного текста " + t1.ToString("0.0") + " ≥ 7");
                    Check(t2 >= 4.5, p.Id + ": контраст второстепенного текста " + t2.ToString("0.0") + " ≥ 4.5");
                    Check(ac >= 4.5, p.Id + ": текст на акцентной кнопке " + ac.ToString("0.0") + " ≥ 4.5");
                    Check(ok >= 3.0, p.Id + ": цвет «успех» на карточке " + ok.ToString("0.0") + " ≥ 3");
                    for (int i = 1; i <= 4; i++)
                    {
                        // подпись плитки: худший случай по обоим концам градиента
                        GradientBrush gb = p.Res["Tile" + i] as GradientBrush;
                        Color[] cs = gb != null ? new Color[] { gb.GradientStops[0].Color, gb.GradientStops[1].Color } : new Color[] { Solid((Brush)p.Res["Tile" + i]) };
                        foreach (Color tc in cs)
                        {
                            Color under = Over(tc, bg);
                            double tf = Contrast(Over(Solid((Brush)p.Res["TileFg2"]), under), under);
                            Check(tf >= 4.5, p.Id + ": подпись на плитке " + i + " — контраст " + tf.ToString("0.0") + " ≥ 4.5");
                        }
                    }
                }
            }
        }

        static void TestVersions()
        {
            Eq(1, Updater.CompareVersions("1.10.3", "1.9.9"), "1.10.3 > 1.9.9");
            Eq(0, Updater.CompareVersions("1.10.3", "1.10.3"), "равные версии");
            Eq(0, Updater.CompareVersions("1.10", "1.10.0"), "1.10 == 1.10.0");
            Eq(1, Updater.CompareVersions("v1.10.4", "1.10.3"), "префикс v");
            Eq(-1, Updater.CompareVersions("1.10.3", "1.11"), "1.10.3 < 1.11");
            Eq(1, Updater.CompareVersions("1.0", null), "неизвестная установленная версия считается старой");
            Check(Updater.IsTrustedDownloadUrl("https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.3/zapret-discord-youtube-1.10.3.zip"), "официальный адрес допустим");
            Check(!Updater.IsTrustedDownloadUrl("http://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.3/a.zip"), "http запрещён");
            Check(!Updater.IsTrustedDownloadUrl("https://github.com/Someone/zapret-discord-youtube/releases/download/1.0/a.zip"), "чужой репозиторий запрещён");
            Check(!Updater.IsTrustedDownloadUrl("https://github.com.evil.example/Flowseal/zapret-discord-youtube/releases/download/1.0/a.zip"), "поддельный домен запрещён");
            Check(!Updater.IsTrustedDownloadUrl("https://github.com/Flowseal/zapret-discord-youtube/releases/download/../../x/a.zip"), "переход .. запрещён");
        }

        static string Fixture(string name)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string c in new string[] { Path.Combine(dir, "..", "tests", "fixtures", name), Path.Combine(dir, "tests", "fixtures", name), Path.Combine(dir, "fixtures", name) })
                if (File.Exists(c)) return File.ReadAllText(c, Encoding.UTF8);
            throw new FileNotFoundException(name);
        }

        static void TestReleaseParse()
        {
            string json = Fixture("release-1.10.3.json");
            ReleaseInfo r = Updater.ParseRelease(json);
            Eq("1.10.3", r.Version, "версия");
            Check(r.HtmlUrl.StartsWith(Updater.RepoUrl), "ссылка на релиз из официального репозитория");
            Check(r.Body.Length > 50, "есть описание релиза");
            Eq("zapret-discord-youtube-1.10.3.zip", r.ZipName, "выбран .zip");
            Eq("244314ae1c24538a0d751601da8e0c925c843371eec4456eb15f14c4fd6b7058", r.ZipSha256, "SHA-256 из поля digest");
            Check(r.CanInstall, "доступна проверяемая установка");

            string noDigest = json.Replace("sha256:244314ae1c24538a0d751601da8e0c925c843371eec4456eb15f14c4fd6b7058", "");
            Check(!Updater.ParseRelease(noDigest).CanInstall, "без digest автоустановка запрещена");
            string evil = json.Replace("https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.3/zapret-discord-youtube-1.10.3.zip", "https://evil.example/z.zip");
            Check(Updater.ParseRelease(evil).ZipUrl == null, "архив с чужого адреса не принимается");
        }

        static void TestSafePaths()
        {
            string root = Path.Combine(tmp, "root");
            Check(Updater.SafeEntryPath(root, "a/b.txt") != null, "обычный путь");
            Check(Updater.SafeEntryPath(root, "../x.txt") == null, "../ запрещён");
            Check(Updater.SafeEntryPath(root, "a/../../x.txt") == null, "a/../../ запрещён");
            Check(Updater.SafeEntryPath(root, "C:\\Windows\\x.dll") == null, "абсолютный путь запрещён");
            Check(Updater.SafeEntryPath(root, "/abs.txt") == null, "корневой путь запрещён");
            Check(Updater.SafeEntryPath(root, "x:stream") == null, "альтернативный поток NTFS запрещён");
        }

        static string MakeZip(string name, Dictionary<string, string> files)
        {
            string p = Path.Combine(tmp, name);
            using (FileStream fs = File.Create(p))
            using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (KeyValuePair<string, string> kv in files)
                {
                    ZipArchiveEntry e = z.CreateEntry(kv.Key);
                    using (StreamWriter w = new StreamWriter(e.Open())) w.Write(kv.Value);
                }
            }
            return p;
        }

        static Dictionary<string, string> Package(string ver)
        {
            string top = "zapret-discord-youtube-" + ver + "/";
            Dictionary<string, string> f = new Dictionary<string, string>();
            f[top + "bin/winws.exe"] = "fake-winws " + ver;
            f[top + "bin/WinDivert.dll"] = "dll";
            f[top + "bin/WinDivert64.sys"] = "sys";
            f[top + "service.bat"] = "set \"LOCAL_VERSION=" + ver + "\"";
            f[top + "general.bat"] = "start \"zapret\" /min \"%BIN%winws.exe\" --wf-tcp=80,443";
            f[top + "lists/list-general.txt"] = "discord.com";
            f[top + "utils/check_updates.enabled"] = "ENABLED";
            return f;
        }

        static void TestInstall()
        {
            string root = Path.Combine(tmp, "inst", "zapret");
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "lists"));
            Directory.CreateDirectory(Path.Combine(root, "utils"));
            File.WriteAllText(Path.Combine(root, "service.bat"), "set \"LOCAL_VERSION=1.10.3\"");
            File.WriteAllText(Path.Combine(root, "bin", "winws.exe"), "old");
            File.WriteAllText(Path.Combine(root, "lists", "list-general-user.txt"), "my.example");
            File.WriteAllText(Path.Combine(root, "lists", "ipset-all.txt"), "203.0.113.113/32");
            File.WriteAllText(Path.Combine(root, "utils", "game_filter.enabled"), "mode=tcp");
            Action noop = delegate { };
            Action<string> quiet = delegate(string m) { };
            int stops = 0;
            Action stop = delegate { stops++; };

            // 1. неверная контрольная сумма
            string zip = MakeZip("ok.zip", Package("9.9.9"));
            string err = Updater.InstallFromZip(zip, new string('0', 64), "9.9.9", root, quiet, stop);
            Check(err != null && err.Contains("контрольная сумма"), "неверный SHA-256 отклонён: " + err);
            Eq("old", File.ReadAllText(Path.Combine(root, "bin", "winws.exe")), "после отказа файлы не тронуты");
            Eq(0, stops, "обход не останавливался при отказе");

            // 2. архив с выходом за пределы папки
            Dictionary<string, string> evil = Package("9.9.9");
            evil["../../evil.txt"] = "pwned";
            string ez = MakeZip("evil.zip", evil);
            err = Updater.InstallFromZip(ez, Updater.Sha256File(ez), "9.9.9", root, quiet, stop);
            Check(err != null && err.Contains("Небезопасный путь"), "zip-slip отклонён: " + err);
            Check(!File.Exists(Path.Combine(tmp, "evil.txt")) && !File.Exists(Path.Combine(tmp, "inst", "evil.txt")), "файл вне папки не создан");

            // 3. неполный архив
            Dictionary<string, string> broken = Package("9.9.9");
            broken.Remove("zapret-discord-youtube-9.9.9/bin/WinDivert64.sys");
            string bz = MakeZip("broken.zip", broken);
            err = Updater.InstallFromZip(bz, Updater.Sha256File(bz), "9.9.9", root, quiet, stop);
            Check(err != null && err.Contains("WinDivert64.sys"), "неполный архив отклонён: " + err);
            Eq("old", File.ReadAllText(Path.Combine(root, "bin", "winws.exe")), "после неполного архива файлы не тронуты");

            // 4. успешная установка
            err = Updater.InstallFromZip(zip, Updater.Sha256File(zip), "9.9.9", root, quiet, stop);
            Eq(null, err, "установка без ошибок");
            Eq(1, stops, "обход остановлен ровно один раз");
            Eq("fake-winws 9.9.9", File.ReadAllText(Path.Combine(root, "bin", "winws.exe")), "новая версия на месте");
            Eq("my.example", File.ReadAllText(Path.Combine(root, "lists", "list-general-user.txt")), "пользовательский список перенесён");
            Eq("203.0.113.113/32", File.ReadAllText(Path.Combine(root, "lists", "ipset-all.txt")), "режим IPSet перенесён");
            Eq("mode=tcp", File.ReadAllText(Path.Combine(root, "utils", "game_filter.enabled")), "игровой фильтр перенесён");
            Check(!File.Exists(Path.Combine(root, "utils", "check_updates.enabled")), "флаг проверки обновлений — как у пользователя (был выключен)");
            Eq("9.9.9", File.ReadAllText(Path.Combine(root, Engine.UpdatedMarker)), "записан маркер версии");
            string backup = Updater.LatestBackup(root);
            Check(backup != null && File.ReadAllText(Path.Combine(backup, "bin", "winws.exe")) == "old", "резервная копия старой версии");
            Check(!Directory.Exists(root + ".staging-9.9.9"), "временная папка удалена");

            // 5. откат
            System.Threading.Thread.Sleep(1100);   // метка времени резервной копии — с точностью до секунды
            err = Updater.Rollback(root, noop);
            Eq(null, err, "откат без ошибок");
            Eq("old", File.ReadAllText(Path.Combine(root, "bin", "winws.exe")), "после отката — прежняя версия");
        }

        static void TestSanitize()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string raw = "Файлы: " + Path.Combine(profile, "AppData", "Local", "Veylix") + " user " + Environment.UserName +
                         " pc " + Environment.MachineName + " ip 185.12.34.56 url https://bob:secret@proxy.example:8080/x?token=abc123&a=1 v6 2a02:6b8:0:1::feed";
            string s = LogStore.Sanitize(raw);
            Check(!s.Contains(profile), "путь профиля скрыт");
            Check(s.Contains("%LOCALAPPDATA%"), "путь заменён переменной");
            if (Environment.UserName.Length >= 3) Check(s.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0, "имя пользователя скрыто");
            if (Environment.MachineName.Length >= 3) Check(s.IndexOf(Environment.MachineName, StringComparison.OrdinalIgnoreCase) < 0, "имя компьютера скрыто");
            Check(s.Contains("185.12.x.x") && !s.Contains("34.56"), "IPv4 замаскирован");
            Check(!s.Contains("secret") && !s.Contains("bob:"), "логин и пароль в URL скрыты");
            Check(s.Contains("token=***") && !s.Contains("abc123"), "токен скрыт");
            Check(!s.Contains("feed"), "IPv6 замаскирован");
            Check(LogStore.Redact("Bearer abcdefghijklmnop").Contains("***"), "Bearer-токен скрыт");
            Check(LogStore.Sanitize("Flowseal 1.10.3 · ALT3").Contains("1.10.3"), "номер версии не портится");
        }

        static void TestAutorun()
        {
            string exe = @"C:\Program Files\Veylix & Co\Veylix.exe";
            string xml = Engine.AutorunXml(exe, @"PC\Пользователь");
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            bool parsed = true;
            try { doc.LoadXml(xml.Substring(xml.IndexOf('<', 1))); } catch { parsed = false; }
            Check(parsed, "XML задания корректен (спецсимволы в пути экранированы)");
            Check(xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"), "запуск и от батареи");
            Check(xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"), "не останавливается при переходе на батарею");
            Check(xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"), "без лимита 72 часа");
            Check(xml.Contains("<RunLevel>HighestAvailable</RunLevel>"), "с правами администратора");
            Check(xml.Contains("<Arguments>--autostart</Arguments>"), "ключ --autostart");
            Check(xml.Contains("<WorkingDirectory>C:\\Program Files\\Veylix &amp; Co</WorkingDirectory>"), "рабочая папка — папка exe");

            Check(Engine.AutorunHealthy(xml, exe), "своё задание считается исправным");
            Check(Engine.AutorunHealthy(xml, exe.ToUpperInvariant()), "путь сравнивается без учёта регистра");
            Check(!Engine.AutorunHealthy(xml, @"D:\Other\Veylix.exe"), "Veylix перенесён — задание нужно обновить");
            Check(!Engine.AutorunHealthy(xml.Replace("<DisallowStartIfOnBatteries>false", "<DisallowStartIfOnBatteries>true"), exe), "ограничение по батарее — задание нужно обновить");
            Check(!Engine.AutorunHealthy(xml.Replace("<ExecutionTimeLimit>PT0S", "<ExecutionTimeLimit>PT72H"), exe), "лимит 72 часа — задание нужно обновить");
            Check(!Engine.AutorunHealthy(xml.Replace("<Settings>", "<Settings><Enabled>false</Enabled>"), exe), "отключённое задание — нужно обновить");
            // так выглядело задание прежних версий (schtasks /Create ... /TR "\"exe\" --autostart")
            string legacy = "<Task><Settings><DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries><ExecutionTimeLimit>PT72H</ExecutionTimeLimit></Settings>" +
                            "<Actions><Exec><Command>\"" + exe.Replace("&", "&amp;") + "\"</Command><Arguments>--autostart</Arguments></Exec></Actions></Task>";
            Check(!Engine.AutorunHealthy(legacy, exe), "задание прежней версии обновляется");
            Check(!Engine.AutorunHealthy(null, exe), "нет XML — не исправно");
        }

        static void TestEngine()
        {
            string root = Engine.Locate();
            Check(File.Exists(Path.Combine(root, "bin", "winws.exe")), "встроенный архив распакован");
            Engine e = new Engine(root);
            Eq("1.10.3", e.InstalledVersion(), "версия Flowseal из service.bat");
            List<Strategy> list = e.LoadStrategies();
            Eq(22, list.Count, "найдено стратегий");
            Eq("general", list[0].Display, "первая — general");
            int okArgs = 0;
            foreach (Strategy s in list)
            {
                string a = e.BuildArgs(s);
                if (a != null && a.Contains("--wf-tcp=") && !a.Contains("%") && a.Contains(e.Lists)) okArgs++;
                else Console.WriteLine("    плохие аргументы: " + s.Display);
            }
            Eq(list.Count, okArgs, "аргументы всех стратегий собраны, плейсхолдеры подставлены");

            e.SetGameMode("all");
            Check(e.BuildArgs(list[0]).Contains("1024-65535"), "игровой фильтр добавляет порты");
            Engine e2 = new Engine(root);
            Eq("all", e2.GameMode, "игровой фильтр сохранён в файле");
            e.SetGameMode("disabled");
            Check(!e.BuildArgs(list[0]).Contains("1024-65535"), "игровой фильтр выключается");

            // IPSet: прямой выбор режима, список не теряется при переходах
            string ipset = Path.Combine(e.Lists, "ipset-all.txt");
            string ipsetSaved = File.Exists(ipset) ? File.ReadAllText(ipset) : null;
            File.WriteAllText(ipset, "1.2.3.0/24" + Environment.NewLine);
            if (File.Exists(ipset + ".backup")) File.Delete(ipset + ".backup");
            Eq("loaded", e.GetIpsetMode(), "IPSet: список загружен");
            Eq(null, e.SetIpsetMode("any"), "IPSet → Все IP");
            Eq("any", e.GetIpsetMode(), "IPSet: режим Все IP");
            Check(e.HasIpsetList, "IPSet: список сохранён в резервной копии");
            Eq(null, e.SetIpsetMode("none"), "IPSet → Выкл");
            Eq("none", e.GetIpsetMode(), "IPSet: режим Выкл");
            Eq(null, e.SetIpsetMode("loaded"), "IPSet → Список");
            Eq("1.2.3.0/24" + Environment.NewLine, File.ReadAllText(ipset), "IPSet: возвращён тот же список");
            Eq(null, e.SetIpsetMode("loaded"), "IPSet: тот же режим — без изменений");
            Eq(null, e.SetIpsetMode("none"), "IPSet → Выкл из списка");
            File.Delete(ipset + ".backup");
            Check(!e.HasIpsetList, "IPSet: без резервной копии списка нет");
            Check(e.SetIpsetMode("loaded") != null, "IPSet: «Список» без копии — понятная ошибка");
            if (ipsetSaved != null) File.WriteAllText(ipset, ipsetSaved); else File.Delete(ipset);

            // пользовательские списки переживают повторную распаковку
            e.EnsureUserLists();
            string user = Path.Combine(e.Lists, "list-general-user.txt");
            File.WriteAllText(user, "custom.example");
            File.Delete(Path.Combine(root, ".version"));
            Engine.Locate();
            Eq("custom.example", File.ReadAllText(user), "список пользователя не затёрт при распаковке");

            Check(!e.IsRunning, "без запуска процесс не числится работающим");
            e.Stop();   // не должен падать, если ничего не запущено
            Check(true, "Stop без запущенного процесса");
        }

        static ProbeResult P(string host, bool main, ProbeStatus st, string stage)
        {
            ProbeResult p = new ProbeResult();
            p.Target = new Target(host, "/", main);
            p.Status = st;
            p.Stage = stage;
            p.Detail = st.ToString();
            return p;
        }

        static void TestVerdicts()
        {
            ServiceResult r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Ok, "HTTP"), P("b", false, ProbeStatus.Ok, "HTTP") }, true);
            Eq(ServiceStatus.Ok, r.Status, "все адреса ответили");
            Eq(4, r.Score, "оценка 2 за каждый адрес");

            r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Ok, "HTTP"), P("b", false, ProbeStatus.Timeout, "TLS") }, true);
            Eq(ServiceStatus.Partial, r.Status, "частичная доступность");
            Check(r.Summary.StartsWith("Частично"), "формулировка «Частично»");

            r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Timeout, "TLS"), P("b", false, ProbeStatus.Timeout, "TLS") }, true);
            Eq(ServiceStatus.Unavailable, r.Status, "все таймауты при работающем интернете — недоступен");
            Check(r.Summary.Contains("таймаут"), "причина — таймаут");

            r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Timeout, "TLS") }, false);
            Eq(ServiceStatus.NoNetwork, r.Status, "без интернета не утверждаем блокировку");

            r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Blocked, "TLS"), P("b", false, ProbeStatus.Blocked, "TLS") }, true);
            Eq(ServiceStatus.Unavailable, r.Status, "сбросы — недоступен");
            Check(r.Summary.Contains("сбрасывается"), "причина — сброс");

            r = NetCheck.Verdict("X", new ProbeResult[] { P("a", true, ProbeStatus.Partial, "HTTP") }, true);
            Eq(ServiceStatus.Partial, r.Status, "обрыв передачи — частично");

            ProbeResult c = NetCheck.CombineAttempts(P("a", true, ProbeStatus.Timeout, "TLS"), P("a", true, ProbeStatus.Ok, "HTTP"));
            Check(c.Passed && c.Flaky, "успех со второй попытки помечен как нестабильный");
            c = NetCheck.CombineAttempts(P("a", true, ProbeStatus.Timeout, "TLS"), P("a", true, ProbeStatus.Timeout, "TLS"));
            Check(c.Detail.Contains("дважды"), "повторная неудача отмечена");
        }

        static AppCommand C(string id, string title, string group, string keywords)
        {
            AppCommand c = new AppCommand();
            c.Id = id; c.Title = title; c.Group = group; c.Keywords = keywords;
            return c;
        }

        static void TestCommands()
        {
            List<AppCommand> list = new List<AppCommand>();
            list.Add(C("page.journal", "Открыть журнал", "Разделы", "лог логи log"));
            list.Add(C("check.services", "Проверить сервисы", "Проверки", "доступность discord youtube"));
            list.Add(C("updates.check", "Проверить обновления Flowseal", "Проверки", ""));
            list.Add(C("theme.night", "Тема: Bee Night", "Оформление", "тема оформление цвет"));
            list.Add(C("pulse.toggle", "Запустить Network Pulse", "Проверки", "мониторинг задержка пульс"));

            Eq(5, AppCommand.Filter(list, "").Count, "пустой запрос — все команды");
            List<AppCommand> r = AppCommand.Filter(list, "провер");
            Eq(3, r.Count, "«провер» — два названия и одна команда из группы «Проверки»");
            Eq("pulse.toggle", r[2].Id, "совпадение только по группе ранжируется ниже совпадения в названии");
            Eq("check.services", r[0].Id, "при равной оценке сохраняется порядок регистрации");
            Eq("updates.check", AppCommand.Filter(list, "обновл")[0].Id, "поиск по слову в середине названия");
            Eq("page.journal", AppCommand.Filter(list, "лог")[0].Id, "поиск по ключевым словам");
            Eq("check.services", AppCommand.Filter(list, "youtube")[0].Id, "поиск по ключевому слову latin");
            Eq("pulse.toggle", AppCommand.Filter(list, "пульс")[0].Id, "русское ключевое слово к английскому названию");
            Eq("theme.night", AppCommand.Filter(list, "ТЕМА night")[0].Id, "несколько слов и регистр");
            Eq(0, AppCommand.Filter(list, "абракадабра").Count, "нет совпадений");
            AppCommand ee = C("x", "Ёлка", "g", "");
            Check(AppCommand.Score(ee, "елка") > 0, "ё и е не различаются");

            Eq("Ctrl+K", AppCommand.Gesture(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.K), "Ctrl+K");
            Eq("Ctrl+Shift+P", AppCommand.Gesture(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.P), "Ctrl+Shift+P");
            Eq("Ctrl+3", AppCommand.Gesture(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.D3), "Ctrl+3");
            Eq("F5", AppCommand.Gesture(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.F5), "F5 без модификаторов");
            Eq(null, AppCommand.Gesture(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.K), "буква без модификаторов не перехватывается (это ввод текста)");
            Eq("Ctrl+,", AppCommand.Gesture(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.OemComma), "Ctrl+,");
        }

        static PulseSample PS(int ms, bool ok)
        {
            PulseSample s = new PulseSample();
            s.Time = DateTime.Now;
            s.Ms = ms;
            s.Status = ok ? ProbeStatus.Ok : ProbeStatus.Timeout;
            return s;
        }

        static void TestPulse()
        {
            PulseSeries s = new PulseSeries();
            Eq(null, s.Delta(), "без измерений изменения нет");
            s.Add(PS(100, true), 3);
            Eq(null, s.Delta(), "одно измерение — сравнивать не с чем");
            s.Add(PS(130, true), 3);
            Eq(30, s.Delta(), "изменение +30 мс");
            s.Add(PS(-1, false), 3);
            Eq(null, s.Delta(), "неудачное измерение не сравнивается");
            Eq(1, s.Fails, "счётчик неудач подряд");
            s.Add(PS(-1, false), 3);
            Eq(2, s.Fails, "две неудачи подряд");
            Eq(3, s.Samples.Count, "история ограничена лимитом");
            int min, avg, max, ok;
            s.Stats(out min, out avg, out max, out ok);
            Eq(1, ok, "в окне одно успешное измерение");
            Eq(130, avg, "среднее только по успешным");
            s.Add(PS(90, true), 3);
            Eq(0, s.Fails, "успех сбрасывает счётчик неудач");
            s.Trim(2);
            Eq(2, s.Samples.Count, "уменьшение истории");

            Eq(30, PulseMonitor.ClampInterval(5), "интервал не меньше 30 с");
            Eq(120, PulseMonitor.ClampInterval(120), "допустимый интервал без изменений");
            Eq(null, PulseMonitor.ServiceOf("google"), "у Google нет отдельной проверки сервиса");
            Eq("Discord", PulseMonitor.ServiceOf("discord"), "Discord сопоставлен с проверкой сервиса");

            ProbeResult pr = new ProbeResult();
            pr.Target = PulseMonitor.TargetOf("discord");
            pr.Status = ProbeStatus.Ok; pr.TcpMs = 20; pr.TlsMs = 70; pr.HttpMs = 50;
            Eq(90, PulseMonitor.FromProbe(pr).Ms, "задержка = TCP + TLS (HTTP не входит)");
            ProbeResult bad = new ProbeResult();
            bad.Target = pr.Target;
            bad.Status = ProbeStatus.Timeout; bad.TcpMs = 20; bad.Stage = "TLS";
            Eq(-1, PulseMonitor.FromProbe(bad).Ms, "без TLS задержка не выдумывается");

            PulseMonitor m = new PulseMonitor();
            m.SetEnabled("discord,unknown,discord,youtube");
            Eq("discord,youtube", m.EnabledCsv(), "неизвестные и повторные адреса отбрасываются");
        }

        static void TestEvents()
        {
            EventStore es = new EventStore();
            es.Add("error", "err", "Сбой X", "token=abc123", "journal");
            es.Add("error", "err", "Сбой X", "повтор", "journal");
            es.Add("check", "ok", "Проверка", "", "diag");
            List<AppEvent> all = es.Snapshot();
            Eq(2, all.Count, "повтор в течение минуты объединён");
            Eq(2, all[0].Count, "счётчик повторов");
            Check(!es.Snapshot()[0].Detail.Contains("abc123") , "секреты не попадают в события");
            Check(EventStore.Important(all[0]), "ошибка — важное событие");
            Check(!EventStore.Important(all[1]), "проверка — не важное");

            SessionStats st = new SessionStats();
            Eq(TimeSpan.Zero, st.EngineUptime, "обход не запускался");
            st.EngineChanged(true);
            System.Threading.Thread.Sleep(120);
            st.EngineChanged(true);   // повторный вызов не сбрасывает отсчёт
            Check(st.EngineUptime.TotalMilliseconds >= 100, "время работы обхода идёт");
            st.EngineChanged(false);
            TimeSpan frozen = st.EngineUptime;
            System.Threading.Thread.Sleep(60);
            Eq(frozen, st.EngineUptime, "после остановки время не растёт");
            Eq(1, st.EngineStarts, "один запуск");
            Check(DateTime.Now - st.AppStart >= st.EngineUptime, "время приложения ≥ времени обхода");
            ServiceResult a = new ServiceResult(); a.Status = ServiceStatus.Ok;
            ServiceResult b = new ServiceResult(); b.Status = ServiceStatus.Unavailable;
            st.CountServices(new ServiceResult[] { a, b });
            Eq(2, st.ServiceChecks, "две проверки");
            Eq(1, st.ServiceChecksOk, "одна успешная");
        }

        static Strategy S(string name, int score, int max, int latency)
        {
            Strategy s = new Strategy();
            s.Display = name;
            s.LastScore = score; s.LastMax = max; s.LastLatency = latency;
            s.LastSummary = name + " " + score + "/" + max;
            s.LastTested = DateTime.Now;
            if (score >= 0) s.LastResults = new ServiceResult[0];
            return s;
        }

        static void TestLab()
        {
            string why;
            List<Strategy> none = new List<Strategy> { S("A", -1, 0, -1), S("B", -1, 0, -1) };
            Check(StrategyLab.Recommend(none, "", null, DateTime.Now, out why) == null, "без измерений рекомендации нет");
            Check(why.Contains("нет измерений"), "объяснение: нет измерений");

            List<Strategy> l = new List<Strategy> { S("A", 8, 12, 150), S("B", 12, 12, 300), S("C", 12, 12, 200), S("D", -1, 0, -1) };
            Eq("C", StrategyLab.Recommend(l, "", null, DateTime.Now, out why).Display, "лучший результат, при равенстве — меньшая задержка");
            Check(why.Contains("Ответили все адреса"), "объяснение полного успеха");
            Check(why.Contains("B"), "объяснение упоминает альтернативу с большей задержкой");

            List<Strategy> tie = new List<Strategy> { S("A", 12, 12, 200), S("B", 12, 12, 205) };
            Eq("B", StrategyLab.Recommend(tie, "B", null, DateTime.Now, out why).Display, "разница < 15 мс — выбирается предпочтительная");
            Eq("A", StrategyLab.Recommend(tie, "", tie[0], DateTime.Now, out why).Display, "при равенстве остаётся текущая");

            List<Strategy> zero = new List<Strategy> { S("A", 0, 12, -1) };
            Check(StrategyLab.Recommend(zero, "", null, DateTime.Now, out why) == null, "все провалились — не рекомендуем");

            Strategy old = S("A", 12, 12, 100);
            old.LastTested = DateTime.Now.AddHours(-2);
            StrategyLab.Recommend(new List<Strategy> { old }, "", null, DateTime.Now, out why);
            Check(why.Contains("больше 30 минут"), "устаревший результат помечается");
        }

        static void TestStudio()
        {
            ThemeOptions d = new ThemeOptions();
            Check(d.IsDefault, "настройки по умолчанию");
            Eq(1.0, d.CornerScale, "стандартные углы");
            Check(ThemeOptions.ValidAccent("#3A7BFF") && !ThemeOptions.ValidAccent("3A7BFF") && !ThemeOptions.ValidAccent("#3A7BF") && !ThemeOptions.ValidAccent("red"), "проверка формата цвета");

            // сохранение между запусками
            Settings s = Settings.Load();
            s.Studio.Accent = "#C2185B"; s.Studio.Glow = 150; s.Studio.Corners = "round"; s.Studio.Density = "compact"; s.Studio.Speed = "fast";
            s.CompanionVisible = false; s.CompanionAnimated = false;
            Eq(null, s.Save(), "сохранение");
            Settings s2 = Settings.Load();
            Eq("#C2185B", s2.Studio.Accent, "акцент сохранился");
            Eq(150, s2.Studio.Glow, "свечение сохранилось");
            Eq("round", s2.Studio.Corners, "углы сохранились");
            Eq("compact", s2.Studio.Density, "плотность сохранилась");
            Eq("fast", s2.Studio.Speed, "скорость сохранилась");
            Eq(false, s2.CompanionVisible, "видимость пчёлки сохранилась");
            Eq(false, s2.CompanionAnimated, "анимация пчёлки сохранилась");
            File.WriteAllText(Settings.FilePath, "StudioAccent=javascript:alert(1)\r\nStudioGlow=9999\r\nStudioCorners=../../x\r\nStudioSpeed=warp\r\n");
            Settings s3 = Settings.Load();
            Check(s3.Studio.IsDefault, "мусор в settings.ini заменяется значениями по умолчанию");

            // экспорт → импорт
            string json = s2.Studio.ToJson("night");
            string bt, err;
            int ign;
            ThemeOptions back = ThemeOptions.FromJson(json, out bt, out ign, out err);
            Check(back != null, "экспортированная тема читается: " + err);
            Eq("night", bt, "базовая тема");
            Eq("#C2185B", back.Accent, "акцент после импорта");
            Eq(150, back.Glow, "свечение после импорта");
            Eq(0, ign, "лишних полей нет");

            Check(ThemeOptions.FromJson("{\"format\":\"other\",\"version\":1}", out bt, out ign, out err) == null, "чужой формат отклонён");
            Check(ThemeOptions.FromJson("{\"format\":\"veylix-theme\",\"version\":2}", out bt, out ign, out err) == null, "неизвестная версия отклонена");
            Check(ThemeOptions.FromJson("{\"format\":\"veylix-theme\",\"version\":1,\"accent\":\"url(http://x)\"}", out bt, out ign, out err) == null, "недопустимый акцент отклонён");
            Check(ThemeOptions.FromJson("{\"format\":\"veylix-theme\",\"version\":1,\"glow\":77}", out bt, out ign, out err) == null, "недопустимое свечение отклонено");
            Check(ThemeOptions.FromJson("{\"format\":\"veylix-theme\",\"version\":1,\"baseTheme\":\"..\\\\evil\"}", out bt, out ign, out err) == null, "недопустимая тема отклонена");
            Check(ThemeOptions.FromJson("не json", out bt, out ign, out err) == null, "не-JSON отклонён");
            Check(ThemeOptions.FromJson(new string(' ', 20000), out bt, out ign, out err) == null, "слишком большой файл отклонён");
            ThemeOptions withExtra = ThemeOptions.FromJson("{\"format\":\"veylix-theme\",\"version\":1,\"script\":\"x\",\"density\":\"spacious\"}", out bt, out ign, out err);
            Check(withExtra != null && withExtra.Density == "spacious", "известные поля применяются");
            Eq(1, ign, "неизвестное поле пропущено и посчитано");

            // читаемость акцента
            Color white = Colors.White;
            Color on = ThemeOptions.OnAccentFor(Theme.C("#FFE066"));
            Check(ThemeOptions.Contrast(on, Theme.C("#FFE066")) >= 4.5, "текст на светлом акценте — тёмный и читаемый");
            Check(ThemeOptions.Contrast(ThemeOptions.OnAccentFor(Theme.C("#2D63D2")), Theme.C("#2D63D2")) >= 4.5, "текст на синем акценте читаем");
            bool adj;
            Color fixedA = ThemeOptions.Readable(Theme.C("#FFF59D"), white, false, out adj);
            Check(adj && ThemeOptions.Contrast(fixedA, white) >= 3.0, "бледный акцент на светлой теме затемняется до 3:1");
            Color keep = ThemeOptions.Readable(Theme.C("#2D63D2"), white, false, out adj);
            Check(!adj && keep == Theme.C("#2D63D2"), "контрастный акцент не меняется");
            Color darkFix = ThemeOptions.Readable(Theme.C("#1A1A40"), Theme.C("#22262B"), true, out adj);
            Check(adj && ThemeOptions.Contrast(darkFix, Theme.C("#22262B")) >= 3.0, "тёмный акцент на тёмной теме осветляется");
            foreach (string p in ThemeOptions.AccentPresets)
                Check(ThemeOptions.Contrast(ThemeOptions.OnAccentFor(Theme.C(p)), Theme.C(p)) >= 4.5, "пресет " + p + ": подпись на кнопке читаема");
        }

        static void TestCompanion()
        {
            Eq("idle", CompanionLogic.Resolve(false, false, false, false), "ничего не происходит — отдыхает");
            Eq("work", CompanionLogic.Resolve(true, false, false, false), "обход работает");
            Eq("diag", CompanionLogic.Resolve(true, true, false, false), "идёт проверка");
            Eq("error", CompanionLogic.Resolve(true, true, true, false), "ошибка важнее проверки");
            Eq("restored", CompanionLogic.Resolve(false, false, true, true), "восстановление связи важнее всего");
            Eq("happy", CompanionLogic.Mood("work"), "работа — довольная пчёлка");
            Eq("error", CompanionLogic.Mood("error"), "ошибка — встревоженная");
            Eq("idle", CompanionLogic.Mood("неизвестно"), "неизвестное состояние — ожидание");
        }

        static ProbeResult PR(string host, bool main, ProbeStatus st, string stage, string detail)
        {
            ProbeResult p = P(host, main, st, stage);
            p.Detail = detail;
            return p;
        }

        static ServiceResult SR(string name, params ProbeResult[] probes)
        {
            return NetCheck.Verdict(name, probes, true);
        }

        static Finding Find(List<Finding> l, string id)
        {
            foreach (Finding f in l) if (f.Id == id) return f;
            return null;
        }

        static void TestAssistant()
        {
            AssistantInput i = new AssistantInput();
            List<Finding> r = Assistant.Analyze(i);
            Finding nd = Find(r, "nodata");
            Check(nd != null, "без данных — честное «недостаточно данных»");
            Check(nd != null && nd.Steps.Count > 0 && nd.Steps[0].Action == "diag.run", "предложена диагностика");

            i = new AssistantInput();
            i.Admin = false;
            r = Assistant.Analyze(i);
            Finding adm = Find(r, "admin");
            Check(adm != null && adm.Level == "err" && adm.Confidence == Assistant.Confirmed, "нет прав — подтверждённая ошибка");
            Eq("admin", r[0].Id, "ошибки идут первыми");

            // нет интернета: о сервисах не делаем выводов
            i = new AssistantInput();
            i.ChecksTime = DateTime.Now;
            i.Checks = new ServiceResult[] {
                SR("Интернет", PR("ya.ru", true, ProbeStatus.Timeout, "TCP", "нет ответа")),
                NetCheck.Verdict("Discord", new ProbeResult[] { PR("discord.com", true, ProbeStatus.Timeout, "TCP", "x") }, false) };
            r = Assistant.Analyze(i);
            Check(Find(r, "internet") != null, "нет интернета — найдено");
            Check(Find(r, "svc-Discord") == null, "без интернета не утверждаем блокировку Discord");

            // TLS-зависание: разные советы при включённом и выключенном обходе
            ServiceResult tlsDown = SR("Discord", PR("discord.com", true, ProbeStatus.Timeout, "TLS", "TLS-рукопожатие зависло"), PR("gateway.discord.gg", false, ProbeStatus.Timeout, "TLS", "зависло"));
            i = new AssistantInput();
            i.ChecksTime = DateTime.Now;
            i.Checks = new ServiceResult[] { SR("Интернет", PR("ya.ru", true, ProbeStatus.Ok, "HTTP", "ok")), tlsDown };
            i.ChecksWithEngine = false;
            Finding off = Find(Assistant.Analyze(i), "svc-Discord");
            Check(off != null && off.Steps[0].Action == "engine.toggle" && off.Steps[0].ChangesState, "обход выключен — предложить включить (с подтверждением)");
            Check(off != null && off.Guess != null && off.Confidence == Assistant.Likely, "причина — предположение, «вероятно»");
            i.ChecksWithEngine = true;
            i.EngineRunning = true;
            Finding on = Find(Assistant.Analyze(i), "svc-Discord");
            Check(on != null && on.Steps[0].Action == "tune.toggle", "обход включён — предложить автоподбор");
            Check(on != null && on.Anchors.Contains("svc:Discord"), "привязка к строке Discord («Как исправить?»)");

            // DNS-подмена
            i.Checks = new ServiceResult[] { i.Checks[0], SR("YouTube", PR("www.youtube.com", true, ProbeStatus.Blocked, "DNS", "домен не найден (NXDOMAIN)")) };
            Finding dns = Find(Assistant.Analyze(i), "svc-YouTube");
            Check(dns != null && dns.Title.Contains("DNS"), "DNS-блокировка распознана");

            // подмена сертификата: никогда не советуем отключать проверку
            i.Checks = new ServiceResult[] { i.Checks[0], SR("YouTube", PR("www.youtube.com", true, ProbeStatus.Blocked, "TLS", "сертификат не прошёл проверку (RemoteCertificateNameMismatch)")) };
            Finding cert = Find(Assistant.Analyze(i), "svc-YouTube");
            Check(cert != null && cert.Steps[0].Text.StartsWith("Не отключайте проверку сертификатов"), "подмена сертификата: совет НЕ отключать проверку");

            // всё хорошо
            i = new AssistantInput();
            i.ChecksTime = DateTime.Now;
            i.EngineRunning = true; i.Strategy = "ALT3";
            ProbeResult ok1 = PR("discord.com", true, ProbeStatus.Ok, "HTTP", "ok");
            i.Checks = new ServiceResult[] { SR("Интернет", PR("ya.ru", true, ProbeStatus.Ok, "HTTP", "ok")), SR("Discord", ok1), SR("YouTube", PR("www.youtube.com", true, ProbeStatus.Ok, "HTTP", "ok")) };
            r = Assistant.Analyze(i);
            Check(Find(r, "ok") != null && r.Count == 1, "всё работает — одна запись «всё в порядке»");

            // устаревшие данные
            i.ChecksTime = DateTime.Now.AddHours(-1);
            Check(Find(Assistant.Analyze(i), "stale") != null, "проверка старше 30 минут — предложить повторить");

            // аварийное завершение
            i = new AssistantInput();
            i.LastExit = DateTime.Now.AddMinutes(-2);
            i.ExitCode = 1;
            Finding ex = Find(Assistant.Analyze(i), "exit");
            Check(ex != null && ex.Facts[0].Contains("кодом 1"), "завершение с кодом — факт");

            // диагностика: конфликт с защитным ПО — не советуем отключать защиту
            i = new AssistantInput();
            i.DiagTime = DateTime.Now;
            i.Diag = new List<DiagItem> { new DiagItem("Службы", "warn", "Конфликтующее ПО", "Check Point (EPWD)", ""), new DiagItem("Сеть", "warn", "VPN-адаптеры", "WireGuard", "") };
            r = Assistant.Analyze(i);
            Finding cf = Find(r, "conflicts");
            bool cpNote = false;
            foreach (AssistStep st in cf.Steps) if (st.Text.Contains("Не отключайте его самостоятельно")) cpNote = true;
            Check(cpNote, "Check Point: совет не отключать защиту, а обратиться к администратору");
            Check(Find(r, "vpn") != null && Find(r, "vpn").Confidence == Assistant.Possible, "VPN — «возможно», не факт проблемы");

            // ни одно правило не предлагает опасного
            List<Finding> all = new List<Finding>();
            foreach (Finding f in Assistant.Analyze(new AssistantInput { Admin = false, LastExit = DateTime.Now, Diag = i.Diag, DiagTime = DateTime.Now })) all.Add(f);
            string[] bad = { "отключите антивирус", "отключите защитник", "отключите брандмауэр", "отключите проверку сертификатов", "format ", "del /", "rm -rf", "reg delete", "bcdedit", "Set-MpPreference" };
            foreach (Finding f in all)
                foreach (AssistStep st in f.Steps)
                    foreach (string b in bad)
                        Check(st.Text.IndexOf(b, StringComparison.OrdinalIgnoreCase) < 0, f.Id + ": нет опасного совета «" + b + "»");
            foreach (Finding f in all)
                foreach (AssistStep st in f.Steps)
                    if (st.Action == "engine.toggle" || st.Action == "tune.toggle" || st.Action == "ipset.update")
                        Check(st.ChangesState, f.Id + ": действие «" + st.Action + "» требует подтверждения");
        }

        // Синтетический релиз в формате GitHub API
        static string Rel(string tag, bool draft, bool pre, string assets)
        {
            return "{\"tag_name\":\"" + tag + "\",\"name\":\"Veylix " + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (pre ? "true" : "false") +
                   ",\"html_url\":\"https://github.com/FreeSize83/Veylix/releases/tag/" + tag + "\",\"published_at\":\"2026-10-15T10:00:00Z\",\"body\":\"* изменения\",\"assets\":[" + assets + "]}";
        }

        static string Asset(string name, long size, string digest)
        {
            return "{\"name\":\"" + name + "\",\"size\":" + size + ",\"digest\":" + (digest == null ? "null" : "\"" + digest + "\"") +
                   ",\"browser_download_url\":\"https://github.com/FreeSize83/Veylix/releases/download/x/" + name + "\"}";
        }

        static void TestAppReleases()
        {
            // единый источник версии
            Version av = typeof(Program).Assembly.GetName().Version;
            Eq(Program.Version, av.Major + "." + av.Minor + "." + av.Build, "версия сборки = Program.Version");
            Check(AppUpdate.ParseVersion(Program.Version) != null, "Program.Version в формате X.Y.Z");

            Eq("2.2.0", AppUpdate.ParseStableTag("v2.2.0"), "тег v2.2.0");
            Eq("2.10.1", AppUpdate.ParseStableTag("2.10.1"), "тег без v");
            Eq(null, AppUpdate.ParseStableTag("Releases"), "тег «Releases» — не версия");
            Eq(null, AppUpdate.ParseStableTag("v2.3.0-beta"), "бета-тег — не стабильный");
            Eq(null, AppUpdate.ParseStableTag("v2.3"), "неполная версия");
            Check(AppUpdate.Compare("2.10.0", "2.9.9") > 0, "2.10.0 новее 2.9.9 (не строковое сравнение)");
            Eq(0, AppUpdate.Compare("2.2.0", "2.2.0"), "равные версии");
            Check(AppUpdate.Compare("2.1.9", "2.2.0") < 0, "2.1.9 старее 2.2.0");

            string why;
            // реальный ответ API для FreeSize83/Veylix на момент разработки: тег «Releases» и исходники
            AppRelease cur = AppUpdate.PickLatest(Fixture("veylix-releases-current.json"), out why);
            Check(cur == null && why.Contains("нет стабильного релиза"), "текущий репозиторий: честно «нет релиза с версией» — " + why);

            Check(AppUpdate.PickLatest("[]", out why) == null && why.Contains("нет опубликованных релизов"), "релизов нет");

            string sha = new string('a', 64);
            string good = Rel("v9.1.0", false, false, Asset("Veylix-9.1.0-win-x64.zip", 1700000, "sha256:" + sha));
            string list = "[" + Rel("v9.5.0", true, false, Asset("Veylix-9.5.0-win-x64.zip", 1, "sha256:" + sha)) + "," +
                          Rel("v9.4.0", false, true, Asset("Veylix-9.4.0-win-x64.zip", 1, "sha256:" + sha)) + "," +
                          Rel("Releases", false, false, Asset("BeeBot-source.zip", 1, "sha256:" + sha)) + "," +
                          good + "," + Rel("v9.0.5", false, false, "") + "]";
            AppRelease r = AppUpdate.PickLatest(list, out why);
            Check(r != null && r.Version == "9.1.0", "черновик и предварительный пропущены, выбран стабильный 9.1.0");
            Check(r.CanInstall && r.Sha256 == sha && r.AssetSize == 1700000, "пакет и SHA-256 из digest");
            Check(r.Published.Year == 2026 && r.Body.Contains("изменения"), "дата и описание релиза");

            r = AppUpdate.PickLatest("[" + Rel("v9.2.0", false, false, Asset("Veylix-src.zip", 5, "sha256:" + sha)) + "]", out why);
            Check(r != null && !r.CanInstall && r.Problem.Contains("нет готового пакета"), "релиз без пакета — понятная причина");
            r = AppUpdate.PickLatest("[" + Rel("v9.2.0", false, false, Asset("Veylix-9.2.0-win-x64.zip", 5, null)) + "]", out why);
            Check(r != null && !r.CanInstall && r.Problem.Contains("контрольной суммы"), "пакет без SHA-256 не устанавливается");
            r = AppUpdate.PickLatest("[" + Rel("v9.2.0", false, false, Asset("Veylix-9.2.0-win-x64.zip", 5, null) + "," + Asset("Veylix-9.2.0-win-x64.zip.sha256", 90, null)) + "]", out why);
            Check(r != null && r.CanInstall && r.Sha256 == null && r.ShaFileUrl != null, "SHA-256 из отдельного файла .sha256");
            r = AppUpdate.PickLatest("[" + Rel("v9.2.0", false, false, Asset("Veylix-9.1.0-win-x64.zip", 5, "sha256:" + sha)) + "]", out why);
            Check(r != null && !r.CanInstall, "пакет с другой версией в имени не подходит");
            string evil = Rel("v9.2.0", false, false, "{\"name\":\"Veylix-9.2.0-win-x64.zip\",\"size\":5,\"digest\":\"sha256:" + sha + "\",\"browser_download_url\":\"https://evil.example/Veylix-9.2.0-win-x64.zip\"}");
            r = AppUpdate.PickLatest("[" + evil + "]", out why);
            Check(r != null && !r.CanInstall, "пакет с чужого адреса отклонён");

            Eq(sha, AppUpdate.ParseShaFile(sha + " *Veylix-9.2.0-win-x64.zip\n", "Veylix-9.2.0-win-x64.zip"), "формат sha256sum");
            Eq(sha, AppUpdate.ParseShaFile(sha.ToUpperInvariant(), "x.zip"), "голый хеш");
            Eq(null, AppUpdate.ParseShaFile(sha + " *other.zip", "Veylix-9.2.0-win-x64.zip"), "хеш другого файла не принимается");
            Eq(null, AppUpdate.ParseShaFile("not a hash", "x.zip"), "мусор не принимается");
            Check(AppUpdate.IsTrustedAssetUrl("https://github.com/FreeSize83/Veylix/releases/download/v2.2.0/Veylix-2.2.0-win-x64.zip"), "официальный адрес");
            Check(!AppUpdate.IsTrustedAssetUrl("https://github.com/Other/Veylix/releases/download/v2.2.0/a.zip"), "чужой репозиторий");
            Check(!AppUpdate.IsTrustedAssetUrl("https://github.com/FreeSize83/Veylix/archive/refs/tags/v2.2.0.zip"), "архив исходников по тегу — не пакет");
        }

        static void TestAppPackage()
        {
            Directory.CreateDirectory(AppUpdate.UpdatesDir);
            string exe, exeSha;
            // пакет без Veylix.exe (как исходники)
            Dictionary<string, string> src = new Dictionary<string, string>();
            src["BeeBot/src/Program.cs"] = "class P {}";
            string z1 = MakeZip("src.zip", src);
            string err = AppUpdate.Stage(z1, Updater.Sha256File(z1), new FileInfo(z1).Length, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("нет Veylix.exe"), "исходники — не пакет: " + err);
            // повреждённый пакет
            err = AppUpdate.Stage(z1, new string('0', 64), 0, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("контрольная сумма"), "неверный SHA-256: " + err);
            // недокачанный (размер меньше ожидаемого)
            err = AppUpdate.Stage(z1, Updater.Sha256File(z1), new FileInfo(z1).Length + 1000, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("размер"), "неполный файл: " + err);
            // Veylix.exe, который не является приложением .NET
            Dictionary<string, string> fake = new Dictionary<string, string>();
            fake["Veylix.exe"] = "MZ not really";
            string z2 = MakeZip("fake.zip", fake);
            err = AppUpdate.Stage(z2, Updater.Sha256File(z2), 0, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("не является приложением .NET"), "поддельный exe: " + err);
            // настоящая сборка .NET, но не Veylix (сам тестовый исполняемый файл)
            string other = Path.Combine(tmp, "otherpkg");
            Directory.CreateDirectory(other);
            File.Copy(typeof(TestProgram).Assembly.Location, Path.Combine(other, "Veylix.exe"), true);
            string z3 = Path.Combine(tmp, "other.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(other, z3);
            err = AppUpdate.Stage(z3, Updater.Sha256File(z3), 0, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("другое приложение"), "чужая сборка: " + err);
            // архив с выходом за пределы папки
            Dictionary<string, string> slip = new Dictionary<string, string>();
            slip["../../evil.exe"] = "x";
            string z4 = MakeZip("slip.zip", slip);
            err = AppUpdate.Stage(z4, Updater.Sha256File(z4), 0, "9.0.1", out exe, out exeSha);
            Check(err != null && err.Contains("Небезопасный путь"), "zip-slip: " + err);
            Check(AppUpdate.Stage(z1, null, 0, "9.0.1", out exe, out exeSha).Contains("нет доверенной"), "без контрольной суммы не ставим");

            // план: только пути и версии, каждое поле проверяется
            UpdatePlan p = new UpdatePlan();
            p.TargetExe = @"C:\Apps\Veylix\Veylix.exe";
            p.NewExe = Path.Combine(AppUpdate.UpdatesDir, "app-9.0.1", "Veylix.exe");
            p.BackupExe = @"C:\Apps\Veylix\Veylix.exe.backup-2.2.0";
            p.OldVersion = "2.2.0"; p.NewVersion = "9.0.1"; p.NewSha256 = new string('b', 64);
            Eq(null, p.Validate(), "корректный план");
            string pp = Path.Combine(tmp, "plan.ini");
            p.Save(pp);
            string perr;
            UpdatePlan back = UpdatePlan.Load(pp, out perr);
            Check(back != null && back.NewVersion == "9.0.1" && back.TargetExe == p.TargetExe, "план сохраняется и читается");
            p.NewExe = @"C:\Windows\Temp\Veylix.exe";
            Check(p.Validate() != null, "новая версия вне папки обновлений отклонена");
            p.NewExe = Path.Combine(AppUpdate.UpdatesDir, "app-9.0.1", "Veylix.exe");
            p.BackupExe = @"C:\Other\Veylix.exe.bak";
            Check(p.Validate() != null, "резервная копия не рядом с exe отклонена");
            p.BackupExe = @"C:\Apps\Veylix\Veylix.exe.backup-2.2.0";
            p.TargetExe = @"C:\Apps\Veylix\cmd.bat";
            Check(p.Validate() != null, "целевой файл не .exe отклонён");
            p.TargetExe = @"C:\Apps\Veylix\Veylix.exe";
            p.NewSha256 = "zzz";
            Check(p.Validate() != null, "план без контрольной суммы отклонён");
            File.WriteAllText(pp, "TargetExe=C:\\x\\Veylix.exe\r\nNewExe=C:\\Windows\\System32\\Veylix.exe\r\n");
            Check(UpdatePlan.Load(pp, out perr) == null, "подменённый план не принимается: " + perr);
            Eq(5, AppUpdate.ApplyUpdate(pp), "помощник отказывается выполнять неверный план");
            Check(File.Exists(AppUpdate.ResultFile), "о неудаче записан результат");
            string msg = AppUpdate.TakeFailure();
            Check(msg != null && msg.Contains("Обновление не выполнено"), "результат читается один раз: " + msg);
            Eq(null, AppUpdate.TakeFailure(), "второй раз — пусто");
        }

        static void TestDiagnostics()
        {
            Engine e = new Engine(Engine.Locate());
            List<DiagItem> items = Diagnostics.Collect(e, 22);
            Check(items.Count >= 10, "собрано пунктов: " + items.Count);
            bool admin = false, bfe = false;
            foreach (DiagItem d in items)
            {
                if (d.Name == "Права администратора") admin = true;
                if (d.Name == "Base Filtering Engine") bfe = true;
            }
            Check(admin, "есть пункт о правах");
            Check(bfe, "есть пункт о службе BFE");
            string rep = Diagnostics.Report(items, null);
            if (Environment.UserName.Length >= 3) Check(rep.IndexOf(Environment.UserName, StringComparison.OrdinalIgnoreCase) < 0, "в отчёте нет имени пользователя");
            foreach (DiagItem d in items) Console.WriteLine("    [" + d.Level + "] " + d.Name + ": " + LogStore.Sanitize(d.Value));
        }

        static void TestNetwork()
        {
            ServiceResult[] all = NetCheck.CheckAll().Result;
            foreach (ServiceResult s in all)
            {
                Console.WriteLine("    " + s.Name + ": " + NetCheck.StatusWord(s.Status) + " — " + s.Summary);
                foreach (ProbeResult p in s.Probes)
                    Console.WriteLine("      " + (p.Passed ? "✓" : "✕") + " " + p.Target.Host + "  " + p.Timing() + (p.Passed ? "" : "  [" + p.Stage + "] " + p.Detail));
            }
            Check(all[0].Status == ServiceStatus.Ok || all[0].Status == ServiceStatus.Partial, "базовый интернет доступен");

            ProbeResult nx = NetCheck.Probe(new Target("does-not-exist.invalid", "/", true)).Result;
            Check(!nx.Passed && nx.Stage == "DNS", "несуществующий домен — ошибка на этапе DNS: " + nx.Detail);

            // проверка сертификатов не отключена: заведомо неверные сертификаты отклоняются
            ProbeResult exp = NetCheck.Probe(new Target("expired.badssl.com", "/", true)).Result;
            Check(!exp.Passed && exp.Stage == "TLS", "просроченный сертификат отклонён: [" + exp.Stage + "] " + exp.Detail);
            ProbeResult wh = NetCheck.Probe(new Target("wrong.host.badssl.com", "/", true)).Result;
            Check(!wh.Passed && wh.Stage == "TLS", "сертификат чужого домена отклонён: [" + wh.Stage + "] " + wh.Detail);

            ReleaseInfo r = Updater.FetchLatest();
            Console.WriteLine("    Последний релиз Flowseal: " + r.Version + " · zip: " + (r.ZipName ?? "нет") + " · sha256: " + (r.ZipSha256 ?? "нет"));
            Check(r.Version.Length > 0, "получена информация о релизе");

            // настоящая установка официального архива во временную папку (скачивание + SHA-256 + распаковка)
            string root = Path.Combine(tmp, "real", "zapret");
            Directory.CreateDirectory(Path.Combine(root, "lists"));
            File.WriteAllText(Path.Combine(root, "lists", "list-general-user.txt"), "keep.example");
            List<string> steps = new List<string>();
            string err = Updater.Install(r, root, delegate(string m) { steps.Add(m); }, delegate { });
            Console.WriteLine("    Шаги: " + string.Join(" | ", steps));
            Eq(null, err, "официальный релиз установлен");
            Check(File.Exists(Path.Combine(root, "bin", "winws.exe")), "winws.exe на месте");
            Eq("keep.example", File.ReadAllText(Path.Combine(root, "lists", "list-general-user.txt")), "список пользователя перенесён");
            Eq(r.Version, new Engine(root).InstalledVersion(), "service.bat новой версии");
            Check(new Engine(root).LoadStrategies().Count > 0, "стратегии новой версии читаются");

            // ---- обновление Veylix: реальный репозиторий FreeSize83/Veylix ----
            string why;
            AppRelease vr = AppUpdate.FetchLatest(out why);
            Console.WriteLine("    Veylix: " + (vr != null ? "релиз " + vr.Version + (vr.CanInstall ? " — можно установить" : " — " + vr.Problem) : why));
            Check(vr != null || why != null, "ответ о релизах Veylix получен и понятен");

            // настоящий файл из текущего релиза: скачивание, сверка с digest GitHub и отказ «это не пакет»
            string json = Http.GetString(AppUpdate.ApiReleases, 15000, "application/vnd.github+json");
            System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
            object[] rels = (object[])js.DeserializeObject(json);
            AppRelease src = null;
            foreach (object o in rels)
                foreach (object ao in (object[])((Dictionary<string, object>)o)["assets"])
                {
                    Dictionary<string, object> a = (Dictionary<string, object>)ao;
                    string dg = Convert.ToString(a["digest"]);
                    if (src != null || dg == null || !dg.StartsWith("sha256:")) continue;
                    src = new AppRelease();
                    src.Version = "9.9.9";
                    src.AssetName = Convert.ToString(a["name"]);
                    src.AssetUrl = Convert.ToString(a["browser_download_url"]);
                    src.AssetSize = Convert.ToInt64(a["size"]);
                    src.Sha256 = dg.Substring(7);
                }
            if (src != null)
            {
                long seen = 0;
                string derr = AppUpdate.Download(src, delegate(long d, long t) { seen = d; }, System.Threading.CancellationToken.None);
                Eq(null, derr, "файл релиза скачан (" + src.AssetName + ")");
                Eq(src.AssetSize, seen, "прогресс дошёл до полного размера");
                string sexe, ssha;
                string serr = AppUpdate.Stage(AppUpdate.PackagePath("9.9.9"), src.Sha256, src.AssetSize, "9.9.9", out sexe, out ssha);
                Check(serr != null && serr.Contains("нет Veylix.exe"), "SHA-256 совпал с digest, но исходники отклонены как не-пакет: " + serr);

                // прерванная загрузка: недокачанный файл не остаётся
                System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource();
                string cerr = AppUpdate.Download(src, delegate(long d, long t) { if (d > 100000) cts.Cancel(); }, cts.Token);
                Check(cerr != null && cerr.Contains("отменена"), "загрузка прервана: " + cerr);
                Check(!File.Exists(AppUpdate.PackagePath("9.9.9") + ".part"), "недокачанный файл удалён");
            }
        }
    }
}
