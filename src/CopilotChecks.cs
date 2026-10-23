using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class CopilotChecks {
        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private static int startMenuActivations;
        private static readonly WinEventProc callback = delegate(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time) {
            try {
                uint pid; Native.GetWindowThreadProcessId(window, out pid);
                string name = Process.GetProcessById((int)pid).ProcessName;
                if (name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)) startMenuActivations++;
            } catch { }
        };
        private static void Send(params int[] codes) {
            SendRaw(0x123, codes);
        }
        private static void SendRaw(uint marker, params int[] codes) {
            Native.Input[] keys = new Native.Input[codes.Length];
            for (int i = 0; i < codes.Length; i++) {
                keys[i].type = 1; keys[i].data.key.vk = (ushort)Math.Abs(codes[i]);
                keys[i].data.key.extraInfo = new UIntPtr(marker);
                if (codes[i] < 0) keys[i].data.key.flags = Native.KEYEVENTF_KEYUP;
            }
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length) throw new Exception("Input injection failed");
        }
        private static void Set(TrayApp app, string name, bool value) {
            typeof(TrayApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, value);
        }
        private static async Task Chord(string mode) {
            if (mode == "fast") Send(91, 160, 134, -134, -160, -91);
            else if (mode == "delayed") { Send(91, 160, 134, -134); await Task.Delay(80); Send(-160, -91); }
            else if (mode == "win-first") Send(91, 160, 134, -134, -91, -160);
            else if (mode == "repeat") Send(91, 160, 134, 134, -134, -160, -91);
            else Send(91, 160, 134, -160, -91, -134);
        }
        private static async Task Focus(Form form, TextBox box) {
            uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
            bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
            Native.ShowWindow(form.Handle, 1); Native.SetForegroundWindow(form.Handle);
            form.ActiveControl = box; box.Select(); box.Focus();
            if (attached) Native.AttachThreadInput(current, previous, false);
            await Task.Delay(150);
            if (!box.Focused || Native.GetForegroundWindow() != form.Handle) throw new Exception("Fixture focus failed");
        }
        private static void Check(bool valid, string name) {
            Console.WriteLine(name + ": " + valid + " startMenu=" + startMenuActivations);
            if (!valid || startMenuActivations != 0) throw new Exception(name);
        }
        private static async Task Type(TrayApp app, Form form, TextBox box, string text) {
            app.ResetContextForTest(); box.Clear(); startMenuActivations = 0;
            IntPtr english = (IntPtr)typeof(TrayApp).GetField("englishLayout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
            IntPtr ignored; Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english, 2, 150, out ignored);
            foreach (char c in text) {
                int vk = c == ' ' ? 32 : Char.ToUpperInvariant(c);
                SendRaw(0, vk, -vk);
                if (c != ' ') await Task.Delay(40);
            }
        }
        internal static int Run() {
            Program.TestMode = true;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            Set(app, "copilotEnabled", true); Set(app, "autoEnabled", false);
            int result = 0;
            IntPtr events = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            if (events == IntPtr.Zero) throw new Exception("Foreground event monitoring failed");
            using (Form form = new Form { Text = "Rasklad Copilot test", Size = new Size(500, 160) })
            using (TextBox box = new TextBox { Dock = DockStyle.Fill }) {
                form.Controls.Add(box);
                form.Shown += async delegate {
                    try {
                        foreach (string mode in new[] { "fast", "delayed", "win-first", "repeat", "f23-last" }) {
                            await Focus(form, box); app.ResetContextForTest();
                            box.Text = "ghbdtn"; box.SelectAll(); startMenuActivations = 0;
                            await Chord(mode);
                            await Task.Delay(800);
                            bool text = box.Text == "привет", focus = Native.GetForegroundWindow() == form.Handle && box.Focused;
                            Console.WriteLine(mode + ": text=" + text + " focus=" + focus + " startMenu=" + startMenuActivations);
                            if (!text || !focus || startMenuActivations != 0) result = 1;
                            await Chord(mode); await Task.Delay(800);
                            bool undo = box.Text == "ghbdtn" && Native.GetForegroundWindow() == form.Handle && box.Focused;
                            Console.WriteLine(mode + ": undo=" + undo + " startMenu=" + startMenuActivations);
                            if (!undo || startMenuActivations != 0) result = 1;
                            await Chord(mode); await Task.Delay(800);
                            bool again = box.Text == "привет" && Native.GetForegroundWindow() == form.Handle && box.Focused;
                            Console.WriteLine(mode + ": again=" + again + " startMenu=" + startMenuActivations);
                            if (!again || startMenuActivations != 0) result = 1;
                        }
                        Set(app, "typoEnabled", false);
                        foreach (string input in new[] { "ghbdtn", "ghbdtn ", "b ", "foo ghbdtn " }) {
                            await Focus(form, box); await Type(app, form, box, input);
                            string mapped = input.Replace("ghbdtn", "привет").Replace("b ", "и ");
                            // No delay after the trailing space: exercise pending dictionary work.
                            for (int tap = 0; tap < 6; tap++) {
                                await Chord("fast"); await Task.Delay(700);
                                Check(box.Text == (tap % 2 == 0 ? mapped : input) && box.Focused &&
                                    Native.GetForegroundWindow() == form.Handle, "word " + input.Length + "/" + tap);
                            }
                        }
                        app.ResetContextForTest(); app.ForceAutomationSelectionForTest = true;
                        box.Text = "ghbdtn"; box.SelectAll(); startMenuActivations = 0;
                        for (int tap = 0; tap < 4; tap++) {
                            await Chord("fast"); await Task.Delay(800);
                            Check(box.Text == (tap % 2 == 0 ? "привет" : "ghbdtn"), "UIA repeat " + tap);
                        }
                        app.ForceAutomationSelectionForTest = false;
                        foreach (bool typo in new[] { false, true }) {
                            Set(app, "autoEnabled", !typo); Set(app, "typoEnabled", typo);
                            await Type(app, form, box, typo ? "helllo " : "ghbdtn "); await Task.Delay(700);
                            string corrected = typo ? "hello " : "привет ", original = typo ? "helllo " : "ghbdtn ";
                            Check(box.Text == corrected, "automatic " + typo);
                            for (int tap = 0; tap < 4; tap++) {
                                await Chord("fast"); await Task.Delay(700);
                                Check(box.Text == (tap % 2 == 0 ? original : corrected), "automatic repeat " + typo + "/" + tap);
                            }
                            Check(!app.HasCustomWordForTest("helllo", false), "repeated toggles do not learn typos");
                        }
                        app.ResetContextForTest(); box.Text = "ghbdtn"; box.SelectAll();
                        SendRaw(0, 162, -162, 163, -163); await Task.Delay(300);
                        Check(box.Text == "ghbdtn", "Ctrl has no correction action");
                        await Focus(form, box); app.ResetContextForTest(); box.UseSystemPasswordChar = true;
                        box.Text = "ghbdtn"; box.SelectAll(); startMenuActivations = 0;
                        await Chord("fast"); await Task.Delay(800);
                        bool password = box.Text == "ghbdtn" && Native.GetForegroundWindow() == form.Handle && box.Focused && startMenuActivations == 0;
                        Console.WriteLine("password protected=" + password + " startMenu=" + startMenuActivations);
                        if (!password) result = 1;
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { Send(-134, -160, -91); UnhookWinEvent(events); form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
