using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace RaskladWindows {
    sealed class ITEntry {
        public string Head, Body, Language;
        public ITEntry(string head, string body, string language) { Head = head; Body = body; Language = language; }
    }

    sealed class ITDictionary {
        private readonly Dictionary<string, ITEntry> entries = new Dictionary<string, ITEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> names;
        public int Count { get { return entries.Count; } }

        public ITDictionary(string foldoc, string russian) {
            string head = null;
            StringBuilder body = new StringBuilder();
            using (StringReader reader = new StringReader(foldoc)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    if (line.Length > 0 && !Char.IsWhiteSpace(line[0])) {
                        AddFoldoc(head, body); head = line.Trim(); body.Clear();
                    } else if (line.Trim().Length == 0) {
                        if (body.Length > 0 && body[body.Length - 1] != '\n') body.Append("\n\n");
                    } else if (head != null) {
                        if (body.Length > 0 && body[body.Length - 1] != '\n') body.Append(' ');
                        body.Append(line.Trim());
                    }
                }
            }
            AddFoldoc(head, body);
            using (StringReader reader = new StringReader(russian)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    if (String.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    string[] columns = line.IndexOf('\t') >= 0 ? line.Split('\t') : line.Split(new[] { " — " }, 2, StringSplitOptions.None);
                    if (columns.Length < 2) continue;
                    string[] aliases = columns[0].Split('|');
                    if (aliases[0].Trim().Length == 0 || columns[1].Trim().Length == 0) continue;
                    ITEntry entry = new ITEntry(aliases[0].Trim(), columns[1].Trim(), "ru");
                    foreach (string alias in aliases) AddName(alias, entry);
                    if (columns.Length > 2) foreach (string alias in columns[2].Split('|')) AddName(alias, entry);
                }
            }
            names = new List<string>(entries.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void AddFoldoc(string head, StringBuilder body) {
            if (head != null) AddName(head, new ITEntry(head, body.ToString().Trim(), "en"));
        }
        private void AddName(string name, ITEntry entry) {
            name = name.Trim();
            if (name.Length > 0) entries[name] = entry;
        }
        public static ITDictionary Load() {
            return new ITDictionary(ReadResource("IT.FOLDOC"), ReadResource("IT.Russian"));
        }
        private static string ReadResource(string name) {
            using (Stream stream = typeof(ITDictionary).Assembly.GetManifestResourceStream(name)) {
                if (stream == null) throw new InvalidDataException("Не найден встроенный словарь: " + name);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
        public ITEntry Find(string name) {
            if (String.IsNullOrWhiteSpace(name)) return null;
            ITEntry entry;
            if (!entries.TryGetValue(name.Trim(), out entry)) return null;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (visited.Add(entry.Head)) {
                string body = entry.Body.TrimEnd('.');
                ITEntry target;
                if (body.Length < 3 || body[0] != '{' || body[body.Length - 1] != '}' ||
                    body.IndexOf('}') != body.Length - 1 ||
                    !entries.TryGetValue(body.Substring(1, body.Length - 2), out target)) break;
                entry = target;
            }
            return entry;
        }
        public bool IsWord(string word, bool russian) {
            if (String.IsNullOrEmpty(word) || word.Length < 4) return false;
            foreach (char c in word) {
                bool letter = russian ? (c >= 'А' && c <= 'я' || c == 'Ё' || c == 'ё') : (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z');
                if (!letter) return false;
            }
            ITEntry entry = Find(word);
            return entry != null && entry.Language == (russian ? "ru" : "en");
        }
        public List<string> Search(string query, int limit) {
            query = (query ?? "").Trim();
            List<string> result = new List<string>();
            if (entries.ContainsKey(query)) result.Add(query);
            foreach (string name in names) {
                if (result.Count >= limit) return result;
                if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase) && !name.Equals(query, StringComparison.OrdinalIgnoreCase)) result.Add(name);
            }
            if (query.Length > 0) foreach (string name in names) {
                if (result.Count >= limit) break;
                if (!name.StartsWith(query, StringComparison.OrdinalIgnoreCase) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(name);
            }
            return result;
        }
    }

    sealed class ITDictionaryWindow : Form {
        private readonly TextBox query = new TextBox();
        private readonly ListBox results = new ListBox();
        private readonly RichTextBox article = new RichTextBox();
        private readonly ITDictionary dictionary;
        public ITDictionaryWindow(ITDictionary dictionary) {
            this.dictionary = dictionary;
            Text = "Rasklad — IT-словарь";
            Icon = AppBranding.WindowIcon;
            Size = new Size(820, 560); MinimumSize = new Size(560, 350);
            StartPosition = FormStartPosition.CenterScreen;
            query.Dock = DockStyle.Top;
            query.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12);
            SplitContainer split = new SplitContainer(); split.Dock = DockStyle.Fill;
            results.Dock = DockStyle.Fill; results.HorizontalScrollbar = true;
            article.Dock = DockStyle.Fill; article.ReadOnly = true; article.BackColor = SystemColors.Window;
            article.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11);
            split.Panel1.Controls.Add(results); split.Panel2.Controls.Add(article);
            LinkLabel source = new LinkLabel(); source.Dock = DockStyle.Bottom; source.Height = 32;
            source.Text = "FOLDOC © Denis Howe • GNU FDL 1.1+ — источник и лицензия";
            source.LinkClicked += delegate { query.Text = "GNU Free Documentation License"; };
            Controls.Add(split); Controls.Add(query); Controls.Add(source);
            query.TextChanged += delegate { RefreshResults(); };
            results.SelectedIndexChanged += delegate { ShowArticle(); };
            Shown += delegate { split.SplitterDistance = 240; query.Focus(); };
            RefreshResults();
        }
        public void SearchFor(string word) { query.Text = word ?? ""; query.SelectAll(); query.Focus(); }
        private void RefreshResults() {
            results.BeginUpdate(); results.Items.Clear();
            foreach (string name in dictionary.Search(query.Text, 200)) results.Items.Add(name);
            results.EndUpdate();
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            else article.Text = "Термин не найден. Попробуйте другое написание или начало слова.";
        }
        private void ShowArticle() {
            ITEntry entry = dictionary.Find(results.SelectedItem as string);
            if (entry != null) article.Text = entry.Head + "\n\n" + entry.Body + "\n\n" +
                (entry.Language == "en" ? "The Free On-line Dictionary of Computing — http://foldoc.org/\nEditor Denis Howe. GNU FDL 1.1+." : "Русские IT-термины — Rasklad, DK-custom. MIT.");
        }
        internal bool ArticleVisibleForTest { get { return article.Text.Length > 50 && results.Items.Count > 0; } }
    }

    static class ITDictionaryTests {
        public static void Run() {
            ITDictionary sample = new ITDictionary("About\n\tLicense.\n\nwidget\n\tFirst line\n\tsecond line.\n\n\tSecond paragraph.\n\nalias\n\t{widget}\n\nloop\n\t{loop}\n",
                "# comment\nтермин|синоним — Пояснение\nошибка\nдругой\tОпределение\tвариант|имя\n");
            Require(sample.Find("ALIAS").Body == "First line second line.\n\nSecond paragraph.", "alias/paragraphs");
            Require(sample.Find("синоним").Head == "термин", "Russian alias");
            Require(sample.Find("имя").Body == "Определение", "tab aliases");
            Require(sample.Find("loop") != null && sample.Find("missing") == null, "cycle/missing");
            Require(sample.IsWord("widget", false) && !sample.IsWord("widget", true), "language");
            Require(!sample.IsWord("имя", true), "short abbreviation safety");
            Require(sample.Search("wid", 10).Count > 0, "search");
            ITDictionary bundled = ITDictionary.Load();
            Require(bundled.Count > 15000, "complete bundled dictionary");
            Require(bundled.Find("API") != null && bundled.Find("ПО") != null, "bundled abbreviations");
            Require(bundled.Find("GNU Free Documentation License").Body.Length > 10000, "license");
            Require(bundled.IsWord("виртуалка", true) && !bundled.IsWord("API", false), "safe recognition");
            Console.WriteLine("IT dictionary: " + bundled.Count + " names; tests passed");
        }
        private static void Require(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("IT dictionary test: " + name);
        }
        public static int RunWindowTest() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int result = 1;
            using (ITDictionaryWindow window = new ITDictionaryWindow(ITDictionary.Load()))
            using (Timer timer = new Timer()) {
                timer.Interval = 400;
                window.Shown += delegate { window.SearchFor("API"); timer.Start(); };
                timer.Tick += delegate {
                    timer.Stop(); result = window.ArticleVisibleForTest ? 0 : 1; window.Close();
                };
                Application.Run(window);
            }
            Console.WriteLine("Dictionary window test: " + result);
            return result;
        }
    }
}
