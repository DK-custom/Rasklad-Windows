using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace RaskladWindows {
    sealed class ReplacementRules {
        private readonly Dictionary<string, string> words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string Key(string word, bool russian) { return (russian ? "ru:" : "en:") + word; }
        private static bool Valid(string word, bool russian) {
            if (String.IsNullOrEmpty(word) || word.Length > 64) return false;
            foreach (char c in word) if (russian ? !(c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё') :
                !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')) return false;
            return true;
        }
        internal bool TryGet(string word, bool russian, out string value) { return words.TryGetValue(Key(word, russian), out value); }
        internal void Remove(string word, bool russian) { words.Remove(Key(word, russian)); }
        internal static bool CanSet(string word, bool russian, string replacement) {
            return Valid(word, russian) && Valid(replacement, russian) && !word.Equals(replacement, StringComparison.OrdinalIgnoreCase);
        }
        internal void Set(string word, bool russian, string replacement) {
            if (!CanSet(word, russian, replacement))
                throw new ArgumentException("Недопустимая пара замены");
            words[Key(word, russian)] = replacement;
        }
        internal void Load(string path) {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
                string[] parts = line.Split('\t');
                if (parts.Length != 3 || (parts[0] != "ru" && parts[0] != "en")) continue;
                try { Set(parts[1], parts[0] == "ru", parts[2]); } catch (ArgumentException) { }
            }
        }
        internal void Save(string path) {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in words)
                lines.Add(pair.Key.Substring(0, 2) + "\t" + pair.Key.Substring(3) + "\t" + pair.Value);
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllLines(temporary, lines, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

}
