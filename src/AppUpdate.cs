using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Veylix
{
    // Релиз Veylix на GitHub, пригодный для обновления
    public class AppRelease
    {
        public string Version, Tag, Name = "", Body = "", HtmlUrl;
        public DateTime Published = DateTime.MinValue;
        public string AssetName, AssetUrl;       // готовый пакет Veylix-<версия>-win-x64.zip
        public long AssetSize;
        public string Sha256;                    // доверенная контрольная сумма (digest из API GitHub)
        public string ShaFileUrl;                // или отдельный файл <пакет>.sha256 в том же релизе
        public string Problem;                   // почему установить нельзя (релиз есть, пакета нет и т.п.)

        public bool CanInstall { get { return AssetUrl != null && (Sha256 != null || ShaFileUrl != null) && Problem == null; } }
    }

    // План обновления: пишет его работающий Veylix, читает помощник (копия текущего Veylix.exe в режиме --apply-update).
    // Только пути и версии — никаких команд. Помощник проверяет каждое поле.
    public class UpdatePlan
    {
        public string TargetExe, NewExe, BackupExe, OldVersion, NewVersion, NewSha256, DataDir = "";
        public int Pid, CloseTimeoutSec = 90, ReadyTimeoutSec = 60;
        public bool RestartEngine, TestFailStart;

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("TargetExe=").Append(TargetExe).Append("\r\n");
            sb.Append("NewExe=").Append(NewExe).Append("\r\n");
            sb.Append("BackupExe=").Append(BackupExe).Append("\r\n");
            sb.Append("OldVersion=").Append(OldVersion).Append("\r\n");
            sb.Append("NewVersion=").Append(NewVersion).Append("\r\n");
            sb.Append("NewSha256=").Append(NewSha256).Append("\r\n");
            sb.Append("DataDir=").Append(DataDir).Append("\r\n");
            sb.Append("Pid=").Append(Pid).Append("\r\n");
            sb.Append("CloseTimeoutSec=").Append(CloseTimeoutSec).Append("\r\n");
            sb.Append("ReadyTimeoutSec=").Append(ReadyTimeoutSec).Append("\r\n");
            sb.Append("RestartEngine=").Append(RestartEngine ? "1" : "0").Append("\r\n");
            if (TestFailStart) sb.Append("TestFailStart=1\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static UpdatePlan Load(string path, out string error)
        {
            error = null;
            UpdatePlan p = new UpdatePlan();
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    int n;
                    switch (k)
                    {
                        case "TargetExe": p.TargetExe = v; break;
                        case "NewExe": p.NewExe = v; break;
                        case "BackupExe": p.BackupExe = v; break;
                        case "OldVersion": p.OldVersion = v; break;
                        case "NewVersion": p.NewVersion = v; break;
                        case "NewSha256": p.NewSha256 = v; break;
                        case "DataDir": p.DataDir = v; break;
                        case "Pid": int.TryParse(v, out p.Pid); break;
                        case "CloseTimeoutSec": if (int.TryParse(v, out n) && n >= 1 && n <= 600) p.CloseTimeoutSec = n; break;
                        case "ReadyTimeoutSec": if (int.TryParse(v, out n) && n >= 5 && n <= 600) p.ReadyTimeoutSec = n; break;
                        case "RestartEngine": p.RestartEngine = v == "1"; break;
                        case "TestFailStart": p.TestFailStart = v == "1"; break;
                    }
                }
            }
            catch (Exception ex) { error = "не удалось прочитать план обновления: " + ex.Message; return null; }
            error = p.Validate();
            return error == null ? p : null;
        }

        public string Validate()
        {
            if (string.IsNullOrEmpty(TargetExe) || !TargetExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(TargetExe))
                return "в плане некорректный путь к Veylix.exe";
            if (string.IsNullOrEmpty(NewExe) || !Path.IsPathRooted(NewExe) || !NewExe.EndsWith("Veylix.exe", StringComparison.OrdinalIgnoreCase))
                return "в плане некорректный путь к новой версии";
            string updates = Path.GetFullPath(AppUpdate.UpdatesDir).TrimEnd('\\') + "\\";
            if (!Path.GetFullPath(NewExe).StartsWith(updates, StringComparison.OrdinalIgnoreCase))
                return "новая версия должна лежать в папке обновлений Veylix";
            if (string.IsNullOrEmpty(BackupExe) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(BackupExe)), Path.GetDirectoryName(Path.GetFullPath(TargetExe)), StringComparison.OrdinalIgnoreCase))
                return "резервная копия должна лежать рядом с Veylix.exe";
            if (AppUpdate.ParseVersion(NewVersion) == null || AppUpdate.ParseVersion(OldVersion) == null) return "в плане некорректные версии";
            if (NewSha256 == null || !Regex.IsMatch(NewSha256, "^[0-9a-f]{64}$")) return "в плане нет контрольной суммы новой версии";
            return null;
        }
    }

    public static class AppUpdate
    {
        public const string Owner = "FreeSize83", Repo = "Veylix";
        public const string RepoUrl = "https://github.com/" + Owner + "/" + Repo;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public const string ApiReleases = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=30";
        const string TrustedPrefix = RepoUrl + "/releases/download/";
        public const long MaxPackageBytes = 60L * 1024 * 1024;

        public static string UpdatesDir { get { return Path.Combine(Settings.Dir, "updates"); } }
        public static string ResultFile { get { return Path.Combine(UpdatesDir, "update-result.ini"); } }
        public static string LogFile { get { return Path.Combine(UpdatesDir, "update.log"); } }
        public static string ReadyFlag(string version) { return Path.Combine(UpdatesDir, "ready-" + version + ".flag"); }

        public static string AssetNameFor(string version)
        {
            return "Veylix-" + version + "-win-x64.zip";
        }

        // Стабильная версия из тега: v2.2.0 или 2.2.0. Всё остальное (Releases, v2.3.0-beta) — не стабильный релиз.
        public static string ParseStableTag(string tag)
        {
            if (tag == null) return null;
            Match m = Regex.Match(tag.Trim(), @"^[vV]?(\d{1,4})\.(\d{1,4})\.(\d{1,5})$");
            return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value + "." + m.Groups[3].Value : null;
        }

        public static int[] ParseVersion(string v)
        {
            if (v == null) return null;
            Match m = Regex.Match(v, @"^(\d{1,4})\.(\d{1,4})\.(\d{1,5})$");
            if (!m.Success) return null;
            return new int[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value) };
        }

        public static int Compare(string a, string b)
        {
            int[] x = ParseVersion(a), y = ParseVersion(b);
            if (x == null || y == null) throw new ArgumentException("версия должна быть в формате X.Y.Z");
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return 0;
        }

        public static bool IsTrustedAssetUrl(string url)
        {
            Uri u;
            if (url == null || url.Contains("..") || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            return u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                   u.AbsoluteUri.StartsWith(TrustedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        // Из списка релизов выбрать самый новый стабильный (не черновик, не предварительный, тег — версия).
        // why — пояснение, если подходящего релиза нет.
        public static AppRelease PickLatest(string json, out string why)
        {
            why = null;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 8 * 1024 * 1024;
            object[] arr = js.DeserializeObject(json) as object[];
            if (arr == null) { why = "GitHub вернул неожиданный ответ"; return null; }
            if (arr.Length == 0) { why = "в репозитории ещё нет опубликованных релизов"; return null; }
            Dictionary<string, object> best = null;
            string bestVer = null;
            int skippedTags = 0, skippedPre = 0;
            foreach (object o in arr)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                if (Str(d, "draft") == "True" || Str(d, "prerelease") == "True") { skippedPre++; continue; }
                string ver = ParseStableTag(Str(d, "tag_name"));
                if (ver == null) { skippedTags++; continue; }
                if (bestVer == null || Compare(ver, bestVer) > 0) { best = d; bestVer = ver; }
            }
            if (best == null)
            {
                why = "нет стабильного релиза с номером версии в теге (ожидается тег вида v" + Program.Version + ")";
                if (skippedTags > 0) why += "; релизов с другими тегами: " + skippedTags;
                if (skippedPre > 0) why += "; черновиков и предварительных: " + skippedPre;
                return null;
            }
            AppRelease r = new AppRelease();
            r.Version = bestVer;
            r.Tag = Str(best, "tag_name");
            r.Name = Str(best, "name");
            r.Body = Str(best, "body");
            r.HtmlUrl = Str(best, "html_url");
            if (!r.HtmlUrl.StartsWith(RepoUrl + "/", StringComparison.OrdinalIgnoreCase)) r.HtmlUrl = ReleasesUrl;
            DateTime pub;
            if (DateTime.TryParse(Str(best, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out pub)) r.Published = pub.ToLocalTime();

            string want = AssetNameFor(bestVer);
            object[] assets = best.ContainsKey("assets") ? best["assets"] as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name"), url = Str(a, "browser_download_url");
                    if (!IsTrustedAssetUrl(url)) continue;
                    if (name.Equals(want, StringComparison.OrdinalIgnoreCase))
                    {
                        r.AssetName = name;
                        r.AssetUrl = url;
                        long sz;
                        long.TryParse(Str(a, "size"), out sz);
                        r.AssetSize = sz;
                        Match m = Regex.Match(Str(a, "digest"), "^sha256:([0-9a-fA-F]{64})$");
                        if (m.Success) r.Sha256 = m.Groups[1].Value.ToLowerInvariant();
                    }
                    else if (name.Equals(want + ".sha256", StringComparison.OrdinalIgnoreCase)) r.ShaFileUrl = url;
                }
            }
            if (r.AssetUrl == null) r.Problem = "в релизе " + r.Tag + " нет готового пакета " + want + " (исходники для обновления не подходят)";
            else if (r.Sha256 == null && r.ShaFileUrl == null) r.Problem = "у пакета " + want + " нет контрольной суммы (digest GitHub или файл .sha256)";
            else if (r.AssetSize > MaxPackageBytes) r.Problem = "пакет больше " + (MaxPackageBytes / 1024 / 1024) + " МБ — это не похоже на Veylix";
            return r;
        }

        public static AppRelease FetchLatest(out string why)
        {
            string json = Http.GetString(ApiReleases, 15000, "application/vnd.github+json");
            return PickLatest(json, out why);
        }

        // Контрольная сумма из файла .sha256 того же релиза (скачивается отдельно от пакета)
        public static string ParseShaFile(string text, string assetName)
        {
            if (text == null || text.Length > 4096) return null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                Match m = Regex.Match(line, @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$");
                if (!m.Success) continue;
                if (m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 && !m.Groups[2].Value.Trim().Equals(assetName, StringComparison.OrdinalIgnoreCase)) continue;
                return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        public static string ResolveSha(AppRelease r)
        {
            if (r.Sha256 != null) return r.Sha256;
            if (r.ShaFileUrl == null) return null;
            return ParseShaFile(Http.GetString(r.ShaFileUrl, 15000, null), r.AssetName);
        }

        public static string PackagePath(string version)
        {
            return Path.Combine(UpdatesDir, AssetNameFor(version));
        }

        // Скачать пакет. Возвращает текст ошибки или null. Недокачанный файл не остаётся.
        public static string Download(AppRelease r, Action<long, long> progress, CancellationToken ct)
        {
            if (!r.CanInstall) return r.Problem ?? "нет пакета для установки";
            if (!IsTrustedAssetUrl(r.AssetUrl)) return "адрес пакета не относится к официальному репозиторию";
            string dest = PackagePath(r.Version), part = dest + ".part";
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                DriveInfo di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(UpdatesDir)));
                if (r.AssetSize > 0 && di.AvailableFreeSpace < r.AssetSize * 4) return "недостаточно места на диске " + di.Name;
                Http.Download(r.AssetUrl, part, 60000, MaxPackageBytes, progress, ct);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(part, dest);
                return null;
            }
            catch (OperationCanceledException) { return "загрузка отменена"; }
            catch (Exception ex) { return "загрузка прервана: " + Http.Describe(ex); }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }

        // Проверить пакет и распаковать во временную папку. stagedExe — путь к новой Veylix.exe.
        public static string Stage(string zipPath, string expectedSha, long expectedSize, string version, out string stagedExe, out string stagedSha)
        {
            stagedExe = null;
            stagedSha = null;
            if (expectedSha == null || !Regex.IsMatch(expectedSha, "^[0-9a-fA-F]{64}$")) return "нет доверенной контрольной суммы — установка отменена";
            string staging = Path.Combine(UpdatesDir, "app-" + version);
            try
            {
                FileInfo fi = new FileInfo(zipPath);
                if (!fi.Exists) return "пакет не найден";
                if (expectedSize > 0 && fi.Length != expectedSize)
                    return "размер пакета не совпадает с данными релиза (" + fi.Length + " вместо " + expectedSize + " байт) — файл повреждён или недокачан";
                string actual = Updater.Sha256File(zipPath);
                if (actual != expectedSha.ToLowerInvariant())
                    return "контрольная сумма не совпала — пакет повреждён или подменён. Ничего не установлено.";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Updater.ExtractSafely(zipPath, staging);
                string exe = Path.Combine(staging, "Veylix.exe");
                if (!File.Exists(exe))
                {
                    string[] subs = Directory.GetDirectories(staging);
                    if (subs.Length == 1 && File.Exists(Path.Combine(subs[0], "Veylix.exe"))) exe = Path.Combine(subs[0], "Veylix.exe");
                }
                if (!File.Exists(exe)) return "в пакете нет Veylix.exe — это не установочный пакет Veylix";
                // совместимость: читаем только метаданные сборки, код при этом не выполняется
                AssemblyName an;
                try { an = AssemblyName.GetAssemblyName(exe); }
                catch (BadImageFormatException) { return "Veylix.exe в пакете не является приложением .NET — пакет повреждён"; }
                if (an.Name != "Veylix") return "в пакете другое приложение («" + an.Name + "»)";
                string got = an.Version.Major + "." + an.Version.Minor + "." + an.Version.Build;
                if (got != version) return "версия в пакете (" + got + ") не совпадает с версией релиза (" + version + ")";
                stagedExe = exe;
                stagedSha = Updater.Sha256File(exe);
                return null;
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                return "пакет не прошёл проверку: " + ex.Message;
            }
        }

        // Можно ли писать в папку с Veylix.exe (проверяется до закрытия приложения)
        public static string CheckWritable(string targetExe)
        {
            string dir = Path.GetDirectoryName(targetExe);
            string probe = Path.Combine(dir, ".veylix-write-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return null;
            }
            catch (Exception ex) { return "нет прав на запись в папку " + dir + ": " + ex.Message; }
        }

        public static UpdatePlan MakePlan(string stagedExe, string stagedSha, string targetExe, string newVersion, bool restartEngine)
        {
            UpdatePlan p = new UpdatePlan();
            p.TargetExe = Path.GetFullPath(targetExe);
            p.NewExe = Path.GetFullPath(stagedExe);
            p.NewSha256 = stagedSha;
            p.OldVersion = Program.Version;
            p.NewVersion = newVersion;
            p.BackupExe = p.TargetExe + ".backup-" + Program.Version;
            p.Pid = Process.GetCurrentProcess().Id;
            p.DataDir = Settings.IsOverridden ? Settings.Dir : "";
            p.RestartEngine = restartEngine;
            return p;
        }

        static string DataArgs(UpdatePlan p)
        {
            return p.DataDir.Length > 0 ? " --data-dir \"" + p.DataDir + "\"" : "";
        }

        // Запустить помощника: копию текущего Veylix.exe (проверенного кода этой версии) в режиме --apply-update
        public static string LaunchHelper(UpdatePlan plan)
        {
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                string planPath = Path.Combine(UpdatesDir, "update-plan.ini");
                plan.Save(planPath);
                string helperDir = Path.Combine(UpdatesDir, "helper");
                Directory.CreateDirectory(helperDir);
                string helper = Path.Combine(helperDir, "Veylix-updater.exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, helper, true);
                if (File.Exists(ResultFile)) File.Delete(ResultFile);
                if (File.Exists(ReadyFlag(plan.NewVersion))) File.Delete(ReadyFlag(plan.NewVersion));
                ProcessStartInfo psi = new ProcessStartInfo(helper, "--apply-update \"" + planPath + "\"" + DataArgs(plan));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = helperDir;
                Process.Start(psi);
                return null;
            }
            catch (Exception ex) { return "не удалось запустить помощника обновления: " + ex.Message; }
        }

        // ===================== помощник (отдельный процесс) =====================

        static void Log(string m)
        {
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + LogStore.Sanitize(m) + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        static void WriteResult(string status, string message, UpdatePlan p)
        {
            try
            {
                File.WriteAllText(ResultFile, "Status=" + status + "\r\nMessage=" + (message ?? "").Replace("\r", " ").Replace("\n", " ") +
                    "\r\nOldVersion=" + (p != null ? p.OldVersion : "") + "\r\nNewVersion=" + (p != null ? p.NewVersion : "") +
                    "\r\nTime=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        static void StartApp(UpdatePlan p, string extraArgs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(p.TargetExe, (extraArgs ?? "") + DataArgs(p));
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(p.TargetExe);
                Process.Start(psi);
            }
            catch (Exception ex) { Log("не удалось запустить " + p.TargetExe + ": " + ex.Message); }
        }

        static bool Retry(Action a, int attempts, out string error)
        {
            error = null;
            for (int i = 0; i < attempts; i++)
            {
                try { a(); return true; }
                catch (Exception ex) { error = ex.Message; Thread.Sleep(700); }
            }
            return false;
        }

        // Код возврата: 0 — обновлено; 2 — Veylix не закрылся; 3 — замена не удалась (старая версия на месте);
        // 4 — новая версия не подтвердила запуск, выполнен откат; 5 — ошибка плана
        public static int ApplyUpdate(string planPath)
        {
            string err;
            UpdatePlan p = UpdatePlan.Load(planPath, out err);
            if (p == null) { Log("план отклонён: " + err); WriteResult("failed", "Обновление не выполнено: " + err, null); return 5; }
            Log("помощник: обновление " + p.OldVersion + " → " + p.NewVersion + ", файл " + p.TargetExe);

            // 1. дождаться закрытия Veylix (не убиваем его: он сам корректно останавливает обход)
            if (p.Pid > 0)
            {
                try
                {
                    using (Process old = Process.GetProcessById(p.Pid))
                    {
                        if (!old.WaitForExit(p.CloseTimeoutSec * 1000))
                        {
                            Log("Veylix не закрылся за " + p.CloseTimeoutSec + " с — отмена, файлы не изменены");
                            WriteResult("failed", "Veylix не закрылся за " + p.CloseTimeoutSec + " с, поэтому обновление отменено. Файлы не изменены — повторите попытку.", p);
                            return 2;
                        }
                    }
                }
                catch (ArgumentException) { }   // процесс уже завершён
            }

            // 2. проверить новую версию ещё раз (файл мог измениться между проверкой и установкой)
            if (!File.Exists(p.NewExe) || Updater.Sha256File(p.NewExe) != p.NewSha256)
            {
                Log("новая версия изменилась или пропала — отмена");
                WriteResult("failed", "Файл новой версии изменился после проверки — обновление отменено. Повторите загрузку.", p);
                StartApp(p, null);
                return 3;
            }

            // 3. положить новую версию рядом (тот же диск) и атомарно заменить, сохранив старую как резервную
            string staged = p.TargetExe + ".new";
            if (!Retry(delegate { File.Copy(p.NewExe, staged, true); }, 5, out err) || Updater.Sha256File(staged) != p.NewSha256)
            {
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                Log("не удалось подготовить файл рядом с Veylix.exe: " + err);
                WriteResult("failed", "Не удалось записать новую версию рядом с Veylix.exe (" + err + "). Установлена прежняя версия " + p.OldVersion + ".", p);
                StartApp(p, null);
                return 3;
            }
            if (!Retry(delegate { File.Replace(staged, p.TargetExe, p.BackupExe, true); }, 8, out err))
            {
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                Log("замена не удалась: " + err);
                WriteResult("failed", "Не удалось заменить Veylix.exe (" + err + "). Возможно, файл занят антивирусом. Прежняя версия " + p.OldVersion + " не тронута.", p);
                StartApp(p, null);
                return 3;
            }
            Log("файл заменён, резервная копия: " + p.BackupExe);

            // 4. запустить новую версию и дождаться подтверждения, что она работает
            string ready = ReadyFlag(p.NewVersion);
            try { if (File.Exists(ready)) File.Delete(ready); } catch { }
            Process np = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(p.TargetExe, "--post-update \"" + planPath + "\"" + DataArgs(p));
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(p.TargetExe);
                np = Process.Start(psi);
            }
            catch (Exception ex) { Log("новая версия не запустилась: " + ex.Message); }
            DateTime until = DateTime.Now.AddSeconds(p.ReadyTimeoutSec);
            bool ok = false;
            while (np != null && DateTime.Now < until)
            {
                if (File.Exists(ready)) { ok = true; break; }
                if (np.HasExited) break;
                Thread.Sleep(300);
            }
            if (ok)
            {
                Log("новая версия " + p.NewVersion + " запустилась и подтвердила готовность");
                PruneBackups(p.TargetExe, p.BackupExe);
                try { File.Delete(ready); } catch { }
                try { Directory.Delete(Path.GetDirectoryName(p.NewExe), true); } catch { }
                return 0;
            }

            // 5. откат
            Log("новая версия не подтвердила запуск" + (np != null && np.HasExited ? " (процесс завершился, код " + SafeExit(np) + ")" : "") + " — откат");
            try { if (np != null && !np.HasExited) { np.Kill(); np.WaitForExit(5000); } } catch { }
            if (Retry(delegate { File.Copy(p.BackupExe, p.TargetExe, true); }, 10, out err))
            {
                WriteResult("rolledback", "Новая версия " + p.NewVersion + " не запустилась, поэтому восстановлена прежняя версия " + p.OldVersion + ". Подробности — в updates\\update.log.", p);
                Log("восстановлена версия " + p.OldVersion);
                StartApp(p, null);
                return 4;
            }
            WriteResult("failed", "Новая версия не запустилась, и вернуть прежнюю автоматически не удалось (" + err + "). Резервная копия: " + p.BackupExe, p);
            Log("ОТКАТ НЕ УДАЛСЯ: " + err);
            return 4;
        }

        static string SafeExit(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        // ===================== новая версия после обновления =====================

        // Вызывается в новом процессе при --post-update. Подтверждает запуск или возвращает null, если план не наш.
        public static UpdatePlan ConfirmStarted(string planPath, out string message)
        {
            message = null;
            string err;
            UpdatePlan p = UpdatePlan.Load(planPath, out err);
            if (p == null) return null;
            if (p.NewVersion != Program.Version) return null;   // запущена другая версия (например, после отката)
            if (p.TestFailStart) Environment.Exit(7);          // проверочный режим: «новая версия не смогла стартовать»
            try { File.WriteAllText(ReadyFlag(p.NewVersion), Program.Version); } catch { }
            message = "Veylix обновлён: " + p.OldVersion + " → " + p.NewVersion + ". Настройки и журналы сохранены; прежняя версия лежит рядом как " + Path.GetFileName(p.BackupExe) + ".";
            return p;
        }

        // Результат неудачного обновления, записанный помощником (показывается один раз)
        public static string TakeFailure()
        {
            try
            {
                if (!File.Exists(ResultFile)) return null;
                string msg = null, status = null;
                foreach (string line in File.ReadAllLines(ResultFile, Encoding.UTF8))
                {
                    if (line.StartsWith("Message=")) msg = line.Substring(8);
                    if (line.StartsWith("Status=")) status = line.Substring(7);
                }
                File.Delete(ResultFile);
                return status != null && status != "ok" ? msg : null;
            }
            catch { return null; }
        }

        // Удалить старые резервные копии, оставив последнюю (только наши файлы Veylix.exe.backup-*)
        public static void PruneBackups(string targetExe, string keep)
        {
            try
            {
                string dir = Path.GetDirectoryName(targetExe), name = Path.GetFileName(targetExe) + ".backup-";
                foreach (string f in Directory.GetFiles(dir, name + "*"))
                    if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase) && ParseVersion(Path.GetFileName(f).Substring(name.Length)) != null)
                        File.Delete(f);
            }
            catch { }
        }
    }
}
