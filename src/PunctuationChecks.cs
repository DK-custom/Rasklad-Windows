using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class PunctuationChecks {
        private static void Set(TrayApp app, string name, bool value) {
            typeof(TrayApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, value);
        }
        private static void Send(params int[] codes) {
            Native.Input[] keys = new Native.Input[codes.Length];
            for (int i = 0; i < codes.Length; i++) {
                keys[i].type = 1; keys[i].data.key.vk = (ushort)Math.Abs(codes[i]);
                if (codes[i] < 0) keys[i].data.key.flags = Native.KEYEVENTF_KEYUP;
            }
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length) throw new Exception("Input injection failed");
        }
        private static void Check(bool valid, string label) { Console.WriteLine(label + ": " + valid); if (!valid) throw new Exception(label); }
        private static async Task Type(TrayApp app, Form form, TextBox box, string text, bool russian = false) {
            box.Clear(); app.ResetContextForTest();
            IntPtr layout = (IntPtr)typeof(TrayApp).GetField(russian ? "russianLayout" : "englishLayout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
            IntPtr ignored; Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout, 2, 150, out ignored);
            foreach (char c in text) {
                if (c == '\n') { Send(13, -13); await Task.Delay(35); continue; }
                short key = Native.VkKeyScanEx(c, layout);
                if (key == -1 || (key & 0x600) != 0) throw new Exception("Unmappable fixture character");
                int vk = key & 0xff;
                if ((key & 0x100) != 0) Send(160);
                Send(vk, -vk);
                if ((key & 0x100) != 0) Send(-160);
                await Task.Delay(35);
            }
            await Task.Delay(500);
        }
        private static async Task Copilot() { Send(91, 160, 134, -134, -160, -91); await Task.Delay(600); }
        internal static int Run() {
            Program.TestMode = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp(); app.SetAutoEnabledForTest(false); Set(app, "copilotEnabled", true);
            int result = 0;
            using (Form form = new Form { Text = "Rasklad punctuation test", Size = new Size(550, 180) })
            using (TextBox box = new TextBox { Dock = DockStyle.Fill, Multiline = true })
            using (TextBox other = new TextBox { Dock = DockStyle.Bottom, Text = "safe" }) {
                form.Controls.Add(box); form.Controls.Add(other);
                form.Shown += async delegate {
                    try {
                        foreach (char mark in ",.!?:;)»”") Check(TrayApp.SpaceBeforePunctuationForTest("слово ", mark), "rule positive " + mark);
                        foreach (string text in new[] { "", " ", "word", "word  ", "word\n", "word, " })
                            Check(!TrayApp.SpaceBeforePunctuationForTest(text, ','), "rule negative " + text.Replace("\n", "\\n"));
                        Check(!TrayApp.SpaceBeforePunctuationForTest("word ", '—') && !TrayApp.SpaceBeforePunctuationForTest("word ", '('), "opening/dash rule unchanged");
                        uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                        bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
                        Native.SetForegroundWindow(form.Handle); box.Focus();
                        if (attached) Native.AttachThreadInput(current, previous, false);
                        await Task.Delay(150); Check(box.Focused, "focus");
                        foreach (char mark in ",.!?:;)") {
                            string input = "word " + mark;
                            await Type(app, form, box, input);
                            Check(box.Text == "word" + mark, "remove space " + mark);
                            await Copilot(); Check(box.Text == input, "undo punctuation " + mark);
                            await Copilot(); Check(box.Text == input, "second Copilot leaves punctuation unchanged " + mark);
                        }
                        await Type(app, form, box, "слово ,", true); Check(box.Text == "слово,", "Russian comma");
                        foreach (string input in new[] { "word  ,", "word (", "word -", "word, :", " ,", ",", "word,", "word\n," }) {
                            await Type(app, form, box, input);
                            Check(box.Text.Replace("\r\n", "\n") == input, "unchanged " + input.Replace("\n", "\\n"));
                        }
                        await Type(app, form, box, "5 ."); Check(box.Text == "5.", "digit before punctuation");
                        await Type(app, form, box, "word ,x"); Check(box.Text == "word,x", "continued typing preserved");
                        await Type(app, form, box, "word "); Send(188, -188, 88, -88); await Task.Delay(600);
                        Check(box.Text == "word,x", "queued next key preserved");
                        await Type(app, form, box, "word "); box.SelectAll(); Send(188, -188); await Task.Delay(600);
                        Check(box.Text == ",", "selection receives original punctuation");
                        await Type(app, form, box, "word "); Send(188, -188); other.Focus(); await Task.Delay(600);
                        Check(other.Text == "safe", "focus change does not edit another field"); box.Focus();
                        Set(app, "typoEnabled", false); await Type(app, form, box, "word ,"); Check(box.Text == "word ,", "typos disabled"); Set(app, "typoEnabled", true);
                        app.SetCurrentAppExcludedForTest(true); await Type(app, form, box, "word ,"); Check(box.Text == "word ,", "excluded app"); app.SetCurrentAppExcludedForTest(false);
                        foreach (string input in new[] { "`word ,", "```word ," }) {
                            await Type(app, form, box, input); Check(box.Text == input, "Markdown code protected");
                        }
                        box.Multiline = false; box.UseSystemPasswordChar = true;
                        await Type(app, form, box, "word ,"); Check(box.Text == "word ,", "password field untouched");
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { Send(-91, -160); form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
