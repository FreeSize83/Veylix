using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Veylix
{
    public class LogEntry
    {
        public DateTime Time;
        public string Level;   // ok | info | warn | err | proc
        public string Text;
    }

    // Журнал событий: хранится в памяти (последние записи) и дублируется в файл veylix.log.
    // В файл и при экспорте пишется только очищенный от личных данных текст.
    public class LogStore
    {
        const int Capacity = 3000;
        const long MaxFileBytes = 1024 * 1024;

        readonly List<LogEntry> items = new List<LogEntry>();
        readonly object sync = new object();
        public event Action<LogEntry> Added;
        public event Action Cleared;

        public static string FilePath
        {
            get { return Path.Combine(Settings.Dir, "veylix.log"); }
        }

        public void Add(string level, string text)
        {
            LogEntry e = new LogEntry();
            e.Time = DateTime.Now;
            e.Level = level;
            e.Text = Redact(text ?? "");
            lock (sync)
            {
                items.Add(e);
                if (items.Count > Capacity) items.RemoveRange(0, items.Count - Capacity);
            }
            AppendToFile(e);
            Action<LogEntry> h = Added;
            if (h != null) h(e);
        }

        public List<LogEntry> Snapshot()
        {
            lock (sync) { return new List<LogEntry>(items); }
        }

        public void Clear()
        {
            lock (sync) { items.Clear(); }
            Action h = Cleared;
            if (h != null) h();
        }

        public static string LevelName(string level)
        {
            switch (level)
            {
                case "ok": return "УСПЕХ";
                case "warn": return "ПРЕДУПР";
                case "err": return "ОШИБКА";
                case "proc": return "WINWS";
                default: return "ИНФО";
            }
        }

        public static string Format(LogEntry e)
        {
            return e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + LevelName(e.Level).PadRight(8) + " " + e.Text;
        }

        void AppendToFile(LogEntry e)
        {
            try
            {
                string f = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                FileInfo fi = new FileInfo(f);
                if (fi.Exists && fi.Length > MaxFileBytes)
                {
                    string old = f + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(f, old);
                }
                File.AppendAllText(f, Sanitize(Format(e)) + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        public static string Export(string path, IList<LogEntry> entries, string header)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                if (header != null) sb.Append(header).Append("\r\n\r\n");
                foreach (LogEntry e in entries) sb.Append(Sanitize(Format(e))).Append("\r\n");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Секреты не должны попадать даже в память журнала: логины/пароли в URL, токены в параметрах
        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Regex.Replace(s, @"(?i)\b([a-z][a-z0-9+.\-]*://)[^\s/@:]+:[^\s/@]+@", "$1***:***@");
            s = Regex.Replace(s, @"(?i)\b(token|access_token|api[_-]?key|key|password|passwd|pwd|secret|auth|authorization|sig|signature)=([^&\s""']+)", "$1=***");
            s = Regex.Replace(s, @"(?i)\b(bearer|basic)\s+[A-Za-z0-9._~+/=\-]{8,}", "$1 ***");
            return s;
        }

        // Для файлов и экспорта: убираем имя пользователя, имя компьютера и полные IP-адреса
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Redact(s);
            s = ReplacePath(s, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%");
            s = ReplacePath(s, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%");
            s = ReplacePath(s, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length >= 3)
                s = Regex.Replace(s, @"(?<![\w])" + Regex.Escape(user) + @"(?![\w])", "<user>", RegexOptions.IgnoreCase);
            string pc = Environment.MachineName;
            if (!string.IsNullOrEmpty(pc) && pc.Length >= 3)
                s = Regex.Replace(s, @"(?<![\w])" + Regex.Escape(pc) + @"(?![\w])", "<pc>", RegexOptions.IgnoreCase);
            // IPv4: оставляем две первые части (по ним видно провайдера/подсеть, но не конкретный адрес)
            s = Regex.Replace(s, @"\b(\d{1,3})\.(\d{1,3})\.\d{1,3}\.\d{1,3}\b", "$1.$2.x.x");
            // IPv6: оставляем первые две группы
            s = Regex.Replace(s, @"\b([0-9a-fA-F]{1,4}):([0-9a-fA-F]{1,4})(:[0-9a-fA-F]{0,4}){2,7}\b", "$1:$2:…");
            return s;
        }

        static string ReplacePath(string s, string path, string token)
        {
            if (string.IsNullOrEmpty(path)) return s;
            return Regex.Replace(s, Regex.Escape(path), token.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }
    }
}
