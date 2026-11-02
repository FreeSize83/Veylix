using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Veylix
{
    // «Игры и приложения»: готовые наборы доменов (Roblox, Steam, Twitch…). Список лежит в репозитории
    // (presets/apps.json) и скачивается раз в 12 часов — правки доходят до всех без новой версии Veylix.
    // Отмеченные наборы записываются отдельным блоком в list-general-user.txt; свои домены пользователя не трогаются.
    public static class AppPresets
    {
        public const string RemoteUrl = "https://raw.githubusercontent.com/FreeSize83/Veylix/main/presets/apps.json";
        public const string BlockStart = "# --- Veylix: игры и приложения (этот блок обновляет Veylix, правки в нём будут перезаписаны) ---";
        public const string BlockEnd = "# --- конец блока Veylix ---";
        public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(12);
        static readonly Regex IdRe = new Regex("^[a-z0-9-]{1,32}$");

        public class Preset
        {
            public string Id, Title, Group, Note;
            public List<string> Domains = new List<string>();
            public List<string> Games = new List<string>();   // для исключений: exe игры — для паузы обхода во время игры
        }

        public class Catalog
        {
            public int Version;
            public string Updated = "";
            public List<Preset> Presets = new List<Preset>();
            // Игры, которые ломаются от обхода: их домены пишутся в исключения (list-exclude-user.txt).
            // Отдельный ключ «exclusions», чтобы старые версии Veylix не приняли их за наборы обхода.
            public List<Preset> Exclusions = new List<Preset>();

            public Preset Find(string id)
            {
                foreach (Preset p in Presets) if (p.Id == id) return p;
                return null;
            }

            public Preset FindExclusion(string id)
            {
                foreach (Preset p in Exclusions) if (p.Id == id) return p;
                return null;
            }
        }

        public static string CachePath { get { return Path.Combine(Settings.Dir, "presets-apps.json"); } }

        public static string GroupTitle(string g)
        {
            switch (g)
            {
                case "apps": return "Мессенджеры";
                case "media": return "Соцсети и видео";
                case "games": return "Игры и лаунчеры";
                default: return "Другое";
            }
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        // Строгий разбор: неверный набор или домен отбрасываются, чтобы ошибка в файле не попала в списки zapret
        public static Catalog Parse(string json)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 2 * 1024 * 1024;
            Dictionary<string, object> d = js.Deserialize<Dictionary<string, object>>(json);
            if (d == null) throw new InvalidDataException("пустой файл наборов");
            Catalog c = new Catalog();
            object v;
            if (d.TryGetValue("version", out v)) try { c.Version = Convert.ToInt32(v); } catch { }
            c.Updated = Str(d, "updated");
            if (!d.TryGetValue("presets", out v) || !(v is System.Collections.IEnumerable)) throw new InvalidDataException("в файле нет наборов");
            foreach (object o in (System.Collections.IEnumerable)v)
            {
                Preset pr = ParsePreset(o as Dictionary<string, object>);
                if (pr != null && c.Find(pr.Id) == null) c.Presets.Add(pr);
            }
            if (c.Presets.Count == 0) throw new InvalidDataException("в файле нет ни одного верного набора");
            if (d.TryGetValue("exclusions", out v) && v is System.Collections.IEnumerable)
                foreach (object o in (System.Collections.IEnumerable)v)
                {
                    Preset pr = ParsePreset(o as Dictionary<string, object>);
                    if (pr != null && c.FindExclusion(pr.Id) == null) c.Exclusions.Add(pr);
                }
            return c;
        }

        static Preset ParsePreset(Dictionary<string, object> p)
        {
            if (p == null) return null;
            Preset pr = new Preset();
            pr.Id = Str(p, "id");
            pr.Title = Str(p, "title").Trim();
            pr.Group = Str(p, "group");
            pr.Note = Str(p, "note").Trim();
            if (!IdRe.IsMatch(pr.Id) || pr.Title.Length == 0) return null;
            object dl;
            if (p.TryGetValue("domains", out dl) && dl is System.Collections.IEnumerable)
                foreach (object x in (System.Collections.IEnumerable)dl)
                {
                    string h = SiteCheck.NormalizeHost(Convert.ToString(x));
                    if (h != null && !pr.Domains.Contains(h)) pr.Domains.Add(h);
                }
            if (p.TryGetValue("games", out dl) && dl is System.Collections.IEnumerable)
                foreach (object x in (System.Collections.IEnumerable)dl)
                {
                    string g = GameWatch.Normalize(Convert.ToString(x));
                    if (g != null && !pr.Games.Contains(g)) pr.Games.Add(g);
                }
            return pr.Domains.Count > 0 || pr.Games.Count > 0 ? pr : null;
        }

        public static Catalog Embedded()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("apps.json"))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return Parse(r.ReadToEnd());
        }

        // Скачанная копия, если она не старее встроенной; иначе встроенная
        public static Catalog LoadLocal()
        {
            Catalog emb = Embedded();
            try
            {
                if (File.Exists(CachePath))
                {
                    Catalog cached = Parse(File.ReadAllText(CachePath, Encoding.UTF8));
                    if (cached.Version >= emb.Version) return cached;
                }
            }
            catch { }
            return emb;
        }

        // Скачать свежий список; null — не удалось (остаётся прежний)
        public static Catalog Download(out string error)
        {
            error = null;
            try
            {
                string json = Http.GetString(RemoteUrl, 15000, "application/json");
                if (json.Length > 1024 * 1024) throw new InvalidDataException("слишком большой файл");
                Catalog c = Parse(json);
                File.WriteAllText(CachePath, json, new UTF8Encoding(false));
                return c;
            }
            catch (Exception ex) { error = Http.Describe(ex); return null; }
        }

        public static List<string> Selected(string stored)
        {
            List<string> r = new List<string>();
            foreach (string p in (stored ?? "").Split('|'))
            {
                string id = p.Trim();
                if (IdRe.IsMatch(id) && !r.Contains(id)) r.Add(id);
            }
            return r;
        }

        public static List<string> DomainsFor(Catalog c, IList<string> ids)
        {
            List<string> r = new List<string>();
            foreach (string id in ids)
            {
                Preset p = c.Find(id);
                if (p == null) continue;
                foreach (string d in p.Domains) if (!r.Contains(d)) r.Add(d);
            }
            r.Sort(StringComparer.Ordinal);
            return r;
        }

        public static List<string> ExclusionDomainsFor(Catalog c, IList<string> ids)
        {
            List<string> r = new List<string>();
            foreach (string id in ids)
            {
                Preset p = c.FindExclusion(id);
                if (p == null) continue;
                foreach (string d in p.Domains) if (!r.Contains(d)) r.Add(d);
            }
            r.Sort(StringComparer.Ordinal);
            return r;
        }

        // Домены из блока Veylix в тексте списка
        public static List<string> ReadBlock(string text)
        {
            List<string> r = new List<string>();
            bool inside = false;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.Trim();
                if (l == BlockStart) { inside = true; continue; }
                if (l == BlockEnd) { inside = false; continue; }
                if (inside && l.Length > 0 && !l.StartsWith("#")) r.Add(l.ToLowerInvariant());
            }
            return r;
        }

        // Новый текст списка: прежний блок Veylix убран, новый (если есть домены) — в конце. Остальное не меняется.
        public static string WriteBlock(string text, IList<string> domains)
        {
            List<string> keep = new List<string>();
            bool inside = false;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.Trim();
                if (l == BlockStart) { inside = true; continue; }
                if (l == BlockEnd) { inside = false; continue; }
                if (!inside) keep.Add(raw.TrimEnd());
            }
            while (keep.Count > 0 && keep[keep.Count - 1].Length == 0) keep.RemoveAt(keep.Count - 1);
            if (domains.Count > 0)
            {
                keep.Add(BlockStart);
                keep.AddRange(domains);
                keep.Add(BlockEnd);
            }
            if (SiteCheck.ParseEntries(keep).Count == 0 && !keep.Exists(delegate(string l) { return l.Trim() == SiteCheck.Placeholder; }))
                keep.Insert(0, SiteCheck.Placeholder);   // Flowseal: файл не должен оставаться пустым
            return string.Join("\r\n", keep.ToArray()) + "\r\n";
        }
    }

    public partial class MainWindow
    {
        AppPresets.Catalog presets;
        DateTime presetsChecked = DateTime.MinValue;
        bool presetsSyncing;
        StackPanel presetsHost;
        TextBlock presetsStatus;

        FrameworkElement BuildPresetsCard()
        {
            Card c = new Card(22);
            c.Margin = new Thickness(0, 0, 0, 16);
            c.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s = new StackPanel();
            s.Children.Add(Ui.Heading("Игры и приложения",
                "Отметьте, что у вас не работает, — Veylix добавит нужные домены в обход и перезапустит его. Наборы собраны по обращениям пользователей zapret и обновляются сами, без новой версии программы."));
            presetsHost = new StackPanel();
            presetsHost.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(presetsHost);
            presetsStatus = Theme.Wrap("", 11.5, "Text3");
            presetsStatus.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(presetsStatus);
            c.Content = s;
            RenderPresets();
            return c;
        }

        void RenderPresets()
        {
            if (presetsHost == null) return;
            presetsHost.Children.Clear();
            if (presets == null)
            {
                try { presets = AppPresets.LoadLocal(); }
                catch (Exception ex) { presetsStatus.Text = "Наборы недоступны: " + ex.Message; return; }
            }
            List<string> sel = AppPresets.Selected(cfg.AppPresets);
            foreach (string group in new string[] { "apps", "media", "games", "" })
            {
                List<AppPresets.Preset> items = presets.Presets.FindAll(delegate(AppPresets.Preset p) { return group.Length > 0 ? p.Group == group : p.Group != "apps" && p.Group != "media" && p.Group != "games"; });
                if (items.Count == 0) continue;
                TextBlock gt = Theme.T(AppPresets.GroupTitle(group), 12, "Text2", true);
                gt.Margin = new Thickness(0, 6, 0, 6);
                presetsHost.Children.Add(gt);
                WrapPanel row = new WrapPanel();
                foreach (AppPresets.Preset pr in items)
                {
                    AppPresets.Preset p = pr;
                    bool on = sel.Contains(p.Id);
                    VButton b = new VButton(on ? "AccentSoft" : "Surface2", on ? "Accent" : "Stroke", 10);
                    b.Height = 32;
                    b.Margin = new Thickness(0, 0, 8, 8);
                    StackPanel sp = Theme.Row();
                    sp.Margin = new Thickness(11, 0, 12, 0);
                    sp.VerticalAlignment = VerticalAlignment.Center;
                    TextBlock ic = Theme.Icon(on ? 0xE73E : 0xE710, 11, on ? "Accent" : "Text2");
                    ic.Margin = new Thickness(0, 1, 7, 0);
                    sp.Children.Add(ic);
                    sp.Children.Add(Theme.T(p.Title, 12.5, on ? "Accent" : "Text", true));
                    b.Content = sp;
                    b.ToolTip = p.Note + "\n\nДомены: " + string.Join(", ", p.Domains.ToArray()) + (on ? "\n\nНажмите, чтобы убрать из обхода." : "");
                    b.Click += delegate { TogglePreset(p.Id); };
                    row.Children.Add(b);
                }
                presetsHost.Children.Add(row);
            }
            if (presets.Exclusions.Count > 0)
            {
                TextBlock et = Theme.T("Если игра не подключается с включённым обходом", 12, "Text2", true);
                et.Margin = new Thickness(0, 10, 0, 6);
                presetsHost.Children.Add(et);
                List<string> exSel = AppPresets.Selected(cfg.AppExclusions);
                foreach (AppPresets.Preset ex in presets.Exclusions) presetsHost.Children.Add(ExclusionRow(ex, exSel.Contains(ex.Id)));
            }
            int n = AppPresets.DomainsFor(presets, sel).Count;
            DateTime upd;
            string when = DateTime.TryParseExact(presets.Updated, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out upd)
                ? upd.ToString("dd.MM.yyyy") : "—";
            presetsStatus.Text = (sel.Count > 0 ? "В обходе: " + Plural(sel.Count, "набор", "набора", "наборов") + ", " + Plural(n, "домен", "домена", "доменов") + ". " : "") +
                "Наборы от " + when + ". Не нашли свою игру? Кнопка «Написать нам» на главной — добавим.";
        }

        FrameworkElement ExclusionRow(AppPresets.Preset p, bool on)
        {
            Border box = new Border();
            box.CornerRadius = new CornerRadius(12);
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(14, 10, 12, 10);
            box.Margin = new Thickness(0, 0, 0, 8);
            box.SetResourceReference(Border.BackgroundProperty, "Surface2");
            box.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            StackPanel sp = new StackPanel();
            sp.Children.Add(Theme.T(p.Title, 13, "Text", true));
            TextBlock note = Theme.Wrap(p.Note, 11.5, "Text2");
            note.Margin = new Thickness(0, 3, 0, 8);
            sp.Children.Add(note);
            WrapPanel act = new WrapPanel();
            if (p.Domains.Count > 0)
            {
                VButton b = new VButton(on ? "AccentSoft" : "Surface", on ? "Accent" : "Stroke", 10);
                b.Height = 30;
                b.Margin = new Thickness(0, 0, 8, 0);
                StackPanel bs = Theme.Row();
                bs.Margin = new Thickness(10, 0, 12, 0);
                bs.VerticalAlignment = VerticalAlignment.Center;
                TextBlock ic = Theme.Icon(on ? 0xE73E : 0xE711, 10.5, on ? "Accent" : "Text2");
                ic.Margin = new Thickness(0, 1, 7, 0);
                bs.Children.Add(ic);
                bs.Children.Add(Theme.T(on ? "Исключена из обхода" : "Исключить из обхода", 12, on ? "Accent" : "Text", true));
                b.Content = bs;
                b.ToolTip = "Домены: " + string.Join(", ", p.Domains.ToArray()) + (on ? "\nНажмите, чтобы вернуть игру в обход." : "");
                b.Click += delegate { ToggleExclusion(p.Id); };
                act.Children.Add(b);
            }
            if (p.Games.Count > 0)
            {
                bool paused = cfg.GameWatch && p.Games.TrueForAll(delegate(string g) { return GameList.Contains(g); });
                VButton g2 = new VButton(paused ? "AccentSoft" : "Surface", paused ? "Accent" : "Stroke", 10);
                g2.Height = 30;
                StackPanel gs = Theme.Row();
                gs.Margin = new Thickness(10, 0, 12, 0);
                gs.VerticalAlignment = VerticalAlignment.Center;
                TextBlock gi = Theme.Icon(paused ? 0xE73E : 0xE7FC, 10.5, paused ? "Accent" : "Text2");
                gi.Margin = new Thickness(0, 1, 7, 0);
                gs.Children.Add(gi);
                gs.Children.Add(Theme.T(paused ? "Пауза во время игры включена" : "Пауза обхода во время игры", 12, paused ? "Accent" : "Text", true));
                g2.Content = gs;
                g2.ToolTip = "Игровой режим: пока запущена " + string.Join(" или ", p.Games.ToArray()) + ", обход стоит на паузе (Discord и YouTube без обхода), после игры включается сам." +
                    (paused ? "\nНажмите, чтобы убрать игру из игрового режима." : "");
                g2.Click += delegate
                {
                    if (paused) RemoveGames(p.Games);
                    else
                    {
                        AddGames(p.Games);
                        if (!cfg.GameWatch) { cfg.GameWatch = true; SaveSettings(); log.Add("info", "Игровой режим включён."); RefreshGameModeCard(); CheckGames(); }
                    }
                    RenderPresets();
                };
                act.Children.Add(g2);
            }
            sp.Children.Add(act);
            box.Child = sp;
            return box;
        }

        void ToggleExclusion(string id)
        {
            List<string> sel = AppPresets.Selected(cfg.AppExclusions);
            if (sel.Contains(id)) sel.Remove(id); else sel.Add(id);
            cfg.AppExclusions = string.Join("|", sel.ToArray());
            SaveSettings();
            AppPresets.Preset p = presets != null ? presets.FindExclusion(id) : null;
            log.Add("info", (sel.Contains(id) ? "Исключено из обхода: «" : "Возвращено в обход: «") + (p != null ? p.Title : id) + "».");
            ApplyPresets(true);
            RenderPresets();
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return n + " " + (m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many);
        }

        void TogglePreset(string id)
        {
            List<string> sel = AppPresets.Selected(cfg.AppPresets);
            if (sel.Contains(id)) sel.Remove(id); else sel.Add(id);
            cfg.AppPresets = string.Join("|", sel.ToArray());
            SaveSettings();
            AppPresets.Preset p = presets != null ? presets.Find(id) : null;
            log.Add("info", (sel.Contains(id) ? "Игры и приложения: включён набор «" : "Игры и приложения: выключен набор «") + (p != null ? p.Title : id) + "».");
            ApplyPresets(true);
            RenderPresets();
        }

        // Записать блок в список; перезапустить обход, только если список доменов действительно изменился
        async void ApplyPresets(bool userAction)
        {
            if (presets == null) return;
            try
            {
                engine.EnsureUserLists();
                bool a = SyncBlock(Path.Combine(engine.Lists, SiteCheck.UserListFile), AppPresets.DomainsFor(presets, AppPresets.Selected(cfg.AppPresets)));
                bool b = SyncBlock(Path.Combine(engine.Lists, SiteCheck.ExcludeFiles[0]), AppPresets.ExclusionDomainsFor(presets, AppPresets.Selected(cfg.AppExclusions)));
                if (!a && !b) return;
                RefreshSiteList();
                if (engine.IsRunning && !busy && !tuning && !updating) await RestartIfRunning();
                else if (userAction && !engine.IsRunning) log.Add("info", "Домены записаны в список — они заработают, когда вы включите обход.");
            }
            catch (Exception ex) { log.Add("err", "Игры и приложения: не удалось обновить список — " + ex.Message); }
        }

        // true — блок в файле изменён
        static bool SyncBlock(string path, List<string> want)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            List<string> have = AppPresets.ReadBlock(text);
            have.Sort(StringComparer.Ordinal);
            if (string.Join("|", have.ToArray()) == string.Join("|", want.ToArray())) return false;
            File.WriteAllText(path, AppPresets.WriteBlock(text, want));
            return true;
        }

        // Раз в 12 часов (и при запуске) — свежие наборы из репозитория
        async void SyncPresets()
        {
            if (presetsSyncing || selfTest) return;
            if (DateTime.Now - presetsChecked < AppPresets.RefreshEvery) return;
            presetsSyncing = true;
            presetsChecked = DateTime.Now;
            try
            {
                if (presets == null) presets = await Task.Run(() => AppPresets.LoadLocal());
                string err = null;
                AppPresets.Catalog fresh = await Task.Run(() => AppPresets.Download(out err));
                if (fresh != null && fresh.Version >= presets.Version)
                {
                    bool changed = fresh.Version != presets.Version || fresh.Updated != presets.Updated;
                    presets = fresh;
                    if (changed) log.Add("info", "Игры и приложения: наборы обновлены (" + fresh.Updated + ").");
                }
                ApplyPresets(false);   // применит и изменения наборов, и возможную ручную правку блока
                RenderPresets();
            }
            catch { }
            finally { presetsSyncing = false; }
        }
    }
}
