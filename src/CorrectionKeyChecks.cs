using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class CorrectionKeyChecks {
        private static void Set(TrayApp app, string name, object value) {
            typeof(TrayApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, value);
        }
        private static void Send(params int[] codes) {
            Native.Input[] keys = new Native.Input[codes.Length];
            for (int i = 0; i < codes.Length; i++) {
                keys[i].type = 1; keys[i].data.key.vk = (ushort)Math.Abs(codes[i]);
                if (Math.Abs(codes[i]) == 163 || Math.Abs(codes[i]) == 165) keys[i].data.key.flags = 1;
                if (codes[i] < 0) keys[i].data.key.flags |= Native.KEYEVENTF_KEYUP;
            }
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length)
                throw new Exception("Input injection failed");
        }
        private static void Check(bool value, string label) {
            Console.WriteLine(label + ": " + value); if (!value) throw new Exception(label);
        }
        private static async Task Focus(Form form, TextBox box) {
            uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
            bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
            Native.ShowWindow(form.Handle, 1); Native.SetForegroundWindow(form.Handle); form.ActiveControl = box; box.Select(); box.Focus();
            if (attached) Native.AttachThreadInput(current, previous, false);
            await Task.Delay(150); Check(box.Focused && Native.GetForegroundWindow() == form.Handle, "fixture focus");
        }
        private static async Task TypeCodeFixture(Form form, string text) {
            IntPtr english = Native.LoadKeyboardLayout("00000409", 0), ignored;
            Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english, 2, 150, out ignored);
            foreach (char c in text) {
                int vk = c == '`' ? 192 : c == ' ' ? 32 : Char.ToUpperInvariant(c);
                Send(vk, -vk); await Task.Delay(30);
            }
            await Task.Delay(600);
        }
        internal static int Run() {
            Program.TestMode = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp(); Set(app, "autoEnabled", false); Set(app, "typoEnabled", false);
            int result = 0;
            using (Form form = new Form { Text = "Rasklad correction key test", Size = new Size(500, 160) })
            using (TextBox box = new TextBox { Dock = DockStyle.Top })
            using (TextBox password = new TextBox { Dock = DockStyle.Bottom, UseSystemPasswordChar = true }) {
                form.Controls.Add(box); form.Controls.Add(password);
                form.Shown += async delegate {
                    try {
                        uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                        bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
                        Native.SetForegroundWindow(form.Handle); box.Focus();
                        if (attached) Native.AttachThreadInput(current, previous, false);
                        await Task.Delay(150); Check(box.Focused, "fixture focus");
                        foreach (int vk in new[] { 19, 164, 165, 162, 163 }) {
                            await Focus(form, box); Set(app, "correctionKey", vk); app.ResetContextForTest(); box.Text = "ghbdtn"; box.SelectAll();
                            Send(vk, -vk); await Task.Delay(400); Check(box.Text == "привет", "selectable key " + vk);
                        }
                        Check(!TrayApp.ValidCorrectionKey(0) && TrayApp.ValidCorrectionKey(19) && TrayApp.ValidCorrectionKey(163) &&
                            !TrayApp.ValidCorrectionKey(17) && !TrayApp.ValidCorrectionKey(65) && !TrayApp.ValidCorrectionKey(-1), "settings key validation");
                        string settings = Path.Combine(Path.GetTempPath(), "Rasklad-keys-" + Guid.NewGuid().ToString("N") + ".txt");
                        FieldInfo pathField = typeof(TrayApp).GetField("settingsPath", BindingFlags.Instance | BindingFlags.NonPublic);
                        string originalPath = (string)pathField.GetValue(app);
                        try {
                            pathField.SetValue(app, settings); Set(app, "correctionKey", 163);
                            Program.TestMode = false;
                            typeof(TrayApp).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
                            Program.TestMode = true; Set(app, "correctionKey", 19);
                            typeof(TrayApp).GetMethod("LoadSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
                            Check((int)typeof(TrayApp).GetField("correctionKey", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app) == 163, "settings round trip");
                            File.WriteAllText(settings, "correctionKey=65\ncorrectionKey=garbage\n");
                            typeof(TrayApp).GetMethod("LoadSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
                            Check((int)typeof(TrayApp).GetField("correctionKey", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app) == 163, "invalid settings ignored");
                        } finally { Program.TestMode = true; pathField.SetValue(app, originalPath); if (File.Exists(settings)) File.Delete(settings); }
                        Set(app, "correctionKey", 163); // Right Ctrl
                        app.ResetContextForTest(); box.Text = "ghbdtn"; box.SelectAll();
                        Send(19, -19); await Task.Delay(200);
                        Check(box.Text == "ghbdtn", "unselected Pause does not correct");
                        Send(163, 118, -118, -163); await Task.Delay(200);
                        Check(box.Text == "ghbdtn", "Ctrl Right Ctrl retains application shortcut");
                        Send(160, 163, -163, -160); await Task.Delay(200);
                        Check(box.Text == "ghbdtn", "Shift Right Ctrl retains application shortcut");
                        for (int tap = 0; tap < 4; tap++) {
                            Send(163, -163); await Task.Delay(500);
                            Check(box.Text == (tap % 2 == 0 ? "привет" : "ghbdtn"), "selection repeat " + tap);
                        }
                        Send(163); await Task.Delay(500); Send(163); await Task.Delay(250);
                        Check(box.Text == "ghbdtn", "held Ctrl waits for release"); Send(-163); await Task.Delay(500); Check(box.Text == "привет", "released Ctrl corrects once");
                        for (int spaces = 0; spaces <= 1; spaces++) {
                            app.ResetContextForTest(); box.Clear();
                            IntPtr english = Native.LoadKeyboardLayout("00000409", 0), ignored;
                            Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english, 2, 150, out ignored);
                            foreach (char c in "ghbdtn" + (spaces == 1 ? " " : "")) { int vk = c == ' ' ? 32 : Char.ToUpperInvariant(c); Send(vk, -vk); await Task.Delay(35); }
                            for (int tap = 0; tap < 4; tap++) {
                                Send(163, -163); await Task.Delay(500);
                                Check(box.Text == (tap % 2 == 0 ? "привет" : "ghbdtn") + (spaces == 1 ? " " : ""), "word/spaces " + spaces + "/" + tap);
                            }
                        }
                        app.ResetContextForTest(); await Focus(form, password); password.Text = "ghbdtn"; password.SelectAll();
                        Send(163, -163); await Task.Delay(250); Check(password.Text == "ghbdtn", "password untouched");
                        await Focus(form, box); app.ResetContextForTest(); box.Text = "ghbdtn"; box.SelectAll();
                        Set(app, "correctionKey", 19); Send(163, -163); await Task.Delay(250); Check(box.Text == "ghbdtn", "unselected Ctrl unchanged");
                        Set(app, "copilotEnabled", true); box.SelectAll(); Send(91, 160, 134, -134, -160, -91); await Task.Delay(600);
                        Check(box.Text == "привет", "Copilot remains available");
                        Set(app, "autoEnabled", true); Set(app, "codeProtectionEnabled", true);
                        app.ResetContextForTest(); box.Text = "``` "; box.SelectionStart = box.TextLength;
                        await TypeCodeFixture(form, "ghbdtn ");
                        Check(box.Text == "``` ghbdtn ", "existing code fence survives typing reset");
                        app.ResetContextForTest(); box.Clear(); await TypeCodeFixture(form, "``` ");
                        box.Clear(); // Rich editors consume the fence when they create a code block.
                        await TypeCodeFixture(form, "ghbdtn ");
                        Check(box.Text == "ghbdtn ", "converted rich code block protected");
                        box.Clear(); // Submitted/cleared message: the old typed suffix no longer matches.
                        await TypeCodeFixture(form, "ghbdtn ");
                        Check(box.Text == "привет ", "new message not stuck in old code block");
                        app.ResetContextForTest(); box.Clear();
                        await TypeCodeFixture(form, "`ghbdtn rfr ltkf` ghbdtn ");
                        Check(box.Text == "`ghbdtn rfr ltkf` привет ", "closing inline word unchanged; outside word corrected");

                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { Send(-163, -134, -160, -91); form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
