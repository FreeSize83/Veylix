using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Veylix
{
    public class ReleaseInfo
    {
        public string Version = "", Name = "", Body = "", HtmlUrl = "";
        public string ZipName, ZipUrl, ZipSha256;
        public long ZipSize;
        public DateTime Published;

        public bool CanInstall
        {
            get { return ZipUrl != null && ZipSha256 != null; }
        }
    }

    // Обновление zapret-discord-youtube от Flowseal.
    // Источник — официальный репозиторий, указанный в service.bat:
    // https://github.com/Flowseal/zapret-discord-youtube
    public static class Updater
    {
        public const string Repo = "Flowseal/zapret-discord-youtube";
        public const string RepoUrl = "https://github.com/" + Repo;
        public const string ReleasesUrl = RepoUrl + "/releases";
        public const string ApiLatest = "https://api.github.com/repos/" + Repo + "/releases/latest";
        const string TrustedDownloadPrefix = RepoUrl + "/releases/download/";
        const long MaxZipBytes = 50L * 1024 * 1024;
        const long MaxUnpackedBytes = 200L * 1024 * 1024;
        const int MaxEntries = 5000;

        // ---------- сравнение версий ----------

        public static int[] ParseVersion(string v)
        {
            if (v == null) return new int[0];
            Match m = Regex.Match(v, @"\d+(\.\d+)*");
            if (!m.Success) return new int[0];
            string[] parts = m.Value.Split('.');
            int[] r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int x;
                int.TryParse(parts[i], out x);
                r[i] = x;
            }
            return r;
        }

        public static int CompareVersions(string a, string b)
        {
            int[] x = ParseVersion(a), y = ParseVersion(b);
            int n = Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                int p = i < x.Length ? x[i] : 0;
                int q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        public static bool IsTrustedDownloadUrl(string url)
        {
            if (url == null) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps || !u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
            // сравниваем нормализованный адрес: «releases/download/../../x» превращается в другой путь
            if (url.Contains("..") || u.AbsolutePath.Contains("..")) return false;
            return u.AbsoluteUri.StartsWith(TrustedDownloadPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- информация о последнем релизе ----------

        public static ReleaseInfo FetchLatest()
        {
            string json = Http.GetString(ApiLatest, 15000, "application/vnd.github+json");
            return ParseRelease(json);
        }

        public static ReleaseInfo ParseRelease(string json)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 8 * 1024 * 1024;
            Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) throw new InvalidDataException("Неожиданный ответ GitHub.");
            ReleaseInfo r = new ReleaseInfo();
            r.Version = Str(d, "tag_name");
            r.Name = Str(d, "name");
            r.Body = Str(d, "body");
            r.HtmlUrl = Str(d, "html_url");
            if (!r.HtmlUrl.StartsWith(RepoUrl + "/", StringComparison.OrdinalIgnoreCase)) r.HtmlUrl = ReleasesUrl;
            DateTime pub;
            if (DateTime.TryParse(Str(d, "published_at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out pub))
                r.Published = pub.ToLocalTime();
            if (r.Version.Length == 0) throw new InvalidDataException("В ответе GitHub нет номера версии.");

            object[] assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name");
                    string url = Str(a, "browser_download_url");
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !IsTrustedDownloadUrl(url)) continue;
                    r.ZipName = name;
                    r.ZipUrl = url;
                    long size;
                    long.TryParse(Str(a, "size"), out size);
                    r.ZipSize = size;
                    // GitHub публикует SHA-256 каждого файла релиза в поле digest ("sha256:<hex>").
                    // Своих контрольных сумм не придумываем: нет digest — нет автоматической установки.
                    string digest = Str(a, "digest");
                    Match m = Regex.Match(digest, "^sha256:([0-9a-fA-F]{64})$");
                    r.ZipSha256 = m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
                    break;
                }
            }
            return r;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return "";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------- безопасная распаковка ----------

        // Полный путь для записи файла из архива или null, если путь выходит за пределы папки (zip-slip)
        public static string SafeEntryPath(string root, string entryName)
        {
            if (string.IsNullOrEmpty(entryName)) return null;
            string n = entryName.Replace('/', '\\');
            if (n.StartsWith("\\") || n.Contains(":")) return null;
            foreach (string part in n.Split('\\'))
                if (part == "..") return null;
            string full = Path.GetFullPath(Path.Combine(root, n));
            string baseDir = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }

        public static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static void ExtractSafely(string zipPath, string dest)
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("В архиве слишком много файлов.");
                long total = 0;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    total += e.Length;
                    if (total > MaxUnpackedBytes) throw new InvalidDataException("Архив распаковывается в слишком большой объём.");
                    if (SafeEntryPath(dest, e.FullName) == null)
                        throw new InvalidDataException("Небезопасный путь в архиве: " + e.FullName);
                }
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string target = SafeEntryPath(dest, e.FullName);
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, false);
                }
            }
        }

        // Папка внутри распакованного архива, где лежит bin\winws.exe
        public static string FindPackageRoot(string dir)
        {
            if (File.Exists(Path.Combine(dir, "bin", "winws.exe"))) return dir;
            foreach (string sub in Directory.GetDirectories(dir))
                if (File.Exists(Path.Combine(sub, "bin", "winws.exe"))) return sub;
            return null;
        }

        public static string ValidatePackage(string root)
        {
            if (root == null) return "в архиве не найден bin\\winws.exe";
            string[] need = { "bin\\winws.exe", "bin\\WinDivert.dll", "bin\\WinDivert64.sys", "service.bat" };
            foreach (string n in need)
                if (!File.Exists(Path.Combine(root, n))) return "в архиве нет файла " + n;
            if (Directory.GetFiles(root, "general*.bat").Length == 0) return "в архиве нет ни одной стратегии general*.bat";
            if (!Directory.Exists(Path.Combine(root, "lists"))) return "в архиве нет папки lists";
            return null;
        }

        // Пользовательские файлы, которые переносятся в новую версию
        static void CarryUserFiles(string from, string to)
        {
            string lf = Path.Combine(from, "lists"), lt = Path.Combine(to, "lists");
            if (Directory.Exists(lf))
            {
                Directory.CreateDirectory(lt);
                foreach (string f in Directory.GetFiles(lf))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (n.EndsWith("-user.txt") || n == "ipset-all.txt" || n == "ipset-all.txt.backup")
                        File.Copy(f, Path.Combine(lt, Path.GetFileName(f)), true);
                }
            }
            string uf = Path.Combine(from, "utils"), ut = Path.Combine(to, "utils");
            Directory.CreateDirectory(ut);
            string game = Path.Combine(uf, "game_filter.enabled");
            if (File.Exists(game)) File.Copy(game, Path.Combine(ut, "game_filter.enabled"), true);
            // флаг проверки обновлений — как выбрал пользователь
            string cu = "check_updates.enabled";
            if (File.Exists(Path.Combine(uf, cu))) File.Copy(Path.Combine(uf, cu), Path.Combine(ut, cu), true);
            else if (File.Exists(Path.Combine(ut, cu))) File.Delete(Path.Combine(ut, cu));
            foreach (string m in new string[] { ".version" })
                if (File.Exists(Path.Combine(from, m))) File.Copy(Path.Combine(from, m), Path.Combine(to, m), true);
        }

        public static string BackupPrefix(string root)
        {
            return Path.GetFullPath(root).TrimEnd('\\') + ".backup-";
        }

        public static string LatestBackup(string root)
        {
            try
            {
                string prefix = BackupPrefix(root);
                string parent = Path.GetDirectoryName(prefix);
                List<string> all = new List<string>(Directory.GetDirectories(parent, Path.GetFileName(prefix) + "*"));
                all.Sort(CompareBackups);
                return all.Count > 0 ? all[all.Count - 1] : null;
            }
            catch { return null; }
        }

        // Установка: скачать → проверить SHA-256 → распаковать во временную папку → проверить состав →
        // остановить обход → переименовать текущую папку в резервную → поставить новую.
        // При любой ошибке после переименования старая папка возвращается на место.
        public static string Install(ReleaseInfo r, string root, Action<string> progress, Action stopEngine)
        {
            if (!r.CanInstall) return "для релиза нет проверяемого .zip-архива";
            if (!IsTrustedDownloadUrl(r.ZipUrl)) return "адрес загрузки не относится к официальному репозиторию";
            if (r.ZipSize > MaxZipBytes) return "архив слишком большой";

            string work = Path.Combine(Settings.Dir, "updates");
            string zip = Path.Combine(work, "flowseal-" + SafeName(r.Version) + ".zip");
            try
            {
                Directory.CreateDirectory(work);
                progress("Скачиваю " + r.ZipName + "…");
                Http.Download(r.ZipUrl, zip, 60000, MaxZipBytes, null);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
                return "не удалось скачать архив: " + Http.Describe(ex);
            }
            try
            {
                return InstallFromZip(zip, r.ZipSha256, r.Version, root, progress, stopEngine);
            }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            }
        }

        public static string InstallFromZip(string zip, string expectedSha256, string version, string root, Action<string> progress, Action stopEngine)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
            // временная папка рядом с текущей: перемещение внутри одного диска атомарно
            string staging = fullRoot + ".staging-" + SafeName(version);
            string backup = BackupPrefix(fullRoot) + SafeName(version) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            bool moved = false;
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);

                progress("Проверяю контрольную сумму SHA-256…");
                string actual = Sha256File(zip);
                if (expectedSha256 == null || actual != expectedSha256.ToLowerInvariant())
                    return "контрольная сумма не совпала (ожидалась " + Short(expectedSha256) + "…, получена " + Short(actual) + "…). Установка отменена, файлы не изменены.";

                progress("Распаковываю во временную папку…");
                ExtractSafely(zip, staging);
                string pkg = FindPackageRoot(staging);
                string bad = ValidatePackage(pkg);
                if (bad != null) return "архив не прошёл проверку: " + bad + ". Файлы не изменены.";

                progress("Переношу пользовательские списки и настройки…");
                CarryUserFiles(fullRoot, pkg);
                File.WriteAllText(Path.Combine(pkg, Engine.UpdatedMarker), version);

                progress("Останавливаю обход…");
                stopEngine();

                progress("Создаю резервную копию текущей версии…");
                Directory.Move(fullRoot, backup);
                moved = true;
                Directory.Move(pkg, fullRoot);
                moved = false;

                PruneBackups(fullRoot, 2);
                progress("Готово.");
                return null;
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (moved)
                {
                    try
                    {
                        if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
                        Directory.Move(backup, fullRoot);
                        msg += " Предыдущая версия восстановлена из резервной копии.";
                    }
                    catch (Exception ex2)
                    {
                        msg += " ВНИМАНИЕ: не удалось вернуть резервную копию автоматически (" + ex2.Message + "). Она лежит в " + backup;
                    }
                }
                else
                {
                    msg += " Файлы не изменены.";
                }
                return msg;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        static string Short(string h)
        {
            return string.IsNullOrEmpty(h) ? "?" : h.Substring(0, Math.Min(12, h.Length));
        }

        // Откат на последнюю резервную копию (текущая версия тоже сохраняется как копия)
        public static string Rollback(string root, Action stopEngine)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
            string backup = LatestBackup(fullRoot);
            if (backup == null) return "резервных копий нет";
            string aside = BackupPrefix(fullRoot) + "rolledback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            stopEngine();
            try
            {
                Directory.Move(fullRoot, aside);
            }
            catch (Exception ex) { return "не удалось освободить папку: " + ex.Message; }
            try
            {
                Directory.Move(backup, fullRoot);
                return null;
            }
            catch (Exception ex)
            {
                try { Directory.Move(aside, fullRoot); } catch { }
                return ex.Message;
            }
        }

        static void PruneBackups(string root, int keep)
        {
            try
            {
                string prefix = BackupPrefix(root);
                List<string> all = new List<string>(Directory.GetDirectories(Path.GetDirectoryName(prefix), Path.GetFileName(prefix) + "*"));
                all.Sort(CompareBackups);
                for (int i = 0; i < all.Count - keep; i++)
                {
                    try { Directory.Delete(all[i], true); } catch { }
                }
            }
            catch { }
        }

        // имена резервных копий заканчиваются на yyyyMMdd-HHmmss — сортируем по этой метке
        static int CompareBackups(string a, string b)
        {
            return string.CompareOrdinal(Stamp(a), Stamp(b));
        }

        static string Stamp(string dir)
        {
            Match m = Regex.Match(dir, @"(\d{8}-\d{6})$");
            return m.Success ? m.Groups[1].Value : "";
        }

        static string SafeName(string s)
        {
            return Regex.Replace(s ?? "", @"[^0-9A-Za-z.\-]", "_");
        }
    }
}
